using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>
/// Deserializes and dispatches every C-to-S packet from a dedicated <c>Mirage.Editor</c> session:
/// the request/save pairs for items, NPCs, shops, spells, classes, quests, conversations, maps and
/// map groups, plus the live re-broadcast that pushes each save out to connected clients.
///
/// <para>Split out of <see cref="PacketHandler"/>, which was doing two unrelated jobs behind one
/// 30-dependency constructor. Editing content and playing the game share almost nothing: this half
/// needs eleven collaborators, and none of combat, guilds, mail, market, trade, parties, spells,
/// social, or the game loop. Separating them means an editor test can build a real handler instead
/// of passing <c>null!</c> for two thirds of a constructor it does not use.</para>
///
/// <para>Authentication is re-checked by every handler through <see cref="IsEditorAuthenticated"/>,
/// so an unauthenticated session can send only <see cref="EditorLoginPacket"/>. There is no flood
/// limit here (unlike the game dispatch) because editor sessions are Mapper+ by definition.</para>
/// </summary>
public sealed class EditorPacketHandler
{
    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly EditorSessionManager _editors;
    private readonly IPacketDispatcher _dispatcher;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly ItemSystem _items;
    private readonly JoinLeaveSystem _joinLeave;
    private readonly QuestSystem _quests;
    private readonly SpawnSystem _spawn;
    private readonly ILogger<EditorPacketHandler> _logger;

    public EditorPacketHandler(
        GameWorld world, PlayerManager pm, EditorSessionManager editors, IPacketDispatcher dispatcher,
        IPersistenceService persistence, IBackgroundPersistence bg, ItemSystem items,
        JoinLeaveSystem joinLeave, QuestSystem quests, SpawnSystem spawn,
        ILogger<EditorPacketHandler> logger)
    {
        _world = world;
        _pm = pm;
        _editors = editors;
        _dispatcher = dispatcher;
        _persistence = persistence;
        _bg = bg;
        _items = items;
        _joinLeave = joinLeave;
        _quests = quests;
        _spawn = spawn;
        _logger = logger;
    }

    /// <summary>Dispatch one JSON line from a dedicated editor session. Every handler re-checks
    /// authentication (<see cref="IsEditorAuthenticated"/>), so an unauthenticated session can send only
    /// <see cref="EditorLoginPacket"/>. No flood limit — editor sessions are Mapper+ by definition.</summary>
    public void HandleEditorPacket(int editorIndex, string jsonLine)
    {
        IPacket? packet = PacketSerializer.TryDeserialize(jsonLine);
        if (packet is null) return;

        try
        {
            switch (packet)
            {
                case EditorLoginPacket p:
                    HandleEditorLogin(editorIndex, p);
                    break;
                case EditorRequestItemPacket p:
                    HandleEditorRequestItem(editorIndex, p);
                    break;
                case EditorRequestNpcPacket p:
                    HandleEditorRequestNpc(editorIndex, p);
                    break;
                case EditorRequestShopPacket p:
                    HandleEditorRequestShop(editorIndex, p);
                    break;
                case EditorRequestQuestPacket p:
                    HandleEditorRequestQuest(editorIndex, p);
                    break;
                case EditorRequestConversationPacket p:
                    HandleEditorRequestConversation(editorIndex, p);
                    break;
                case EditorRequestSpellPacket p:
                    HandleEditorRequestSpell(editorIndex, p);
                    break;
                case EditorRequestMapPacket p:
                    HandleEditorRequestMap(editorIndex, p);
                    break;
                case EditorRequestClassPacket p:
                    HandleEditorRequestClass(editorIndex, p);
                    break;
                case EditorRequestAllItemsPacket p:
                    HandleEditorRequestAllItems(editorIndex, p);
                    break;
                case EditorRequestAllNpcsPacket p:
                    HandleEditorRequestAllNpcs(editorIndex, p);
                    break;
                case EditorRequestAllShopsPacket p:
                    HandleEditorRequestAllShops(editorIndex, p);
                    break;
                case EditorRequestAllQuestsPacket p:
                    HandleEditorRequestAllQuests(editorIndex, p);
                    break;
                case EditorRequestAllConversationsPacket p:
                    HandleEditorRequestAllConversations(editorIndex, p);
                    break;
                case EditorRequestAllSpellsPacket p:
                    HandleEditorRequestAllSpells(editorIndex, p);
                    break;
                case EditorRequestAllClassesPacket p:
                    HandleEditorRequestAllClasses(editorIndex, p);
                    break;
                case EditorRequestMapGroupPacket p:
                    HandleEditorRequestMapGroup(editorIndex, p);
                    break;
                case EditorRequestAllMapGroupsPacket p:
                    HandleEditorRequestAllMapGroups(editorIndex, p);
                    break;
                case EditorSaveMapGroupPacket p:
                    HandleEditorSaveMapGroup(editorIndex, p);
                    break;
                case EditorSaveClassPacket p:
                    HandleEditorSaveClass(editorIndex, p);
                    break;
                case EditorSaveItemPacket p:
                    HandleEditorSaveItem(editorIndex, p);
                    break;
                case EditorSaveNpcPacket p:
                    HandleEditorSaveNpc(editorIndex, p);
                    break;
                case EditorSaveShopPacket p:
                    HandleEditorSaveShop(editorIndex, p);
                    break;
                case EditorSaveQuestPacket p:
                    HandleEditorSaveQuest(editorIndex, p);
                    break;
                case EditorSaveConversationPacket p:
                    HandleEditorSaveConversation(editorIndex, p);
                    break;
                case EditorSaveSpellPacket p:
                    HandleEditorSaveSpell(editorIndex, p);
                    break;
                case EditorSaveMapPacket p:
                    HandleEditorSaveMap(editorIndex, p);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EditorPacketHandler error for editor {Index}", editorIndex);
        }
    }

    private void HandleEditorLogin(int editorIndex, EditorLoginPacket p)
    {
        var session = _editors.GetSession(editorIndex);
        if (session is null || session.IsAuthenticated) return;
        RunAsync(HandleEditorLoginAsync(editorIndex, p.Username.Trim(), p.Password, p.Locale), nameof(HandleEditorLoginAsync));
    }

    private async Task HandleEditorLoginAsync(int editorIndex, string username, string password, string locale)
    {
        var session = _editors.GetSession(editorIndex);
        if (session is null) return;

        if (!await _persistence.AccountExistsAsync(username) ||
            !await _persistence.PasswordOkAsync(username, password))
        {
            _dispatcher.SendToEditor(editorIndex, new EditorLoginResponsePacket
            { Success = false, Message = ServerStrings.ForLocale(locale, ServerStrings.EditorAuth_InvalidCredentials) });
            _dispatcher.GracefulDisconnectEditor(editorIndex);
            return;
        }

        var account = await _persistence.LoadAccountAsync(username);
        // Access is per-account — read it straight off the account record.
        AdminLevel access = account?.Access ?? AdminLevel.Player;

        if (access < AdminLevel.Mapper)
        {
            _dispatcher.SendToEditor(editorIndex, new EditorLoginResponsePacket
            { Success = false, Message = ServerStrings.ForLocale(locale, ServerStrings.EditorAuth_InsufficientAccess) });
            _dispatcher.GracefulDisconnectEditor(editorIndex);
            return;
        }

        session.Login = username;
        session.AdminLevel = access;
        session.IsAuthenticated = true;

        _dispatcher.SendToEditor(editorIndex, new EditorLoginResponsePacket
        { Success = true, Message = ServerStrings.ForLocale(locale, ServerStrings.EditorAuth_Authenticated), AccessLevel = access });

        _dispatcher.SendToEditor(editorIndex, BuildEditorDataPacket());
        _logger.LogInformation("Editor session authenticated: {Username}", username);
    }

    private EditorDataPacket BuildEditorDataPacket()
    {
        var items = Enumerable.Range(1, Constants.MaxItems)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Items[i].Name))
            .ToArray();
        var npcs = Enumerable.Range(1, Constants.MaxNpcs)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Npcs[i].Name))
            .ToArray();
        var shops = Enumerable.Range(1, Constants.MaxShops)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Shops[i].Name))
            .ToArray();
        var spells = Enumerable.Range(1, Constants.MaxSpells)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Spells[i].Name))
            .ToArray();
        var maps = Enumerable.Range(1, Constants.MaxMaps)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Maps[i].Name))
            .ToArray();

        var classes = Enumerable.Range(1, Constants.MaxClasses)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Classes[i].Name))
            .ToArray();

        // MapGroups live in a sparse Dictionary; project the editor's 1-based slot range over it (absent
        // slots -> blank name), matching how every other record type presents a fixed slot list.
        var mapGroups = Enumerable.Range(1, Constants.MaxMapGroups)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.MapGroups.GetValueOrDefault(i)?.Name ?? ""))
            .ToArray();

        var quests = Enumerable.Range(1, Constants.MaxQuests)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Quests[i].Name))
            .ToArray();

        var conversations = Enumerable.Range(1, Constants.MaxConversations)
            .Select(i => new EditorDataPacket.NameEntry(i, _world.Conversations[i].Name))
            .ToArray();

        var currencyItems = Enumerable.Range(1, Constants.MaxItems)
            .Where(i => _world.Items[i].Type == ItemType.Currency)
            .ToArray();

        // Gate facts for the class editor's starting-loadout tables, from the LIVE world. Only authored
        // slots are sent — a blank row has nothing to gate and would just pad the payload.
        var itemGates = Enumerable.Range(1, Constants.MaxItems)
            .Where(i => !string.IsNullOrEmpty(_world.Items[i].Name))
            .Select(i => new EditorDataPacket.ItemGate(i, _world.Items[i].Type, _world.Items[i].Power,
                _world.Items[i].LevelReq,
                _world.Items[i].AllowedClasses is null ? null : new List<short>(_world.Items[i].AllowedClasses!)))
            .ToArray();
        var spellGates = Enumerable.Range(1, Constants.MaxSpells)
            .Where(i => !string.IsNullOrEmpty(_world.Spells[i].Name))
            .Select(i => new EditorDataPacket.SpellGate(i, _world.Spells[i].Type, _world.Spells[i].VitalAmount,
                _world.Spells[i].LevelReq,
                _world.Spells[i].AllowedClasses is null ? null : new List<short>(_world.Spells[i].AllowedClasses!)))
            .ToArray();

        var npcSizes = new int[Constants.MaxNpcs + 1];
        for (int i = 1; i <= Constants.MaxNpcs; i++) npcSizes[i] = _world.Npcs[i].EffectiveSize;

        return new EditorDataPacket
        {
            Items = items,
            Npcs = npcs,
            Shops = shops,
            Spells = spells,
            Maps = maps,
            Classes = classes,
            MapGroups = mapGroups,
            Quests = quests,
            Conversations = conversations,
            CurrencyItems = currencyItems,
            ItemGates = itemGates,
            SpellGates = spellGates,
            NpcSizes = npcSizes,
        };
    }

    private void HandleEditorRequestItem(int editorIndex, EditorRequestItemPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.ItemNum;
        if (!SlotValidation.IsValidItemNum(n)) return;
        _dispatcher.SendToEditor(editorIndex, PacketBuilder.UpdateItem(n, _world.Items[n]));
    }

    private void HandleEditorRequestNpc(int editorIndex, EditorRequestNpcPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.NpcNum;
        if (!SlotValidation.IsValidNpcNum(n)) return;
        var npc = _world.Npcs[n];
        _dispatcher.SendToEditor(editorIndex, new UpdateNpcPacket
        {
            NpcNum = n,
            Name = npc.Name,
            AttackSay = npc.AttackSay,
            Sprite = npc.Sprite,
            Size = npc.EffectiveSize,
            SpawnSecs = npc.SpawnSecs,
            Behavior = npc.Behavior,
            Group = npc.Group,
            Range = npc.Range,
            Drops = npc.Drops is null ? null : new List<NpcDrop>(npc.Drops),
            Str = npc.Str,
            Def = npc.Def,
            Spd = npc.Spd,
            Int = npc.Int,
            ExtraHp = npc.ExtraHp,
            IsBoss = npc.IsBoss,
            EmitsLight = npc.EmitsLight,
            Light = npc.Light,
        });
    }

    private void HandleEditorRequestShop(int editorIndex, EditorRequestShopPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.ShopNum;
        if (!SlotValidation.IsValidShopNum(n)) return;
        var shop = _world.Shops[n];
        _dispatcher.SendToEditor(editorIndex, new UpdateShopPacket
        {
            ShopNum = n,
            Name = shop.Name,
            FixesItems = shop.FixesItems,
            ShopType = shop.ShopType,
            AllowBanking = shop.AllowBanking,
            Keeper = shop.Keeper,
            Trades = shop.TradeItem
                .Select(t => new EditorSaveShopPacket.TradeEntry(
                    t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
                .ToArray(),
            Sales = [.. shop.SalesItem],
        });
    }

    private void HandleEditorRequestSpell(int editorIndex, EditorRequestSpellPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.SpellNum;
        if (!SlotValidation.IsValidSpellNum(n)) return;
        var spell = _world.Spells[n];
        _dispatcher.SendToEditor(editorIndex, new UpdateSpellPacket
        {
            SpellNum = n,
            Name = spell.Name,
            AllowedClasses = spell.AllowedClasses is null ? null : new List<short>(spell.AllowedClasses),
            Type = spell.Type,
            VitalAmount = spell.VitalAmount,
            ItemNum = spell.ItemNum,
            ItemQuantity = spell.ItemQuantity,
            IntReq = spell.IntReq,
        });
    }

    private void HandleEditorRequestMap(int editorIndex, EditorRequestMapPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.MapNum;
        if (!SlotValidation.IsValidMapNum(n)) return;
        _dispatcher.SendToEditor(editorIndex, PacketBuilder.SendMap(n, _world.Maps[n], forEditor: true));
    }

    private void HandleEditorRequestClass(int editorIndex, EditorRequestClassPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.ClassNum;
        if (!SlotValidation.IsValidClassNum(n)) return;
        var cls = _world.Classes[n];
        _dispatcher.SendToEditor(editorIndex, new UpdateClassPacket
        {
            ClassNum = n,
            Name = cls.Name,
            Description = cls.Description,
            SpriteMale = cls.SpriteMale,
            SpriteFemale = cls.SpriteFemale,
            Str = cls.Str,
            Def = cls.Def,
            Spd = cls.Spd,
            Int = cls.Int,
            StartingItems = cls.StartingItems is null ? null : new List<ClassStartingItem>(cls.StartingItems),
            StartingSpells = cls.StartingSpells is null ? null : new List<int>(cls.StartingSpells),
        });
    }

    private void HandleEditorRequestAllItems(int editorIndex, EditorRequestAllItemsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllItemsPacket
        {
            Items = Enumerable.Range(1, Constants.MaxItems)
                .Select(n => PacketBuilder.UpdateItem(n, _world.Items[n]))
                .ToArray(),
        });
    }

    private void HandleEditorRequestAllNpcs(int editorIndex, EditorRequestAllNpcsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllNpcsPacket
        {
            Npcs = Enumerable.Range(1, Constants.MaxNpcs).Select(n =>
            {
                var npc = _world.Npcs[n];
                return new UpdateNpcPacket
                {
                    NpcNum = n, Name = npc.Name, AttackSay = npc.AttackSay,
                    Sprite = npc.Sprite, Size = npc.EffectiveSize, SpawnSecs = npc.SpawnSecs,
                    Behavior = npc.Behavior, Group = npc.Group, Range = npc.Range,
                    Drops = npc.Drops is null ? null : new List<NpcDrop>(npc.Drops),
                    Str = npc.Str, Def = npc.Def, Spd = npc.Spd, Int = npc.Int,
                    ExtraHp = npc.ExtraHp,
                    IsBoss = npc.IsBoss,
                    EmitsLight = npc.EmitsLight,
                    Light = npc.Light,
                };
            }).ToArray(),
        });
    }

    private void HandleEditorRequestAllShops(int editorIndex, EditorRequestAllShopsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllShopsPacket
        {
            Shops = Enumerable.Range(1, Constants.MaxShops).Select(n =>
            {
                var shop = _world.Shops[n];
                return new UpdateShopPacket
                {
                    ShopNum = n, Name = shop.Name, FixesItems = shop.FixesItems,
                    ShopType = shop.ShopType, AllowBanking = shop.AllowBanking,
                    Keeper = shop.Keeper,
                    Trades = shop.TradeItem
                        .Select(t => new EditorSaveShopPacket.TradeEntry(
                            t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
                        .ToArray(),
                };
            }).ToArray(),
        });
    }

    private void HandleEditorRequestAllSpells(int editorIndex, EditorRequestAllSpellsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllSpellsPacket
        {
            Spells = Enumerable.Range(1, Constants.MaxSpells).Select(n =>
            {
                var spell = _world.Spells[n];
                return new UpdateSpellPacket
                {
                    SpellNum = n, Name = spell.Name,
                    AllowedClasses = spell.AllowedClasses is null ? null : new List<short>(spell.AllowedClasses),
                    Type = spell.Type,
                    VitalAmount = spell.VitalAmount, ItemNum = spell.ItemNum,
                    ItemQuantity = spell.ItemQuantity, IntReq = spell.IntReq,
                };
            }).ToArray(),
        });
    }

    private void HandleEditorRequestAllClasses(int editorIndex, EditorRequestAllClassesPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllClassesPacket
        {
            Classes = Enumerable.Range(1, Constants.MaxClasses).Select(n =>
            {
                var cls = _world.Classes[n];
                return new UpdateClassPacket
                {
                    ClassNum = n, Name = cls.Name, Description = cls.Description,
                    SpriteMale = cls.SpriteMale, SpriteFemale = cls.SpriteFemale,
                    Str = cls.Str, Def = cls.Def, Spd = cls.Spd, Int = cls.Int,
                    StartingItems = cls.StartingItems is null ? null : new List<ClassStartingItem>(cls.StartingItems),
                    StartingSpells = cls.StartingSpells is null ? null : new List<int>(cls.StartingSpells),
                };
            }).ToArray(),
        });
    }

    private void HandleEditorSaveClass(int editorIndex, EditorSaveClassPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.ClassNum;
        if (!SlotValidation.IsValidClassNum(n)) return;

        var cls = _world.Classes[n];
        cls.Name = p.Name;
        cls.Description = p.Description;
        cls.SpriteMale = p.SpriteMale;
        cls.SpriteFemale = p.SpriteFemale;
        cls.Str = p.Str;
        cls.Def = p.Def;
        cls.Spd = p.Spd;
        cls.Int = p.Int;
        // Normalized per line on save so a bad state never persists: currency keeps its stack, everything
        // else is exactly one. Inert lines and duplicate spells are stripped by Normalize below, so what
        // lands on disk is exactly what character creation will grant.
        cls.StartingItems = p.StartingItems is null ? null : [.. p.StartingItems.Select(s =>
        {
            bool isCurrency = SlotValidation.IsValidItemNum(s.ItemNum)
                              && _world.Items[s.ItemNum].Type == ItemType.Currency;
            return new ClassStartingItem
            {
                ItemNum = s.ItemNum,
                Quantity = isCurrency ? (s.Quantity < 1 ? (short)1 : s.Quantity) : (short)0,
            };
        })];
        cls.StartingSpells = p.StartingSpells is null ? null : [.. p.StartingSpells];
        cls.Normalize();

        _bg.Run(_persistence.SaveClassAsync(n, cls), nameof(IPersistenceService.SaveClassAsync));
        _dispatcher.SendToAll(new UpdateClassPacket
        {
            ClassNum = n,
            Name = cls.Name,
            Description = cls.Description,
            SpriteMale = cls.SpriteMale,
            SpriteFemale = cls.SpriteFemale,
            Str = cls.Str,
            Def = cls.Def,
            Spd = cls.Spd,
            Int = cls.Int,
            StartingItems = cls.StartingItems is null ? null : new List<ClassStartingItem>(cls.StartingItems),
            StartingSpells = cls.StartingSpells is null ? null : new List<int>(cls.StartingSpells),
        });
        _logger.LogInformation("Editor saved class #{Num}.", n);
    }

    private void HandleEditorSaveItem(int editorIndex, EditorSaveItemPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int n = p.ItemNum;
        if (!SlotValidation.IsValidItemNum(n)) return;

        var item = _world.Items[n];
        item.Name = p.Name;
        item.Pic = p.Pic;
        item.Type = p.Type;
        item.Durability = p.Durability;
        item.VitalAmount = p.VitalAmount;
        item.SpellNum = p.SpellNum;
        item.Power = p.Power;
        item.LevelReq = p.LevelReq;
        item.AllowedClasses = p.AllowedClasses;
        item.NonTradeable = p.NonTradeable;
        item.NonListable = p.NonListable;
        item.NonMailable = p.NonMailable;
        item.DestroyOnDrop = p.DestroyOnDrop;
        item.NonJunkable = p.NonJunkable;
        item.Price = p.Price;
        // Clear anything the new Type doesn't use before it is stored or broadcast. The editor already
        // normalizes on its side, but the server is authoritative: it will not persist a stale Power on
        // something that stopped being equipment, whatever a client sends.
        item.Normalize();

        _bg.Run(_persistence.SaveItemAsync(n, item), nameof(IPersistenceService.SaveItemAsync));
        _dispatcher.SendToAll(PacketBuilder.UpdateItem(n, item));
        _logger.LogInformation("Editor saved item #{Num}.", n);
    }

    private void HandleEditorSaveNpc(int editorIndex, EditorSaveNpcPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int n = p.NpcNum;
        if (!SlotValidation.IsValidNpcNum(n)) return;

        var npc = _world.Npcs[n];
        npc.Name = p.Name;
        npc.AttackSay = p.AttackSay;
        npc.Sprite = p.Sprite;
        // Clamp to a valid footprint class on save so a malformed packet never persists a bad size
        // (mirrors the DropItemQuantity normalization below).
        npc.Size = Math.Clamp(p.Size, 1, Constants.MaxNpcSize);
        npc.SpawnSecs = p.SpawnSecs;
        npc.Behavior = p.Behavior;
        npc.Group = p.Group;
        npc.Range = p.Range;
        // Drop table. Value only matters for a CURRENCY line; the runtime ignores it for every other item
        // type. Normalized per line on save so a bad state never persists: currency -> at least 1,
        // anything else -> 0. Lines naming no item are dropped by npc.Normalize() below, along with the
        // legacy single-drop fields, so what lands on disk is exactly what the roller reads.
        npc.Drops = p.Drops is null ? null : [.. p.Drops.Select(d =>
        {
            bool isCurrency = d.ItemNum > 0 && d.ItemNum <= Constants.MaxItems
                              && _world.Items[d.ItemNum].Type == ItemType.Currency;
            return new NpcDrop
            {
                ItemNum = d.ItemNum,
                Chance = d.Chance,
                Quantity = isCurrency ? (d.Quantity < 1 ? (short)1 : d.Quantity) : (short)0,
            };
        })];
        npc.Str = p.Str;
        npc.Def = p.Def;
        npc.Spd = p.Spd;
        npc.Int = p.Int;
        npc.ExtraHp = p.ExtraHp;
        npc.IsBoss = p.IsBoss;
        npc.EmitsLight = p.EmitsLight;
        npc.Light = p.Light;
        // Canonicalize before persisting: drops inert lines, caps the table, and collapses an empty list
        // to null. Mirrors item.Normalize() / spell.Normalize() on their save paths — a saved file should
        // say exactly what the roller will read.
        npc.Normalize();

        _bg.Run(_persistence.SaveNpcAsync(n, npc), nameof(IPersistenceService.SaveNpcAsync));
        _dispatcher.SendToAll(BuildUpdateNpc(n));
        _logger.LogInformation("Editor saved npc #{Num}.", n);
    }

    // Full NPC template snapshot for a live editor broadcast (SendToAll). Carries the keeper-shop KIND
    // (GameWorld.KeeperShopKind: 0 none / 1 store / 2 inn) so a client refreshes the $ glyph + interact
    // routing + menu label without a reconnect. Reused by the shop-keeper-change re-broadcast below.
    private UpdateNpcPacket BuildUpdateNpc(int npcNum)
    {
        var npc = _world.Npcs[npcNum];
        return new UpdateNpcPacket
        {
            NpcNum = npcNum,
            Name = npc.Name,
            AttackSay = npc.AttackSay,
            Sprite = npc.Sprite,
            Size = npc.EffectiveSize,
            SpawnSecs = npc.SpawnSecs,
            Behavior = npc.Behavior,
            Group = npc.Group,
            Range = npc.Range,
            Drops = npc.Drops is null ? null : new List<NpcDrop>(npc.Drops),
            Str = npc.Str,
            Def = npc.Def,
            Spd = npc.Spd,
            Int = npc.Int,
            ExtraHp = npc.ExtraHp,
            IsBoss = npc.IsBoss,
            EmitsLight = npc.EmitsLight,
            Light = npc.Light,
            KeeperShop = _world.KeeperShopKind(npcNum),
        };
    }

    private void HandleEditorSaveShop(int editorIndex, EditorSaveShopPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int n = p.ShopNum;
        if (!SlotValidation.IsValidShopNum(n)) return;

        var shop = _world.Shops[n];
        // Capture the pre-save keeper binding + type: a change to either moves/relabels the $ glyph and the
        // interact target on already-connected clients, so we re-broadcast the affected NPC template(s) below.
        int oldKeeper = shop.Keeper;
        var oldShopType = shop.ShopType;
        shop.Name = p.Name;
        shop.FixesItems = p.FixesItems;
        shop.ShopType = p.ShopType;
        shop.AllowBanking = p.AllowBanking;
        shop.Keeper = p.Keeper;

        // Rebuild the trade list from the packet: drop empty rows (so persisted data stays dense with no
        // gaps), cap at the MaxTrades safety ceiling, and normalize each side's quantity so a bad state never
        // persists (mirrors the NPC-drop rule above): no item -> 0, non-currency -> exactly 1 (never stacks),
        // currency -> at least 1. This keeps the give-side HasItem(>=) check well-formed and stops a zero
        // quantity from minting free items.
        shop.TradeItem = p.Trades
            .Where(t => t.GiveItem > 0 || t.GetItem > 0)
            .Take(Constants.MaxTrades)
            .Select(t => new TradeItemRecord
            {
                GiveItem = t.GiveItem,
                GiveQuantity = NormalizeTradeQuantity(t.GiveItem, t.GiveQuantity),
                GetItem = t.GetItem,
                GetQuantity = NormalizeTradeQuantity(t.GetItem, t.GetQuantity),
            })
            .ToList();

        // Sales list: bare item numbers, canonicalized server-side (dead numbers and duplicates dropped)
        // because the server is authoritative and will not store whatever a client happened to send.
        shop.SalesItem = [.. p.Sales];
        shop.Normalize(Constants.MaxItems);

        _bg.Run(_persistence.SaveShopAsync(n, shop), nameof(IPersistenceService.SaveShopAsync));
        _dispatcher.SendToAll(new UpdateShopPacket
        {
            ShopNum = n,
            Name = shop.Name,
            FixesItems = shop.FixesItems,
            ShopType = shop.ShopType,
            AllowBanking = shop.AllowBanking,
            Keeper = shop.Keeper,
            Trades = shop.TradeItem
                .Select(t => new EditorSaveShopPacket.TradeEntry(
                    t.GiveItem, t.GiveQuantity, t.GetItem, t.GetQuantity))
                .ToArray(),
            Sales = [.. shop.SalesItem]
        });

        // A keeper reassignment (or a Store<->Inn flip on the same keeper) changes which NPC shows the $
        // vendor glyph and what its melee/right-click interact opens (and the menu label). Re-broadcast the
        // affected NPC template(s) so already-connected clients refresh without a reconnect — KeeperShopKind
        // reads the just-saved shop.
        if (oldKeeper != shop.Keeper || oldShopType != shop.ShopType)
        {
            if (SlotValidation.IsValidNpcNum(oldKeeper)) _dispatcher.SendToAll(BuildUpdateNpc(oldKeeper));
            if (shop.Keeper != oldKeeper && SlotValidation.IsValidNpcNum(shop.Keeper))
                _dispatcher.SendToAll(BuildUpdateNpc(shop.Keeper));
        }
        _logger.LogInformation("Editor saved shop #{Num}.", n);
    }

    // ── Quest editor ───────────────────────────────────────────────────────────

    private void HandleEditorRequestQuest(int editorIndex, EditorRequestQuestPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        if (!SlotValidation.IsValidQuestNum(p.QuestNum)) return;
        _dispatcher.SendToEditor(editorIndex, BuildUpdateQuest(p.QuestNum));
    }

    private void HandleEditorRequestAllQuests(int editorIndex, EditorRequestAllQuestsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllQuestsPacket
        {
            Quests = Enumerable.Range(1, Constants.MaxQuests).Select(BuildUpdateQuest).ToArray(),
        });
    }

    private void HandleEditorSaveQuest(int editorIndex, EditorSaveQuestPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.QuestNum;
        if (!SlotValidation.IsValidQuestNum(n)) return;

        var quest = _world.Quests[n];
        quest.Name = p.Name;
        quest.Description = p.Description;
        // Drop degenerate/empty authored rows + cap to the shared limits so a bad state never persists (mirrors
        // the shop trade-quantity normalization). The editor sends fixed-slot lists that include empties.
        quest.Objectives = NormalizeQuestObjectives(p.Objectives);
        quest.ReqLevel = p.ReqLevel;
        quest.ReqStr = p.ReqStr;
        quest.ReqDef = p.ReqDef;
        quest.ReqSpd = p.ReqSpd;
        quest.ReqInt = p.ReqInt;
        // Same authoritative-normalize rule as items and spells: the server drops ids outside the class
        // table, dedupes and sorts, whatever a client sent.
        quest.AllowedClasses = ClassGate.Normalize(p.AllowedClasses);
        quest.PrereqQuest = p.PrereqQuest;
        quest.RewardExp = p.RewardExp;
        quest.RewardItems = NormalizeQuestRewards(p.RewardItems);
        quest.RepeatRewardExp = p.RepeatRewardExp;
        quest.RepeatRewardItems = NormalizeQuestRewards(p.RepeatRewardItems);
        quest.GiverNpc = p.GiverNpc;
        quest.TurnInNpc = p.TurnInNpc;
        quest.Repeatable = p.Repeatable;
        quest.Cadence = p.Cadence;

        _bg.Run(_persistence.SaveQuestAsync(n, quest), nameof(IPersistenceService.SaveQuestAsync));
        // Live-refresh: game clients rebuild this quest's def (the ?/! glyph + dialog/log read it). A def change
        // can also change who's eligible (requirements) or where the glyph sits (giver/turn-in), so re-push every
        // online player's quest log + eligible set too — mirrors the shop-keeper live-refresh.
        _dispatcher.SendToAll(BuildUpdateQuest(n));
        for (int i = 1; i <= Constants.MaxPlayers; i++)
            if (_pm[i].IsPlaying) _quests.RefreshEligibility(i);
        _logger.LogInformation("Editor saved quest #{Num}.", n);
    }

    // Full quest-definition snapshot — the RequestQuest response, an EditorAllQuests element, and the live save
    // broadcast. Lists are deep-cloned so the serialized snapshot can't tear if the game thread re-authors a def.
    private UpdateQuestPacket BuildUpdateQuest(int questNum)
    {
        var q = _world.Quests[questNum];
        return new UpdateQuestPacket
        {
            QuestNum = questNum,
            Name = q.Name,
            Description = q.Description,
            Objectives = q.Objectives.Select(o => o.Clone()).ToList(),
            ReqLevel = q.ReqLevel, ReqStr = q.ReqStr, ReqDef = q.ReqDef, ReqSpd = q.ReqSpd, ReqInt = q.ReqInt,
            AllowedClasses = q.AllowedClasses is null ? null : new List<short>(q.AllowedClasses),
            PrereqQuest = q.PrereqQuest,
            RewardExp = q.RewardExp, RewardItems = q.RewardItems.Select(r => r.Clone()).ToList(),
            RepeatRewardExp = q.RepeatRewardExp, RepeatRewardItems = q.RepeatRewardItems.Select(r => r.Clone()).ToList(),
            GiverNpc = q.GiverNpc, TurnInNpc = q.TurnInNpc, Repeatable = q.Repeatable, Cadence = q.Cadence,
        };
    }

    // Keep only real objectives (a set Kind + positive Count), capped at the shared objective limit.
    private static List<Objective> NormalizeQuestObjectives(List<Objective> src)
    {
        var list = new List<Objective>();
        foreach (var o in src)
        {
            if (o.Kind == ObjectiveKind.None || o.Count <= 0) continue;
            list.Add(new Objective { Kind = o.Kind, Target = o.Target, Count = o.Count });
            if (list.Count >= Constants.MaxQuestObjectives) break;
        }
        return list;
    }

    // Keep only real rewards (a valid item + positive value); gold is item #1 like everywhere else.
    private static List<QuestReward> NormalizeQuestRewards(List<QuestReward> src)
    {
        var list = new List<QuestReward>();
        foreach (var r in src)
        {
            if (r.ItemNum > 0 && r.ItemNum <= Constants.MaxItems && r.Quantity > 0)
                list.Add(new QuestReward { ItemNum = r.ItemNum, Quantity = r.Quantity });
        }

        return list;
    }

    // ── Conversation editor ──────────────────────────────────────────────────

    private void HandleEditorRequestConversation(int editorIndex, EditorRequestConversationPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        if (!SlotValidation.IsValidConversationNum(p.ConvNum)) return;
        _dispatcher.SendToEditor(editorIndex, BuildUpdateConversation(p.ConvNum));
    }

    private void HandleEditorRequestAllConversations(int editorIndex, EditorRequestAllConversationsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllConversationsPacket
        {
            Conversations = Enumerable.Range(1, Constants.MaxConversations).Select(BuildUpdateConversation).ToArray(),
        });
    }

    private void HandleEditorSaveConversation(int editorIndex, EditorSaveConversationPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.ConvNum;
        if (!SlotValidation.IsValidConversationNum(n)) return;

        var conv = _world.Conversations[n];
        conv.Name = p.Name;
        conv.SpeakerNpc = p.SpeakerNpc;
        conv.RootNodeId = p.RootNodeId;
        // Drop empty authored nodes/choices + cap to the shared limits so a bad state never persists.
        conv.Nodes = NormalizeConversationNodes(p.Nodes);

        _bg.Run(_persistence.SaveConversationAsync(n, conv), nameof(IPersistenceService.SaveConversationAsync));
        // Live-refresh: game clients rebuild this conversation's def (the "..." glyph + the panel walk it) — no reconnect.
        _dispatcher.SendToAll(BuildUpdateConversation(n));
        _logger.LogInformation("Editor saved conversation #{Num}.", n);
    }

    // Full conversation-definition snapshot — the RequestConversation response, an EditorAllConversations element,
    // and the live save broadcast. The node/choice lists are deep-cloned so the snapshot can't tear.
    private UpdateConversationPacket BuildUpdateConversation(int convNum)
    {
        var c = _world.Conversations[convNum];
        return new UpdateConversationPacket
        {
            ConvNum = convNum,
            Name = c.Name,
            SpeakerNpc = c.SpeakerNpc,
            RootNodeId = c.RootNodeId,
            Nodes = c.Nodes.Select(n => n.Clone()).ToList(),
        };
    }

    // Keep only real nodes (text OR at least one real choice); each node's choices trimmed to those with a
    // non-empty label, capped at the shared limits. Stable node Ids are preserved (choices reference them).
    private static List<ConversationNode> NormalizeConversationNodes(List<ConversationNode> src)
    {
        var list = new List<ConversationNode>();
        foreach (var n in src)
        {
            var choices = new List<ConversationChoice>();
            foreach (var ch in n.Choices)
            {
                if (ch.Label.TrimEnd().Length == 0) continue;
                choices.Add(new ConversationChoice { Label = ch.Label, NextNodeId = ch.NextNodeId, Action = ch.Action });
                if (choices.Count >= Constants.MaxConversationChoices) break;
            }
            if (n.Text.TrimEnd().Length == 0 && choices.Count == 0) continue;   // an empty node
            list.Add(new ConversationNode { Id = n.Id, Speaker = n.Speaker, Text = n.Text, Choices = choices });
            if (list.Count >= Constants.MaxConversationNodes) break;
        }
        return list;
    }

    // Trade quantity rule shared by both sides of a trade: no item -> 0; a non-currency item -> exactly 1
    // (it never stacks); a currency item -> the caller's value floored at 1.
    private int NormalizeTradeQuantity(int itemNum, int value)
    {
        if (itemNum <= 0 || itemNum > Constants.MaxItems) return 0;
        if (_world.Items[itemNum].Type != ItemType.Currency) return 1;
        return value < 1 ? 1 : value;
    }

    private void HandleEditorSaveSpell(int editorIndex, EditorSaveSpellPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int n = p.SpellNum;
        if (!SlotValidation.IsValidSpellNum(n)) return;

        var spell = _world.Spells[n];
        spell.Name = p.Name;
        spell.AllowedClasses = p.AllowedClasses;
        spell.Type = p.Type;
        spell.VitalAmount = p.VitalAmount;
        spell.ItemNum = p.ItemNum;
        spell.ItemQuantity = p.ItemQuantity;
        spell.IntReq = p.IntReq;
        spell.LevelReq = p.LevelReq;
        // As on the item path: the server clears what the new Type doesn't use before storing or
        // broadcasting. It matters more here — a stale IntReq would silently re-gate the spell.
        spell.Normalize();

        _bg.Run(_persistence.SaveSpellAsync(n, spell), nameof(IPersistenceService.SaveSpellAsync));
        _dispatcher.SendToAll(new UpdateSpellPacket
        {
            SpellNum = n,
            Name = spell.Name,
            AllowedClasses = spell.AllowedClasses is null ? null : new List<short>(spell.AllowedClasses),
            Type = spell.Type,
            VitalAmount = spell.VitalAmount,
            ItemNum = spell.ItemNum,
            ItemQuantity = spell.ItemQuantity,
            IntReq = spell.IntReq,
        });
        _logger.LogInformation("Editor saved spell #{Num}.", n);
    }

    private void HandleEditorSaveMap(int editorIndex, EditorSaveMapPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int mapNum = p.MapNum;
        if (!SlotValidation.IsValidMapNum(mapNum)) return;

        var src = p.Map;
        var map = _world.Maps[mapNum];

        map.Name = src.Name;
        map.DisplayName = src.DisplayName;
        map.Revision++;
        map.Moral = src.Moral;
        map.Up = src.Up;
        map.Down = src.Down;
        map.Left = src.Left;
        map.Right = src.Right;
        map.Music = src.Music;
        map.BootMap = src.BootMap;
        map.BootX = src.BootX;
        map.BootY = src.BootY;
        map.Indoors = src.Indoors;
        map.AlwaysDark = src.AlwaysDark;
        map.GreetingSpeaker = src.GreetingSpeaker;
        map.JoinSay = src.JoinSay;
        map.LeaveSay = src.LeaveSay;
        map.MapGroup = src.MapGroup;

        // Replace, don't merge: the editor's BuildSaveMapPacket omits tiles that are fully default,
        // so a tile cleared back to default in the editor would otherwise keep its old server value.
        // The whole tile grid is replaced by the incoming editor payload.
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
                map.Tile[x, y] = new TileRecord();
        }

        foreach (var tile in src.Tiles)
            map.Tile[tile.X, tile.Y] = tile.ToTile();

        // NPC spawn entries: wholesale replace with the editor's dense list (only non-empty rows are authored).
        // Drop empty/out-of-range rows defensively and cap at MaxMapNpcs runtime posts; SpawnNpc (below) reads
        // these by index, so a pinned entry respawns at its tile immediately after the save.
        map.Npcs = src.Npcs
            .Where(e => e.Npc > 0 && e.Npc <= Constants.MaxNpcs)
            .Take(Constants.MaxMapNpcs)
            .ToList();

        // Size-aware save backstop: drop any pin whose footprint runs off-map, covers a
        // blocked tile, or overlaps an earlier-kept pin (first-wins) — so a bad pin never persists; that NPC then
        // spawns at a random valid tile (SpawnNpc's fallback).
        int NpcFootprintSize(int npcId) => npcId >= 1 && npcId <= Constants.MaxNpcs ? _world.Npcs[npcId].EffectiveSize : 1;
        for (int i = 0; i < map.Npcs.Count; i++)
        {
            var e = map.Npcs[i];
            if (!e.HasPin) continue;
            if (MapNpcPlacement.ValidatePin(map, i, e.PinX!.Value, e.PinY!.Value, e.PinLayer, NpcFootprintSize, overlapBelowIndex: i) != NpcPlacementError.None)
                map.Npcs[i] = e with { PinX = null, PinY = null };
        }

        // Placed lights: wholesale replace (same replace-don't-merge rationale as tiles).
        map.Lights = [.. src.Lights];

        _bg.Run(_persistence.SaveMapAsync(mapNum, map), nameof(IPersistenceService.SaveMapAsync));

        // Re-sync tile-defined map items to the freshly saved layout so newly placed item tiles appear
        // immediately instead of waiting for a /respawn or a restart.  Mirrors HandleMapRespawn — clear
        // every live item (player drops included) then re-spawn from the current tiles.
        _items.ClearMapItems(mapNum);
        _items.SpawnMapItems(mapNum);

        // Respawn NPC slots (1-based).  Skip a slot whose native is away chasing as a guest — resetting it
        // would un-reserve and duplicate that NPC; it respawns from the (now-updated) template on return.
        for (int i = 1; i <= Constants.MaxMapNpcs; i++)
        {
            if (_world.MapNpcs[mapNum, i].IsReservedSlot) continue;
            _world.MapNpcs[mapNum, i] = new MapNpcRecord();
            _spawn.SpawnNpc(i, mapNum);
        }

        // Refresh map for everyone observing it — occupants get a full reload (PlayerWarp blocks input
        // until they confirm), neighbor-cell observers get a targeted CheckForMap + items + NPCs for the
        // cell they observe.  Restricting this to occupants would leave players on adjacent maps using
        // stale neighbor caches — a connection added to the edited map wouldn't be traversable until relog.
        _joinLeave.BroadcastMapRefresh(mapNum);

        _logger.LogInformation("Editor saved map #{MapNum}.", mapNum);
    }

    // ── MapGroup editor ───────────────────────────────────────────────────────
    // Projects a MapGroupRecord onto the wire packet (shared by the single-fetch + bulk responses).
    private static UpdateMapGroupPacket BuildUpdateMapGroup(int n, MapGroupRecord g) => new()
    {
        GroupNum = n,
        Name = g.Name,
        DisplayName = g.DisplayName,
        Music = g.Music,
        Moral = g.Moral,
        Indoors = g.Indoors,
        AlwaysDark = g.AlwaysDark,
        BootMap = g.BootMap,
        BootX = g.BootX,
        BootY = g.BootY,
        GreetingSpeaker = g.GreetingSpeaker,
        JoinSay = g.JoinSay,
        LeaveSay = g.LeaveSay,
        Territory = g.Territory,
        ControllingGuild = g.ControllingGuild,
    };

    private void HandleEditorRequestMapGroup(int editorIndex, EditorRequestMapGroupPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        int n = p.GroupNum;
        if (!SlotValidation.IsValidMapGroupNum(n)) return;
        var group = _world.MapGroups.GetValueOrDefault(n) ?? new MapGroupRecord { Index = n };
        _dispatcher.SendToEditor(editorIndex, BuildUpdateMapGroup(n, group));
    }

    private void HandleEditorRequestAllMapGroups(int editorIndex, EditorRequestAllMapGroupsPacket _)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;
        _dispatcher.SendToEditor(editorIndex, new EditorAllMapGroupsPacket
        {
            MapGroups = Enumerable.Range(1, Constants.MaxMapGroups)
                .Select(n => BuildUpdateMapGroup(n, _world.MapGroups.GetValueOrDefault(n) ?? new MapGroupRecord { Index = n }))
                .ToArray(),
        });
    }

    private void HandleEditorSaveMapGroup(int editorIndex, EditorSaveMapGroupPacket p)
    {
        if (!IsEditorAuthenticated(editorIndex)) return;

        int n = p.GroupNum;
        if (!SlotValidation.IsValidMapGroupNum(n)) return;

        // Reuse the existing record so runtime-only state (ControllingGuild) survives an authoring save;
        // create it on first save (groups are a sparse Dictionary, unlike the pre-sized record arrays).
        if (!_world.MapGroups.TryGetValue(n, out var group))
        {
            group = new MapGroupRecord { Index = n };
            _world.MapGroups[n] = group;
        }
        group.Name = p.Name;
        group.DisplayName = p.DisplayName;
        group.Music = p.Music;
        group.Moral = p.Moral;
        group.Indoors = p.Indoors;
        group.AlwaysDark = p.AlwaysDark;
        group.BootMap = p.BootMap;
        group.BootX = p.BootX;
        group.BootY = p.BootY;
        group.GreetingSpeaker = p.GreetingSpeaker;
        group.JoinSay = p.JoinSay;
        group.LeaveSay = p.LeaveSay;
        group.Territory = p.Territory;

        _bg.Run(_persistence.SaveMapGroupAsync(n, group), nameof(IPersistenceService.SaveMapGroupAsync));

        // Push the edit to online players. A MapGroup is an INDEPENDENT client-cached def (like items/npcs/shops):
        // the client holds the group and resolves each member map's effective values (Moral/Music/Indoors/
        // AlwaysDark/display name) against it on demand (ClientState.*Of + MapGroupResolve), so a group edit needs
        // NO map re-send and NO map-revision bump — broadcasting the new group state re-caches it on every client
        // and the next frame recomputes. Server-side gameplay reads (GameWorld.*Of) already resolve against this
        // same live record, so they pick it up at once too. Reuses UpdateMapGroupPacket (the editor-fetch shape);
        // game clients handle it in HandleUpdateMapGroup, editors via their request/response flow.
        _dispatcher.SendToAll(BuildUpdateMapGroup(n, group));

        _logger.LogInformation("Editor saved map group #{Num}.", n);
    }

    // Starts an async handler without blocking the receive loop, logging any unhandled exception so
    // failures are never silently swallowed. (PacketHandler keeps its own copy for the game half —
    // six lines of plumbing is not worth a shared base class between two otherwise unrelated types.)
    private void RunAsync(Task task, string context)
    {
        task.ContinueWith(
            t => _logger.LogError(t.Exception!.InnerException ?? t.Exception,
                "Unhandled error in {Context}", context),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private bool IsEditorAuthenticated(int editorIndex)
    {
        var session = _editors.GetSession(editorIndex);
        return session is not null && session.IsAuthenticated;
    }
}
