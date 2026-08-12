using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.GameLogic;

/// <summary><c>UseItem</c> and the equip/consume behavior behind each item type, including the
/// six potion variants and the vital read/write/broadcast plumbing they share.</summary>
public sealed partial class ItemSystem : GameSystem
{
    // ── Use item ──────────────────────────────────────────────────────────────

    /// <summary>Use the item in an inventory slot, dispatching on its type. Equipment toggles the matching
    /// gear slot — refused in combat, on a class mismatch, below the stat requirement, or when the piece has
    /// been worn to 0 durability (unequipping is always allowed). Potions apply their vital change, a spell
    /// scroll is studied into an open spell slot, and a key opens the door the player faces (across a map seam
    /// if need be), consuming itself only when that door's take flag is set.</summary>
    public void UseItem(int index, int invSlot)
    {
        if (!_pm[index].IsPlaying || !SlotValidation.IsValidInvSlot(invSlot)) return;
        var sp = _pm[index];
        var p = sp.Char;
        int itemNum = p.Inv[invSlot].Num;
        if (itemNum <= 0 || itemNum > Constants.MaxItems) return;

        var item = _world.Items[itemNum];

        bool isEquipment = item.Type is ItemType.Weapon or ItemType.Armor or ItemType.Helmet or ItemType.Shield;
        if (isEquipment)
        {
            long now = Environment.TickCount64;
            if (sp.IsInCombat(now))
            {
                SendMsg(index, ServerStrings.ItemSystem_GearSwapCombat, GameColor.BrightRed);
                return;
            }
        }

        // Class gate (equipment only; empty = anyone). Mirrors the spell-learning gate below — both ask
        // ClassGate, so "may this class use this?" has one answer everywhere.
        if (ItemRecord.IsEquipment(item.Type) && !ClassGate.Allows(item.AllowedClasses, p.Class))
        {
            SendMsg(index, ServerStrings.ItemSystem_WrongClass, GameColor.BrightRed,
                ("Class", ClassGate.Describe(item.AllowedClasses, _world.Classes)));
            return;
        }

        // An item worn to 0 durability BREAKS rather than being destroyed: it stays in the bag, unequipped
        // and unusable, until a repair shop restores it. Only the equip direction is blocked here — taking
        // off an already-worn piece is always allowed. A 0-Durability item carries no durability budget, so
        // it is never "broken" (mirrors CombatSystem.WarnDurability).
        if (isEquipment && item.Durability > 0 && p.Inv[invSlot].Dur <= 0
            && EquippedSlotForType(p, item.Type) != invSlot)
        {
            SendMsg(index, ServerStrings.ItemSystem_ItemBroken, GameColor.BrightRed, ("Item", item.TrimmedName));
            return;
        }

        // Level gate. Sits here, ahead of the type switch, because it is the one requirement that reads
        // the same on a sword, a potion and a scroll — and because it is what actually paces the tier
        // ladder. The stat requirements below cannot: a class's BASE stat is high enough at level 1 that
        // a specialist already meets a mid-ladder piece on the day it is rolled, so those gate WHO may
        // wear a thing while this gates WHEN. Unequipping is never blocked — only reaching for it is.
        if (ItemRecord.UsesLevelReq(item.Type) && item.LevelReq > p.Level
            && !(isEquipment && EquippedSlotForType(p, item.Type) == invSlot))
        {
            SendMsg(index, ServerStrings.ItemSystem_LevelReq, GameColor.BrightRed, ("Level", item.LevelReq));
            return;
        }

        // Player's class record — drives the class-affinity head-start on the equip/learn gates below
        // (a class needs proportionally less of its affinity stat to meet a requirement).
        var cls = _world.Classes[p.Class];
        switch (item.Type)
        {
            case ItemType.Weapon:
                int weaponStrReq = CombatFormulas.GearStatRequirement(item.Power, cls.Str);
                if (p.WeaponSlot != invSlot && p.Str < weaponStrReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_WeaponStrReq, GameColor.BrightRed, ("Required", weaponStrReq));
                    break;
                }
                p.WeaponSlot = (p.WeaponSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Armor:
                int armorDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.ArmorSlot != invSlot && p.Def < armorDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ArmorDefReq, GameColor.BrightRed, ("Required", armorDefReq));
                    break;
                }
                p.ArmorSlot = (p.ArmorSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Helmet:
                int helmetDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.HelmetSlot != invSlot && p.Def < helmetDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_HelmetDefReq, GameColor.BrightRed, ("Required", helmetDefReq));
                    break;
                }
                p.HelmetSlot = (p.HelmetSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.Shield:
                int shieldDefReq = CombatFormulas.GearStatRequirement(item.Power, cls.Def);
                if (p.ShieldSlot != invSlot && p.Def < shieldDefReq)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ShieldDefReq, GameColor.BrightRed, ("Required", shieldDefReq));
                    break;
                }
                p.ShieldSlot = (p.ShieldSlot == invSlot) ? 0 : invSlot;
                SendEquippedGear(index);
                break;

            case ItemType.PotionAddHp:
                ApplyAddPotion(index, p, item, itemNum, PotionVital.Hp);
                break;
            case ItemType.PotionAddMp:
                ApplyAddPotion(index, p, item, itemNum, PotionVital.Mp);
                break;
            case ItemType.PotionAddSp:
                ApplyAddPotion(index, p, item, itemNum, PotionVital.Sp);
                break;
            case ItemType.PotionSubHp:
                ApplySubPotion(index, p, item, itemNum, PotionVital.Hp);
                break;
            case ItemType.PotionSubMp:
                ApplySubPotion(index, p, item, itemNum, PotionVital.Mp);
                break;
            case ItemType.PotionSubSp:
                ApplySubPotion(index, p, item, itemNum, PotionVital.Sp);
                break;

            case ItemType.Spell:
                if (sp.IsInCombat(Environment.TickCount64))
                {
                    SendMsg(index, ServerStrings.PacketHandler_StudyCombat, GameColor.BrightRed);
                    break;
                }
                int spellNum = item.SpellNum;
                if (spellNum <= 0 || spellNum > Constants.MaxSpells)
                {
                    SendMsg(index, ServerStrings.ItemSystem_ScrollNoSpell, GameColor.White);
                    break;
                }
                var learnSpell = _world.Spells[spellNum];
                if (!ClassGate.Allows(learnSpell.AllowedClasses, p.Class))
                {
                    SendMsg(index, ServerStrings.ItemSystem_SpellWrongClass, GameColor.White,
                        ("Class", ClassGate.Describe(learnSpell.AllowedClasses, _world.Classes)));
                    break;
                }
                // The SPELL's own level gate, distinct from the scroll's: a scroll is a delivery mechanism
                // and could be handed out early, while the spell on it is tied to a tier. INT decides who
                // may learn it, this decides when — and the message names whichever one failed.
                if (learnSpell.LevelReq > p.Level)
                {
                    SendMsg(index, ServerStrings.ItemSystem_SpellLevelReq, GameColor.White, ("Level", learnSpell.LevelReq));
                    break;
                }
                int learnIntReq = CombatFormulas.GetSpellIntRequirement(learnSpell, cls.Int);
                if (learnIntReq > p.Int)
                {
                    SendMsg(index, ServerStrings.ItemSystem_SpellIntReq, GameColor.White, ("Int", learnIntReq));
                    break;
                }
                int spellSlot = SpellSystem.FindOpenSpellSlot(p);
                if (spellSlot == 0)
                {
                    SendMsg(index, ServerStrings.ItemSystem_SpellBookFull, GameColor.BrightRed);
                    break;
                }
                if (SpellSystem.HasSpell(p, spellNum))
                {
                    SendMsg(index, ServerStrings.ItemSystem_SpellAlreadyKnown, GameColor.BrightRed);
                    break;
                }
                p.Spell[spellSlot] = spellNum;
                TakeItem(index, itemNum, 0);
                _dispatcher.SendTo(index, new PlayerSpellsPacket { Spells = p.Spell[1..], PreparedSpell = p.PreparedSpell });
                SendMsg(index, ServerStrings.ItemSystem_StudyingSpell, GameColor.Yellow);
                SendMsg(index, ServerStrings.ItemSystem_LearnedSpell, GameColor.White);
                break;

            case ItemType.Key:
                // Resolve the faced tile in world coords so a locked door on the neighbor map
                // directly across a seam opens too (mirrors combat/LoS). The player's own map sits
                // at grid [1,1], so local (0,0) maps to world (MapTilesX, MapTilesY).
                var grid = WorldCoordHelper.BuildMapGrid(_world.Maps, p.Map);
                var (dx, dy) = WorldCoordHelper.DirDelta(p.Dir);
                int wx = WorldCoordHelper.MapTilesX + p.X + dx;
                int wy = WorldCoordHelper.MapTilesY + p.Y + dy;
                var (mapNum, tx, ty) = WorldCoordHelper.ResolveWorldTile(grid, wx, wy);
                if (mapNum <= 0 || mapNum > Constants.MaxMaps) break;
                var map = _world.Maps[mapNum];
                if (map is null) break; // cardinal link to a non-existent map
                var tile = map.Tile[tx, ty];
                // The faced door is read + opened on the player's own layer (a fringe door on the bridge, a ground
                // one beneath). KeyItemNum names the item that opens it; compare against the item being used.
                var key = LayerLogic.AttrFor(tile, p.Layer);
                if (key.Type != TileType.Key || key.KeyItemNum != itemNum) break;
                var temp = _world.TempTiles[mapNum];
                // An already-open door must not re-trigger or consume the key (matches the KeyOpen trigger guard).
                if (temp.IsDoorOpen(tx, ty, p.Layer)) break;
                temp.OpenDoor(tx, ty, p.Layer, Environment.TickCount64);
                SendToMap(_world, mapNum, new MapKeyPacket { MapNum = mapNum, X = tx, Y = ty, Open = true, Layer = p.Layer });
                ViewportMsg(index, ServerStrings.Common_DoorUnlocked, GameColor.White);
                // Read off `key` — the attribute resolved on the PLAYER'S layer — not off the tile's inline
                // ground attribute. As `tile.Data2` it read the ground tile's flag even when the door being
                // opened was the fringe one, so a fringe door consumed the key only if the unrelated ground
                // attribute happened to say so. Naming the field is what made the mismatch visible.
                if (key.KeyIsConsumed)
                {
                    TakeItem(index, itemNum, 0);
                    SendMsg(index, ServerStrings.ItemSystem_KeyDissolves, GameColor.Yellow);
                }
                break;
        }
    }
}
