using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mirage.Editor.Tests;

/// <summary>
/// What happens to a request whose answer never comes.
///
/// <para>Every request awaits a <c>TaskCompletionSource</c> that only the receive loop can complete, and
/// callers pass no cancellation token. So anything that ends the loop — the server closing the socket, a
/// read throwing — has to release the waiters itself, or the await never returns and the UI operation
/// that started it is stuck with no way out but restarting the editor.</para>
/// </summary>
[TestFixture]
public class EditorConnectionPendingTests
{
    /// <summary>How long a released caller is given before the test calls it a lock-up.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A reader the test drives line by line, and can close on demand — the server end of the
    /// wire, standing in for a socket.</summary>
    private sealed class ScriptedReader : TextReader
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _lines = new();

        public void Send(string line) => _lines.Add(line);
        public void CloseStream() => _lines.CompleteAdding();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken ct)
        {
            await Task.Yield();
            try { return _lines.Take(ct); }
            catch (InvalidOperationException) { return null; }   // completed = the far end hung up
        }

        public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();
    }

    private static (EditorConnection conn, ScriptedReader server) Connect()
    {
        var conn = new EditorConnection();
        var server = new ScriptedReader();
        conn.AttachTransport(server, new StringWriter(new StringBuilder()));
        return (conn, server);
    }

    /// <summary>Asserts a request was released. The failure being pinned here IS an unbounded wait, so
    /// this must not reproduce it by waiting unboundedly itself: a regression has to fail the run, not
    /// hang it.</summary>
    private static async Task AssertReleased(Task request)
    {
        Assert.That(await Task.WhenAny(request, Task.Delay(Patience)), Is.SameAs(request),
            "the caller is still waiting on an answer that can never arrive — this is the lock-up");
        Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>(),
            "and it is told so, rather than completing with a silent null");
    }

    /// <summary>The reported lock-up: a request outstanding when the connection dies.</summary>
    [Test]
    public async Task ARequestInFlight_IsReleasedWhenTheConnectionDrops()
    {
        var (conn, server) = Connect();

        var request = conn.RequestItemAsync(itemNum: 1);
        Assert.That(request.IsCompleted, Is.False, "precondition: it is waiting on a response");

        server.CloseStream();                            // the server hangs up
        await conn.ReceiveLoop!.WaitAsync(Patience);     // the loop notices and ends

        await AssertReleased(request);
    }

    /// <summary>A second bulk request for the same response command displaces the first. The displaced
    /// caller is still awaiting, so it has to be released too.</summary>
    [Test]
    public async Task ADisplacedBulkRequest_IsReleased()
    {
        var (conn, _) = Connect();

        var first = conn.RequestAllItemsAsync();
        var second = conn.RequestAllItemsAsync();

        await AssertReleased(first);
        Assert.That(second.IsCompleted, Is.False, "while the newer one is still live");
    }

    [Test]
    public async Task DisconnectAlsoReleasesAWaitingRequest()
    {
        var (conn, _) = Connect();

        var request = conn.RequestItemAsync(itemNum: 1);
        await conn.DisconnectAsync();

        await AssertReleased(request);
    }

    /// <summary>The happy path, so the releases above cannot be mistaken for "everything is cancelled".</summary>
    [Test]
    public async Task AResponseStillCompletesItsRequest()
    {
        var (conn, server) = Connect();

        var request = conn.RequestItemAsync(itemNum: 7);
        server.Send(PacketSerializer.Serialize(new UpdateItemPacket { ItemNum = 7 }).TrimEnd('\n'));

        var packet = await request.WaitAsync(Patience);

        Assert.That(packet, Is.Not.Null);
        Assert.That(packet!.ItemNum, Is.EqualTo(7));
    }
}
