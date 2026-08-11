using Mirage.Editor.Localization;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;

namespace Mirage.Editor.Services;

public sealed class EditorConnection : IDisposable
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    // Pending on-demand record requests keyed by (responseCmd, recordNum)
    private readonly ConcurrentDictionary<(string, int), TaskCompletionSource<IPacket>> _pending = new();
    // Pending bulk (all-records) requests keyed by responseCmd
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IPacket>> _pendingBulk = new();

    public bool IsConnected => _client?.Connected == true;

    public event Action<string>? OnServerMessage;
    public event Action? OnDisconnected;

    /// <summary>A recognized packet the server pushed to us that isn't a pending request's response — i.e. a
    /// SendToAll live broadcast (e.g. UpdateNpc after another editor's save). Fires on the receive-loop thread,
    /// so subscribers MUST marshal any UI work to the UI thread.</summary>
    public event Action<IPacket>? OnLivePacket;

    /// <summary>The outcome of a connect + editor-login handshake. <see cref="Data"/> is non-null only when
    /// <see cref="Success"/> is true; <see cref="Message"/> carries the server's reason on failure and its
    /// greeting on success. Named rather than a four-element tuple so a caller cannot bind the failure
    /// message to the success flag's neighbour and act on the wrong one.</summary>
    public readonly record struct AuthResult(bool Success, string Message, EditorDataPacket? Data, AdminLevel AccessLevel)
    {
        public static AuthResult Failed(string message) => new(false, message, null, AdminLevel.Player);
    }

    public async Task<AuthResult> ConnectAndAuthAsync(string host, int port, string username, string password,
                                                     CancellationToken ct = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, ct);

        var stream = _client.GetStream();
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("mirage-server");
        _reader = new StreamReader(ssl, leaveOpen: true);
        _writer = new StreamWriter(ssl, leaveOpen: true) { AutoFlush = true };

        await _writer.WriteAsync(PacketSerializer.Serialize(new EditorLoginPacket
        {
            Username = username,
            Password = password,
            Locale = AppSettings.Current.Language,
        }));

        var responseLine = await _reader.ReadLineAsync(ct);
        if (responseLine is null)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ClosedUnexpectedly));

        var responsePacket = PacketSerializer.TryDeserialize(responseLine);
        if (responsePacket is not EditorLoginResponsePacket response)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_UnexpectedResponse));

        if (!response.Success)
        {
            await DisconnectAsync();
            return AuthResult.Failed(response.Message);
        }

        var dataLine = await _reader.ReadLineAsync(ct);
        if (dataLine is null)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ClosedBeforeData));

        var dataPacket = PacketSerializer.TryDeserialize(dataLine) as EditorDataPacket;
        if (dataPacket is null)
            return AuthResult.Failed(EditorStrings.Get(EditorStrings.EditorConnection_ExpectedDataPacket));

        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_cts.Token);

        return new AuthResult(true, response.Message, dataPacket, response.AccessLevel);
    }

    public async Task SendSaveAsync(IPacket packet)
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        await _writer.WriteAsync(PacketSerializer.Serialize(packet));
    }

    // ── Bulk fetch (all records of a type in one round-trip) ─────────────────

    public Task<EditorAllItemsPacket?> RequestAllItemsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllItemsPacket>(PacketNames.EditorAllItems, new EditorRequestAllItemsPacket(), ct);

    public Task<EditorAllNpcsPacket?> RequestAllNpcsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllNpcsPacket>(PacketNames.EditorAllNpcs, new EditorRequestAllNpcsPacket(), ct);

    public Task<EditorAllShopsPacket?> RequestAllShopsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllShopsPacket>(PacketNames.EditorAllShops, new EditorRequestAllShopsPacket(), ct);

    public Task<EditorAllQuestsPacket?> RequestAllQuestsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllQuestsPacket>(PacketNames.EditorAllQuests, new EditorRequestAllQuestsPacket(), ct);

    public Task<EditorAllConversationsPacket?> RequestAllConversationsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllConversationsPacket>(PacketNames.EditorAllConversations, new EditorRequestAllConversationsPacket(), ct);

    public Task<EditorAllSpellsPacket?> RequestAllSpellsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllSpellsPacket>(PacketNames.EditorAllSpells, new EditorRequestAllSpellsPacket(), ct);

    public Task<EditorAllClassesPacket?> RequestAllClassesAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllClassesPacket>(PacketNames.EditorAllClasses, new EditorRequestAllClassesPacket(), ct);

    public Task<EditorAllMapGroupsPacket?> RequestAllMapGroupsAsync(CancellationToken ct = default)
        => RequestBulkAsync<EditorAllMapGroupsPacket>(PacketNames.EditorAllMapGroups, new EditorRequestAllMapGroupsPacket(), ct);

    private async Task<T?> RequestBulkAsync<T>(string responseCmd, IPacket request,
                                                CancellationToken ct) where T : class, IPacket
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingBulk[responseCmd] = tcs;
        try
        {
            await _writer.WriteAsync(PacketSerializer.Serialize(request));
            using var reg = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                _pendingBulk.TryRemove(responseCmd, out _);
            });
            return await tcs.Task.ConfigureAwait(false) as T;
        }
        catch
        {
            _pendingBulk.TryRemove(responseCmd, out _);
            throw;
        }
    }

    // ── Per-record lazy fetch ─────────────────────────────────────────────────

    public Task<UpdateItemPacket?> RequestItemAsync(int itemNum, CancellationToken ct = default)
        => RequestAsync<UpdateItemPacket>(
            PacketNames.UpdateItem, itemNum,
            new EditorRequestItemPacket { ItemNum = itemNum }, ct);

    public Task<UpdateNpcPacket?> RequestNpcAsync(int npcNum, CancellationToken ct = default)
        => RequestAsync<UpdateNpcPacket>(
            PacketNames.UpdateNpc, npcNum,
            new EditorRequestNpcPacket { NpcNum = npcNum }, ct);

    public Task<UpdateShopPacket?> RequestShopAsync(int shopNum, CancellationToken ct = default)
        => RequestAsync<UpdateShopPacket>(
            PacketNames.UpdateShop, shopNum,
            new EditorRequestShopPacket { ShopNum = shopNum }, ct);

    public Task<UpdateQuestPacket?> RequestQuestAsync(int questNum, CancellationToken ct = default)
        => RequestAsync<UpdateQuestPacket>(
            PacketNames.UpdateQuest, questNum,
            new EditorRequestQuestPacket { QuestNum = questNum }, ct);

    public Task<UpdateConversationPacket?> RequestConversationAsync(int convNum, CancellationToken ct = default)
        => RequestAsync<UpdateConversationPacket>(
            PacketNames.UpdateConversation, convNum,
            new EditorRequestConversationPacket { ConvNum = convNum }, ct);

    public Task<UpdateSpellPacket?> RequestSpellAsync(int spellNum, CancellationToken ct = default)
        => RequestAsync<UpdateSpellPacket>(
            PacketNames.UpdateSpell, spellNum,
            new EditorRequestSpellPacket { SpellNum = spellNum }, ct);

    public Task<SendMapPacket?> RequestMapAsync(int mapNum, CancellationToken ct = default)
        => RequestAsync<SendMapPacket>(
            PacketNames.SendMap, mapNum,
            new EditorRequestMapPacket { MapNum = mapNum }, ct);

    public Task<UpdateClassPacket?> RequestClassAsync(int classNum, CancellationToken ct = default)
        => RequestAsync<UpdateClassPacket>(
            PacketNames.UpdateClass, classNum,
            new EditorRequestClassPacket { ClassNum = classNum }, ct);

    public Task<UpdateMapGroupPacket?> RequestMapGroupAsync(int groupNum, CancellationToken ct = default)
        => RequestAsync<UpdateMapGroupPacket>(
            PacketNames.UpdateMapGroup, groupNum,
            new EditorRequestMapGroupPacket { GroupNum = groupNum }, ct);

    private async Task<T?> RequestAsync<T>(string responseCmd, int num, IPacket request,
                                           CancellationToken ct) where T : class, IPacket
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");

        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[(responseCmd, num)] = tcs;
        try
        {
            await _writer.WriteAsync(PacketSerializer.Serialize(request));
            using var reg = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                _pending.TryRemove((responseCmd, num), out _);
            });
            return await tcs.Task.ConfigureAwait(false) as T;
        }
        catch
        {
            _pending.TryRemove((responseCmd, num), out _);
            throw;
        }
    }

    // ── Disconnect ────────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _cts = null;
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();
        foreach (var tcs in _pendingBulk.Values)
            tcs.TrySetCanceled();
        _pendingBulk.Clear();
        if (_writer is not null)
        {
            try
            {
                await _writer.FlushAsync();
            }
            catch { }
        }
        _client?.Close();
        _client = null;
        _reader = null;
        _writer = null;
    }

    // ── Background receive loop ───────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        bool unexpected = false;
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line is null)
                {
                    unexpected = true;
                    break;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;

                var packet = PacketSerializer.TryDeserialize(line);
                if (packet is not null)
                {
                    if (TryCompletePending(packet)) continue;
                    // Recognized packet, no pending request waiting on it = a server-pushed live broadcast.
                    OnLivePacket?.Invoke(packet);
                    continue;
                }

                OnServerMessage?.Invoke(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { unexpected = true; }

        if (unexpected) OnDisconnected?.Invoke();
    }

    private bool TryCompletePending(IPacket packet)
    {
        // Bulk responses
        var bulkCmd = packet switch
        {
            EditorAllItemsPacket => PacketNames.EditorAllItems,
            EditorAllNpcsPacket => PacketNames.EditorAllNpcs,
            EditorAllShopsPacket => PacketNames.EditorAllShops,
            EditorAllQuestsPacket => PacketNames.EditorAllQuests,
            EditorAllConversationsPacket => PacketNames.EditorAllConversations,
            EditorAllSpellsPacket => PacketNames.EditorAllSpells,
            EditorAllClassesPacket => PacketNames.EditorAllClasses,
            EditorAllMapGroupsPacket => PacketNames.EditorAllMapGroups,
            _ => "",
        };
        if (bulkCmd != "")
            return _pendingBulk.TryRemove(bulkCmd, out var btcs) && btcs.TrySetResult(packet);

        // Per-record responses
        (string cmd, int num) key = packet switch
        {
            UpdateItemPacket p => (PacketNames.UpdateItem, p.ItemNum),
            UpdateNpcPacket p => (PacketNames.UpdateNpc, p.NpcNum),
            UpdateShopPacket p => (PacketNames.UpdateShop, p.ShopNum),
            UpdateQuestPacket p => (PacketNames.UpdateQuest, p.QuestNum),
            UpdateConversationPacket p => (PacketNames.UpdateConversation, p.ConvNum),
            UpdateSpellPacket p => (PacketNames.UpdateSpell, p.SpellNum),
            SendMapPacket p => (PacketNames.SendMap, p.MapNum),
            UpdateClassPacket p => (PacketNames.UpdateClass, p.ClassNum),
            UpdateMapGroupPacket p => (PacketNames.UpdateMapGroup, p.GroupNum),
            _ => ("", 0),
        };
        if (key.cmd == "") return false;
        return _pending.TryRemove(key, out var tcs) && tcs.TrySetResult(packet);
    }

    public void Dispose() => _client?.Dispose();
}
