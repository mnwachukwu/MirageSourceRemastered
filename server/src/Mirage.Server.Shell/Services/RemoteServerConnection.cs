using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace Mirage.Server.Shell.Services;

/// <summary>
/// A server on another machine, reached over its management port.
///
/// <para>The same two streams a child process gives you, over a socket: command lines out, console lines
/// in. Nothing here knows what a command means — that stays on the server, where the console lives.</para>
///
/// <para>Stopping DETACHES. A remote server is not this shell's to end, and the operator who wants it
/// down has <c>/shutdown</c> for that, which is the honest way to say so.</para>
/// </summary>
public sealed class RemoteServerConnection(string host, int port, string token) : IServerConnection
{
    /// <summary>The line the server sends once the token is accepted.</summary>
    private const string HandshakeOk = "MIRAGE-MANAGEMENT-OK";

    /// <summary>How long to wait for the connection and the handshake before giving up.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly object _lock = new();
    private TcpClient? _client;
    private SslStream? _ssl;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public event Action<string>? OutputReceived;
    public event Action<ServerState>? StateChanged;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>False: a process on another machine is not this shell's to start or stop.</summary>
    public bool CanSupervise => false;

    public string Host { get; } = host;
    public int Port { get; } = port;

    /// <summary>Connects and presents the token. Returns null on success, or a message to show.</summary>
    public async Task<string?> StartAsync()
    {
        if (State != ServerState.Stopped) return null;

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = new CancellationTokenSource(ConnectTimeout);
            await client.ConnectAsync(Host, Port, timeout.Token).ConfigureAwait(false);

            // Trust anything: the server presents a throwaway self-signed certificate, exactly as it does
            // to the game client and the editor. This buys encryption, not identity.
            var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync("mirage-server").ConfigureAwait(false);

            var reader = new StreamReader(ssl, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(ssl, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(token).ConfigureAwait(false);
            string? greeting = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (greeting != HandshakeOk)
            {
                // A refusal closes without saying why, so this is the shell's reading of the silence
                // rather than anything the server said.
                ssl.Dispose();
                client.Dispose();
                return RemoteError.Rejected;
            }

            lock (_lock)
            {
                _client = client;
                _ssl = ssl;
                _writer = writer;
                _cts = new CancellationTokenSource();
            }

            _ = ReadLoopAsync(reader, _cts.Token);
            SetState(ServerState.Running);
            return null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException
                                      or OperationCanceledException)
        {
            client.Dispose();
            return RemoteError.Unreachable;
        }
    }

    /// <summary>What went wrong, as a token the view model turns into a translated sentence — this layer
    /// has no business holding display text.</summary>
    public static class RemoteError
    {
        public const string Rejected = nameof(Rejected);
        public const string Unreachable = nameof(Unreachable);
    }

    public void SendCommand(string line)
    {
        lock (_lock)
        {
            if (_writer is null) return;
            try { _writer.WriteLine(line); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { Teardown(); }
        }
    }

    /// <summary>Detaches. The server keeps running; it was never this shell's process.</summary>
    public Task StopAsync()
    {
        Teardown();
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;   // the server closed
                OutputReceived?.Invoke(line);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            reader.Dispose();
            Teardown();
        }
    }

    private void Teardown()
    {
        lock (_lock)
        {
            if (_client is null) return;
            _cts?.Cancel();
            _cts?.Dispose();
            _writer?.Dispose();
            _ssl?.Dispose();
            _client.Dispose();
            _cts = null;
            _writer = null;
            _ssl = null;
            _client = null;
        }
        SetState(ServerState.Stopped);
    }

    private void SetState(ServerState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose() => Teardown();
}
