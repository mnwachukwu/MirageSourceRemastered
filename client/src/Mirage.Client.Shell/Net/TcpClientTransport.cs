using Mirage.Client.Core.Net;
using Mirage.Shared.Protocol;
using Mirage.Shared.Security;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Mirage.Client.Shell.Net;

/// <summary>
/// TCP transport. Background task reads lines into a queue; the game loop drains it.
/// Implements <see cref="IClientTransport"/>.
/// </summary>
public sealed class TcpClientTransport : IClientTransport, IDisposable
{
    private TcpClient? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<string> _incoming = new();
    private volatile bool _connected;
    private volatile bool _droppedUnexpectedly;

    public bool IsConnected => _connected;
    public bool DroppedUnexpectedly => _droppedUnexpectedly;

    public async Task ConnectAsync(string host, int port, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, _cts.Token);
        _client.NoDelay = true;
        _connected = true;

        var stream = _client.GetStream();
        var pinned = new PinnedServer(ServerPinStore.Store, host, port);
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false, pinned.Validate);
        try
        {
            await ssl.AuthenticateAsClientAsync("mirage-server");
        }
        catch (AuthenticationException ex)
        {
            _connected = false;
            throw pinned.Translate(ex);
        }
        pinned.Commit();
        _writer = new StreamWriter(ssl, System.Text.Encoding.UTF8) { AutoFlush = true };
        var reader = new StreamReader(ssl, System.Text.Encoding.UTF8);

        _ = ReceiveLoopAsync(reader, _cts.Token);
    }

    private async Task ReceiveLoopAsync(StreamReader reader, CancellationToken ct)
    {
        bool unexpected = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    unexpected = true;
                    break;
                }
                if (line.Length > 0) _incoming.Enqueue(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { unexpected = true; }
        finally
        {
            if (unexpected) _droppedUnexpectedly = true;
            _connected = false;
        }
    }

    public void Send(IPacket packet)
    {
        if (_writer is null) return;
        string line = PacketSerializer.Serialize(packet);
        try { _writer.WriteLine(line.TrimEnd('\n')); }
        catch { /* connection may have dropped */ }
    }

    public bool TryDequeue(out string line)
    {
        if (_incoming.TryDequeue(out string? result))
        {
            line = result;
            return true;
        }
        line = "";
        return false;
    }

    public void Disconnect()
    {
        _droppedUnexpectedly = false;
        _connected = false;
        _cts?.Cancel();
        _writer?.Close();
        _client?.Close();
        _client = null;
        _writer = null;
        _cts = null;
    }

    public void Dispose() => Disconnect();
}
