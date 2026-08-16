using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Shell.Bench;

/// <summary>
/// One fake player, over the REAL transport.
///
/// <para>Deliberately not an in-process shortcut. A load figure is only worth having if it includes what
/// a connection actually costs: the TLS handshake, the per-packet encryption, the JSON parse and the
/// dispatch hop onto the game thread. Calling the handlers directly would measure a server nobody runs.
/// The framing mirrors <c>TcpClientTransport</c> exactly — newline-delimited JSON over
/// <see cref="SslStream"/>, permissive certificate check, because the server's certificate is
/// self-signed.</para>
///
/// <para>Each instance owns a throwaway account and one character. They are created against a SCRATCH
/// world, never the operator's — see <see cref="LoadBenchmark"/>.</para>
/// </summary>
public sealed class SimulatedPlayer : IDisposable
{
    private readonly string _login;
    private TcpClient? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private volatile bool _inGame;
    private volatile bool _dropped;
    private int _direction;

    public SimulatedPlayer(int ordinal)
    {
        // Long enough to clear the minimum-length rule, and ordinal-keyed so a failure names the client.
        _login = $"bench{ordinal:D5}";
    }

    /// <summary>True once the server has answered with <c>ingame</c> — the only honest signal that this
    /// connection is costing what a real player costs. A socket that connected but never got in is load
    /// on the acceptor and nothing else.</summary>
    public bool IsInGame => _inGame;

    /// <summary>The connection died on its own. Under a ramp this is the symptom that matters most: the
    /// server stopped keeping up rather than refusing politely.</summary>
    public bool Dropped => _dropped;

    public string? FailureReason { get; private set; }

    /// <summary>Connects, creates an account and a character, and enters the world. Returns false with
    /// <see cref="FailureReason"/> set rather than throwing, because a ramp expects to hit a wall and
    /// wants to record where.</summary>
    public async Task<bool> JoinAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, _cts.Token).ConfigureAwait(false);

            var ssl = new SslStream(_client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync("mirage-server").ConfigureAwait(false);
            _writer = new StreamWriter(ssl, System.Text.Encoding.UTF8) { AutoFlush = true };
            var reader = new StreamReader(ssl, System.Text.Encoding.UTF8);

            _ = ReceiveLoopAsync(reader, _cts.Token);

            Send(new NewAccountPacket { Username = _login, Password = Password });
            if (!await WaitFor(() => _accountReady, ct).ConfigureAwait(false))
                return Fail("account was never confirmed");

            Send(new LoginPacket
            {
                Username = _login,
                Password = Password,
                Major = Constants.ClientMajor,
                Minor = Constants.ClientMinor,
                Revision = Constants.ClientRevision,
            });
            if (!await WaitFor(() => _charsReady, ct).ConfigureAwait(false))
                return Fail("login produced no character list");

            Send(new AddCharPacket { Name = _login, Sex = Sex.Male, Class = 1 });
            Send(new UseCharPacket { Slot = 1 });
            if (!await WaitFor(() => _inGame, ct).ConfigureAwait(false))
                return Fail("never reached in-game");

            return true;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>One beat of ordinary play. Movement rather than idling, because a parked connection costs
    /// almost nothing and would flatter the result: walking is what drives collision checks, observer
    /// resolution and the broadcast fan-out that the game thread actually spends its time on.</summary>
    public void Act()
    {
        if (!_inGame) return;
        _direction = (_direction + 1) & 3;
        Send(new PlayerMovePacket { Dir = (Direction)_direction, Movement = MovementType.Walking });
    }

    private const string Password = "benchmark-password";

    private volatile bool _accountReady;
    private volatile bool _charsReady;

    private async Task ReceiveLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) { _dropped = true; return; }
                if (line.Length == 0) continue;
                Observe(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { _dropped = true; }
        finally { _inGame = false; }
    }

    /// <summary>Reads only the <c>cmd</c> discriminator and the alert code. Deserializing every packet
    /// into its record would put the BENCH under measurable load, and a fake player has no use for the
    /// contents — only for knowing which stage of the handshake it has reached.</summary>
    private void Observe(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("cmd", out var cmd)) return;
            switch (cmd.GetString())
            {
                case PacketNames.AlertMsg:
                    // An existing account answers with a code too; either way the login can proceed.
                    if (doc.RootElement.TryGetProperty("code", out var code))
                        _accountReady |= code.GetInt32() is (int)AlertCode.AccountCreated;
                    break;
                case PacketNames.SendChars:
                    _charsReady = true;
                    break;
                case PacketNames.PlayerInGame:
                    _inGame = true;
                    break;
            }
        }
        catch (JsonException) { /* a partial or unexpected line is not the bench's problem */ }
    }

    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Polls a stage flag. The timeout is generous on purpose: under a heavy ramp the server is
    /// slow, not broken, and calling that a failure would report the bench's impatience as the server's
    /// limit.</summary>
    private static async Task<bool> WaitFor(Func<bool> done, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StageTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (done()) return true;
            await Task.Delay(25, ct).ConfigureAwait(false);
        }
        return done();
    }

    private bool Fail(string reason)
    {
        FailureReason = reason;
        return false;
    }

    private void Send(IPacket packet)
    {
        try { _writer?.WriteLine(PacketSerializer.Serialize(packet).TrimEnd('\n')); }
        catch { _dropped = true; }
    }

    public void Dispose()
    {
        _inGame = false;
        try { _cts?.Cancel(); } catch { /* already gone */ }
        _writer?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();
    }
}
