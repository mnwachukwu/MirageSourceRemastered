using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Startup, shutdown, networking, console commands, and the handful of common words
/// reused across every other group.</summary>
public static partial class ServerStrings
{
    // ── Server lifecycle ──────────────────────────────────────────────────────
    public const string Server_Starting = nameof(Server_Starting);
    public const string Server_RunningInLanguage = nameof(Server_RunningInLanguage);
    public const string Server_LoadingGameData = nameof(Server_LoadingGameData);
    public const string Server_LoadingItems = nameof(Server_LoadingItems);
    public const string Server_LoadingNpcs = nameof(Server_LoadingNpcs);
    public const string Server_LoadingShops = nameof(Server_LoadingShops);
    public const string Server_LoadingSpells = nameof(Server_LoadingSpells);
    public const string Server_LoadingClasses = nameof(Server_LoadingClasses);
    public const string Server_LoadingQuests = nameof(Server_LoadingQuests);
    public const string Server_LoadingConversations = nameof(Server_LoadingConversations);
    public const string Server_LoadingMaps = nameof(Server_LoadingMaps);
    public const string Server_LoadingMotd = nameof(Server_LoadingMotd);
    public const string Server_LoadedSummary = nameof(Server_LoadedSummary);
    public const string Server_RuntimeDataSummary = nameof(Server_RuntimeDataSummary);
    public const string Server_PaddedSummary = nameof(Server_PaddedSummary);
    public const string Server_SpawningMapItems = nameof(Server_SpawningMapItems);
    public const string Server_LoadingDroppedItems = nameof(Server_LoadingDroppedItems);
    public const string Server_SpawningNpcs = nameof(Server_SpawningNpcs);
    public const string Server_Ready = nameof(Server_Ready);
    public const string Server_ShuttingDown = nameof(Server_ShuttingDown);
    public const string Server_Stopped = nameof(Server_Stopped);
    public const string Server_SavedPlayersOnShutdown = nameof(Server_SavedPlayersOnShutdown);
    public const string Server_SavedDropsOnShutdown = nameof(Server_SavedDropsOnShutdown);
    public const string Server_GameThreadStarted = nameof(Server_GameThreadStarted);
    public const string Server_GameThreadStopped = nameof(Server_GameThreadStopped);

    // ── Network ───────────────────────────────────────────────────────────────
    public const string Net_ListeningOnPort = nameof(Net_ListeningOnPort);
    public const string Net_NewConnection = nameof(Net_NewConnection);
    public const string Net_PlayerDisconnected = nameof(Net_PlayerDisconnected);
    public const string Net_EditorDisconnected = nameof(Net_EditorDisconnected);
    public const string Net_PlayerRefusedFull = nameof(Net_PlayerRefusedFull);
    public const string Net_EditorRefusedFull = nameof(Net_EditorRefusedFull);

    // ── Console commands ──────────────────────────────────────────────────────
    public const string Console_Prompt = nameof(Console_Prompt);
    public const string Console_Help = nameof(Console_Help);
    public const string Console_Shutdown = nameof(Console_Shutdown);
    public const string Console_UnknownCommand = nameof(Console_UnknownCommand);
    public const string Console_WhoTotal = nameof(Console_WhoTotal);
    public const string Console_KickUsage = nameof(Console_KickUsage);
    public const string Console_BanUsage = nameof(Console_BanUsage);
    public const string Console_MuteUsage = nameof(Console_MuteUsage);
    public const string Console_PlayerNotOnline = nameof(Console_PlayerNotOnline);
    public const string Console_Kicked = nameof(Console_Kicked);
    public const string Console_Banned = nameof(Console_Banned);
    public const string Console_Muted = nameof(Console_Muted);

    // ── Common ────────────────────────────────────────────────────────────────
    public const string Common_InventoryFull = nameof(Common_InventoryFull);
    public const string Common_DoorUnlocked = nameof(Common_DoorUnlocked);
}
