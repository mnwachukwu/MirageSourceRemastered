namespace Mirage.Shared;

/// <summary>
/// Bound-check helpers for the 1-based indices used throughout the game. Every helper
/// implements the same `value &gt;= 1 &amp;&amp; value &lt;= &lt;Constant&gt;` shape — extracted so the
/// 60+ inline `slot &lt; 1 || slot &gt; Constants.MaxX` guards across server, client, and
/// editor read uniformly and any cap change goes through <see cref="Constants"/> only.
/// Pure functions; no allocations.
/// </summary>
public static class SlotValidation
{
    // ── Player / NPC instance slots (1-based per-server / per-map) ──────────────
    public static bool IsValidPlayerSlot(int slot) => slot >= 1 && slot <= Constants.MaxPlayers;
    public static bool IsValidNpcSlot(int slot) => slot >= 1 && slot <= Constants.MaxMapNpcs;

    // ── Map number ──────────────────────────────────────────────────────────────
    public static bool IsValidMapNum(int mapNum) => mapNum >= 1 && mapNum <= Constants.MaxMaps;

    // ── Per-player slots ────────────────────────────────────────────────────────
    public static bool IsValidInvSlot(int slot) => slot >= 1 && slot <= Constants.MaxInv;
    public static bool IsValidBankSlot(int slot) => slot >= 1 && slot <= Constants.MaxBankSlots;
    public static bool IsValidSpellSlot(int slot) => slot >= 1 && slot <= Constants.MaxPlayerSpells;
    public static bool IsValidCharSlot(int slot) => slot >= 1 && slot <= Constants.MaxChars;

    // ── World-data record numbers ───────────────────────────────────────────────
    public static bool IsValidItemNum(int num) => num >= 1 && num <= Constants.MaxItems;
    public static bool IsValidNpcNum(int num) => num >= 1 && num <= Constants.MaxNpcs;
    public static bool IsValidShopNum(int num) => num >= 1 && num <= Constants.MaxShops;
    public static bool IsValidSpellNum(int num) => num >= 1 && num <= Constants.MaxSpells;
    public static bool IsValidClassNum(int num) => num >= 1 && num <= Constants.MaxClasses;
    public static bool IsValidQuestNum(int num) => num >= 1 && num <= Constants.MaxQuests;
    public static bool IsValidConversationNum(int num) => num >= 1 && num <= Constants.MaxConversations;
    public static bool IsValidMapGroupNum(int num) => num >= 1 && num <= Constants.MaxMapGroups;

    // ── Map tile coordinates (0-based, inclusive maxes) ────────────────────────
    public static bool IsValidTileCoord(int x, int y) =>
        x >= 0 && x <= Constants.MaxMapX && y >= 0 && y <= Constants.MaxMapY;
}
