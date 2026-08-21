using Microsoft.Extensions.Logging.Abstractions;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using Mirage.Shared.Security;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>
/// Live-propagation regression guard for the "editor edits must reach already-connected clients" audit.
/// Every editor save has to push its change to online players without a reconnect: the simple entity saves
/// (class/item/npc/shop/spell) broadcast the matching Update*Packet to ALL players; a MapGroup save has no
/// per-client cache to update, so it must instead bump + refresh every member map (the client caches the
/// group's EFFECTIVE values baked into each map, keyed by map revision). These tests lock that contract so a
/// refactor can't silently drop a field (as the NpcRecord.Spd rebuild once did) or a whole broadcast.
/// </summary>
[TestFixture]
public class EditorLiveBroadcastTests
{
    const int Editor = 1;

    // ── Simple entity saves broadcast their Update packet to ALL players ───────────

    [Test]
    public void SaveClass_BroadcastsUpdateClassToAll()
    {
        var h = new Harness();
        h.Save(new EditorSaveClassPacket
        {
            ClassNum = 2, Name = "Warrior", SpriteMale = 7, SpriteFemale = 17, Str = 11, Def = 12, Spd = 13, Int = 14,
        });

        var u = h.Dispatcher.OneBroadcast<UpdateClassPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.ClassNum, Is.EqualTo(2));
            Assert.That(u.Name, Is.EqualTo("Warrior"));
            Assert.That(u.SpriteMale, Is.EqualTo(7));
            Assert.That(u.SpriteFemale, Is.EqualTo(17));
            Assert.That(u.Str, Is.EqualTo(11));
            Assert.That(u.Def, Is.EqualTo(12));
            Assert.That(u.Spd, Is.EqualTo(13));
            Assert.That(u.Int, Is.EqualTo(14));
        });
    }

    [Test]
    public void SaveItem_BroadcastsUpdateItemToAll()
    {
        var h = new Harness();
        h.Save(new EditorSaveItemPacket
        {
            ItemNum = 3, Name = "Short Sword", Pic = 4, Type = ItemType.Weapon,
            Durability = 100, Power = 10, AllowedClasses = [5, 2],
        });

        var u = h.Dispatcher.OneBroadcast<UpdateItemPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.ItemNum, Is.EqualTo(3));
            Assert.That(u.Name, Is.EqualTo("Short Sword"));
            Assert.That(u.Pic, Is.EqualTo(4));
            Assert.That(u.Type, Is.EqualTo(ItemType.Weapon));
            Assert.That(u.Durability, Is.EqualTo(100));
            Assert.That(u.Power, Is.EqualTo(10));
            // Sorted, not as sent: the server canonicalizes the gate before broadcasting it.
            Assert.That(u.AllowedClasses, Is.EqualTo(new short[] { 2, 5 }));
        });
    }

    // The NpcRecord.Spd/EmitsLight/Light fields are the ones a past rebuild dropped: assert they survive the
    // broadcast so the running-NPC slide scaling + light never silently reset to zero on a live edit again.
    [Test]
    public void SaveNpc_BroadcastsUpdateNpcToAll_IncludingSpdAndLight()
    {
        var h = new Harness();
        h.Save(new EditorSaveNpcPacket
        {
            NpcNum = 4, Name = "Guard", Sprite = 21, Size = 1, Behavior = NpcBehavior.Stationary,
            SpawnSecs = 30, Spd = 17, EmitsLight = true, Drops = null,
        });

        var u = h.Dispatcher.OneBroadcast<UpdateNpcPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.NpcNum, Is.EqualTo(4));
            Assert.That(u.Name, Is.EqualTo("Guard"));
            Assert.That(u.Sprite, Is.EqualTo(21));
            Assert.That(u.Size, Is.EqualTo(1));
            Assert.That(u.Behavior, Is.EqualTo(NpcBehavior.Stationary));
            Assert.That(u.SpawnSecs, Is.EqualTo(30));
            Assert.That(u.Spd, Is.EqualTo(17), "Spd must reach the client or the NPC move-slide zeroes out");
            Assert.That(u.EmitsLight, Is.True);
        });
    }

    [Test]
    public void SaveShop_BroadcastsUpdateShopToAll()
    {
        var h = new Harness();
        h.Save(new EditorSaveShopPacket
        {
            ShopNum = 2, Name = "General Store",
            FixesItems = true, ShopType = ShopType.Store, AllowBanking = true, Barters = [],
        });

        var u = h.Dispatcher.OneBroadcast<UpdateShopPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.ShopNum, Is.EqualTo(2));
            Assert.That(u.Name, Is.EqualTo("General Store"));
            Assert.That(u.FixesItems, Is.True);
            Assert.That(u.ShopType, Is.EqualTo(ShopType.Store));
            Assert.That(u.AllowBanking, Is.True);
        });
    }

    [Test]
    public void SaveSpell_BroadcastsUpdateSpellToAll()
    {
        var h = new Harness();
        h.Save(new EditorSaveSpellPacket
        {
            SpellNum = 5, Name = "Fireball", AllowedClasses = [2], Type = SpellType.AddHp,
            VitalAmount = 25,
        });

        var u = h.Dispatcher.OneBroadcast<UpdateSpellPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.SpellNum, Is.EqualTo(5));
            Assert.That(u.Name, Is.EqualTo("Fireball"));
            Assert.That(u.AllowedClasses, Is.EqualTo(new short[] { 2 }));
            Assert.That(u.Type, Is.EqualTo(SpellType.AddHp));
            Assert.That(u.VitalAmount, Is.EqualTo(25));
        });
    }

    // ── MapGroup save: an independent client-cached def — broadcast it, never touch member maps ──

    // A MapGroup is shipped like items/npcs: the client caches it and resolves each member map's effective values
    // against it on demand, so the save just broadcasts UpdateMapGroupPacket to ALL players.
    [Test]
    public void SaveMapGroup_BroadcastsUpdateMapGroupToAll()
    {
        var h = new Harness();
        h.Save(new EditorSaveMapGroupPacket
        {
            GroupNum = 3, Name = "Catacombs", DisplayName = "The Catacombs", Music = 9, Moral = MapMoral.Safe,
        });

        var u = h.Dispatcher.OneBroadcast<UpdateMapGroupPacket>();
        Assert.Multiple(() =>
        {
            Assert.That(u.GroupNum, Is.EqualTo(3));
            Assert.That(u.DisplayName, Is.EqualTo("The Catacombs"));
            Assert.That(u.Music, Is.EqualTo(9));
            Assert.That(u.Moral, Is.EqualTo(MapMoral.Safe));
            Assert.That(h.World.MapGroups[3].Music, Is.EqualTo(9), "the edit was applied to the live record");
        });
    }

    // The load-bearing guarantee: a group edit reaches clients WITHOUT re-sending or bumping any member
    // map, so a revision-bump fan-out across the group is a regression, not an implementation detail.
    [Test]
    public void SaveMapGroup_DoesNotTouchMemberMaps()
    {
        var h = new Harness();
        h.World.Maps[5].MapGroup = 3;   // a member map
        int rev5 = h.World.Maps[5].Revision;

        h.Save(new EditorSaveMapGroupPacket { GroupNum = 3, Name = "Catacombs", Music = 9 });

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Maps[5].Revision, Is.EqualTo(rev5), "member map revision must NOT change");
            Assert.That(h.Persistence.SavedMaps, Is.Empty, "no map is re-persisted on a group save");
        });
    }

    // ── Auth gate: an unauthenticated editor session can neither mutate nor broadcast ──

    [Test]
    public void UnauthenticatedEditor_SaveIsIgnored_NoBroadcastNoMutation()
    {
        var h = new Harness();
        h.Editors.GetSession(Editor)!.IsAuthenticated = false;

        h.Save(new EditorSaveClassPacket { ClassNum = 2, Name = "Warrior", SpriteMale = 7 });

        Assert.Multiple(() =>
        {
            Assert.That(h.Dispatcher.Broadcasts, Is.Empty, "no broadcast without an authenticated editor");
            Assert.That(h.World.Classes[2].Name, Is.Empty, "no mutation without an authenticated editor");
        });
    }

    // ── Access gate: authenticated is not the same as ALLOWED ─────────────────────
    //
    // The handlers used to check authentication alone. session.AdminLevel was set at login and never
    // read again, so a MAPPER — the lowest tier the editor admits — could save items, NPCs, shops,
    // spells, classes, quests and conversations. The editor client hides those sections below Developer,
    // but that is presentation, and this engine ships its client's source.
    //
    // The compiler cannot help here: the old guard and the new one both return bool, so a handler left on
    // the wrong tier looks identical. These assert the boundary from the outside instead.

    [Test]
    public void MapperCannotSaveContent()
    {
        var h = new Harness(AdminLevel.Mapper);

        h.Save(new EditorSaveClassPacket { ClassNum = 2, Name = "Warrior", SpriteMale = 7 });
        h.Save(new EditorSaveItemPacket { ItemNum = 3, Name = "Short Sword" });
        h.Save(new EditorSaveSpellPacket { SpellNum = 5, Name = "Fireball" });

        Assert.Multiple(() =>
        {
            Assert.That(h.World.Classes[2].Name, Is.Empty, "a Mapper must not be able to rewrite a class");
            Assert.That(h.World.Items[3].Name, Is.Empty, "a Mapper must not be able to rewrite an item");
            Assert.That(h.World.Spells[5].Name, Is.Empty, "a Mapper must not be able to rewrite a spell");
            Assert.That(h.Dispatcher.Broadcasts, Is.Empty, "a refused save must not reach players either");
        });
    }

    // The other half of the gate: a Mapper is admitted to the editor precisely to author maps, so tiering
    // must not lock them out of the tool they use every day.
    [Test]
    public void MapperCanStillSaveAMapGroup()
    {
        var h = new Harness(AdminLevel.Mapper);

        h.Save(new EditorSaveMapGroupPacket { GroupNum = 3, Name = "Catacombs", Music = 9 });

        Assert.That(h.World.MapGroups[3].Music, Is.EqualTo(9), "map work is what a Mapper is for");
    }

    [Test]
    public void DeveloperCanSaveContent()
    {
        var h = new Harness(AdminLevel.Developer);

        h.Save(new EditorSaveItemPacket { ItemNum = 3, Name = "Short Sword" });

        Assert.That(h.World.Items[3].Name, Is.EqualTo("Short Sword"));
    }

    // ── Harness ────────────────────────────────────────────────────────────────────

    // Builds an EditorPacketHandler wired with just the deps the editor-save paths actually touch (world, editor
    // sessions, dispatcher, persistence, background). The remaining four are left null! on purpose: if a future
    // edit starts calling one from an editor-save path, the test throws instead of silently passing — the signal
    // we want. (The MapGroup save is a plain broadcast, so it needs no JoinLeaveSystem either.)
    //
    // Only reachable at this cost because the editor dispatch is its own type: against a full PacketHandler
    // this harness would mean passing null! for nineteen game systems the editor never touches — combat,
    // guilds, mail, market, trade, parties, spells, social, the game loop.
    sealed class Harness
    {
        public readonly GameWorld World = new();
        public readonly PlayerManager Pm = new();
        public readonly EditorSessionManager Editors = new();
        public readonly CapturingDispatcher Dispatcher = new();
        public readonly RecordingPersistence Persistence = new();
        private readonly EditorPacketHandler _handler;

        // Access defaults to Creator because these tests are about BROADCASTS, not permissions — the
        // handler now refuses a content save below Developer, and a session left at the enum's default
        // (Player) would fail every test here for a reason that has nothing to do with what they assert.
        public Harness(AdminLevel access = AdminLevel.Creator)
        {
            Editors.GetSession(Editor)!.IsAuthenticated = true;
            Editors.GetSession(Editor)!.AdminLevel = access;
            _handler = new EditorPacketHandler(
                World, Pm, Editors, Dispatcher, Persistence, new NoOpBackground(),
                items: null!, joinLeave: null!, quests: null!, spawn: null!,
                saver: null!, gameLoop: null!,
                NullLogger<EditorPacketHandler>.Instance);
        }

        public void Save<T>(T packet) where T : IPacket
            => _handler.HandleEditorPacket(Editor, PacketSerializer.Serialize(packet));
    }

    // Records what was broadcast so a test can assert the editor save produced the expected packet(s).
    sealed class CapturingDispatcher : IPacketDispatcher
    {
        public readonly List<IPacket> Broadcasts = new();                 // SendToAll
        public readonly List<(int Index, IPacket Packet)> Direct = new(); // SendTo

        public T OneBroadcast<T>() where T : IPacket
        {
            var matches = Broadcasts.OfType<T>().ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"expected exactly one {typeof(T).Name} broadcast to all");
            return matches[0];
        }

        public void SendToAll(IPacket packet) => Broadcasts.Add(packet);
        public void SendTo(int index, IPacket packet) => Direct.Add((index, packet));

        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) => Direct.Add((editorIndex, packet));
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }

    // Fire-and-forget stand-in: the editor-save recording happens when SaveXAsync is CALLED (to build the task
    // argument), so Run doesn't need to do anything with the task.
    sealed class NoOpBackground : IBackgroundPersistence
    {
        public void Run(Task task, string operation) { }
        public Task DrainAsync() => Task.CompletedTask;
    }

    // Records which entities were persisted; every read returns an empty/default result (boot loads aren't
    // exercised here).
    sealed class RecordingPersistence : IPersistenceService
    {
        public readonly List<int> SavedMaps = new();
        public readonly List<int> SavedMapGroups = new();
        public readonly List<int> SavedItems = new();
        public readonly List<int> SavedNpcs = new();
        public readonly List<int> SavedShops = new();
        public readonly List<int> SavedSpells = new();
        public readonly List<int> SavedClasses = new();

        public Task SaveMapAsync(int mapNum, MapRecord map)
        {
            SavedMaps.Add(mapNum);
            return Task.CompletedTask;
        }
        public Task SaveMapGroupAsync(int num, MapGroupRecord group)
        {
            SavedMapGroups.Add(num);
            return Task.CompletedTask;
        }
        public Task SaveItemAsync(int num, ItemRecord item)
        {
            SavedItems.Add(num);
            return Task.CompletedTask;
        }
        public Task SaveNpcAsync(int num, NpcRecord npc)
        {
            SavedNpcs.Add(num);
            return Task.CompletedTask;
        }
        public Task SaveShopAsync(int num, ShopRecord shop)
        {
            SavedShops.Add(num);
            return Task.CompletedTask;
        }
        public Task SaveSpellAsync(int num, SpellRecord spell)
        {
            SavedSpells.Add(num);
            return Task.CompletedTask;
        }
        public Task SaveClassAsync(int num, ClassRecord cls)
        {
            SavedClasses.Add(num);
            return Task.CompletedTask;
        }

        // ── Unused reads/writes: benign defaults ──────────────────────────────────
        public Task<bool> AccountExistsAsync(string login) => Task.FromResult(false);
        public Task<bool> AccountNameTakenAsync(string name) => Task.FromResult(false);
        public Task<bool> PasswordOkAsync(string login, string password) => Task.FromResult(false);
        public Task<AccountRecord?> LoadAccountAsync(string login) => Task.FromResult<AccountRecord?>(null);
        public Task<(IReadOnlyList<AccountSummary> page, int total)> ListAccountsAsync(
            string search, AdminLevel? access, int skip, int take) =>
            Task.FromResult<(IReadOnlyList<AccountSummary>, int)>(([], 0));
        public Task SaveAccountAsync(AccountRecord account) => Task.CompletedTask;
        public Task CreateAccountAsync(string login, string password, AdminLevel access = AdminLevel.Player) => Task.CompletedTask;
        public bool HasNoAccounts() => false;
        public Task ChangePasswordAsync(string login, string newPassword) => Task.CompletedTask;
        public Task DeleteAccountAsync(string login) => Task.CompletedTask;
        public Task<bool> CharExistsAsync(string name) => Task.FromResult(false);
        public Task AddCharNameAsync(string name) => Task.CompletedTask;
        public Task DeleteCharNameAsync(string name) => Task.CompletedTask;
        public Task<MapRecord?> LoadMapAsync(int mapNum) => Task.FromResult<MapRecord?>(null);
        public Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync() => Task.FromResult((Array.Empty<ItemRecord>(), 0));
        public Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync() => Task.FromResult((Array.Empty<NpcRecord>(), 0));
        public Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync() => Task.FromResult((Array.Empty<ShopRecord>(), 0));
        public Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync() => Task.FromResult((Array.Empty<SpellRecord>(), 0));
        public Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync() => Task.FromResult((Array.Empty<ClassRecord>(), 0));
        public Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync() => Task.FromResult((Array.Empty<QuestRecord>(), 0));
        public Task SaveQuestAsync(int num, QuestRecord quest) => Task.CompletedTask;
        public Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync() => Task.FromResult((Array.Empty<ConversationRecord>(), 0));
        public Task SaveConversationAsync(int num, ConversationRecord conversation) => Task.CompletedTask;
        public Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync() => Task.FromResult(new Dictionary<int, GuildRecord>());
        public Task SaveGuildAsync(int num, GuildRecord guild) => Task.CompletedTask;
        public Task RetireGuildAsync(int num, GuildRecord guild) => Task.CompletedTask;
        public Task<int> HighestGuildNumberAsync() => Task.FromResult(0);
        public Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync() => Task.FromResult(new Dictionary<int, MapGroupRecord>());
        public Task SaveSeasonArchiveAsync(int season, SeasonArchive archive) => Task.CompletedTask;
        public Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync() => Task.FromResult(new List<SeasonArchive>());
        public Task DeleteMapGroupAsync(int num) => Task.CompletedTask;
        public Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync() => Task.FromResult(new Dictionary<int, MarketListing>());
        public Task SaveMarketListingAsync(int id, MarketListing listing) => Task.CompletedTask;
        public Task DeleteMarketListingAsync(int id) => Task.CompletedTask;
        public Task<List<MarketSale>> LoadMarketSalesAsync() => Task.FromResult(new List<MarketSale>());
        public Task SaveMarketSalesAsync(List<MarketSale> sales) => Task.CompletedTask;
        public Task<List<TradeJournal>> LoadAllTradeJournalsAsync() => Task.FromResult(new List<TradeJournal>());
        public void SaveTradeJournal(TradeJournal journal) { }
        public Task DeleteTradeJournalAsync(int id) => Task.CompletedTask;
        public Task<bool> IsBannedAsync(string login) => Task.FromResult(false);
        public Task BanAsync(string login, string reason) => Task.CompletedTask;
        public Task<bool> UnbanAsync(string login) => Task.FromResult(false);
        public Task<IReadOnlyList<BanEntry>> LoadBanListAsync() => Task.FromResult<IReadOnlyList<BanEntry>>([]);
        public Task<(IReadOnlyList<AccountPenalty> penalties, int scanned)> LoadActivePenaltiesAsync(long nowUtc) =>
            Task.FromResult<(IReadOnlyList<AccountPenalty>, int)>(([], 0));
        public Task RefreshBanListAsync() => Task.CompletedTask;
        public Task<string> HashMachineKeyAsync(string clientKey) => Task.FromResult(clientKey);
        public Task<HardwareBanEntry?> FindHardwareBanAsync(string hashedKey) => Task.FromResult<HardwareBanEntry?>(null);
        public Task<bool> HardwareBanAsync(string hashedKey, string login, string reason) => Task.FromResult(false);
        public Task<int> HardwareUnbanAsync(string login) => Task.FromResult(0);
        public Task<IReadOnlyList<HardwareBanEntry>> LoadHardwareBanListAsync() =>
            Task.FromResult<IReadOnlyList<HardwareBanEntry>>([]);
        public Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum) => Task.FromResult(Array.Empty<DroppedItemSaveData>());
        public Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items) => Task.CompletedTask;
        public Task<string> LoadMotdAsync() => Task.FromResult("");
        public Task SaveMotdAsync(string motd) => Task.CompletedTask;
        public Task<EnvironmentState?> LoadEnvironmentAsync() => Task.FromResult<EnvironmentState?>(null);
        public Task SaveEnvironmentAsync(EnvironmentState state) => Task.CompletedTask;
        public Task AddLogAsync(string message, string chatType) => Task.CompletedTask;
    }
}
