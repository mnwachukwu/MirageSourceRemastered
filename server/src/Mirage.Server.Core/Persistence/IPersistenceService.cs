using Mirage.Shared.Records;

namespace Mirage.Server.Core.Persistence;

public interface IPersistenceService
{
    // ── Account ───────────────────────────────────────────────────────────────
    /// <summary>Exact-login existence (case-insensitive via the lowercased filename) — for login, delete, and
    /// change-password, which must match the real account name.</summary>
    Task<bool> AccountExistsAsync(string login);
    /// <summary>Canonical name-collision check for account CREATION only: true if any account's name shares
    /// the same <see cref="Mirage.Shared.NameRules.Key"/> (case- and underscore-insensitive), so
    /// "B_o_b" can't be registered alongside "Bob".</summary>
    Task<bool> AccountNameTakenAsync(string name);
    Task<bool> PasswordOkAsync(string login, string password);
    Task<AccountRecord?> LoadAccountAsync(string login);
    Task SaveAccountAsync(AccountRecord account);
    Task CreateAccountAsync(string login, string password);
    Task ChangePasswordAsync(string login, string newPassword);
    Task DeleteAccountAsync(string login);

    // ── Characters ────────────────────────────────────────────────────────────
    Task<bool> CharExistsAsync(string name);
    Task AddCharNameAsync(string name);
    Task DeleteCharNameAsync(string name);

    // ── Maps ──────────────────────────────────────────────────────────────────
    Task<MapRecord?> LoadMapAsync(int mapNum);
    Task SaveMapAsync(int mapNum, MapRecord map);

    // ── Game data arrays ──────────────────────────────────────────────────────
    Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync();
    Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync();
    Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync();
    Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync();
    Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync();
    Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync();
    Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync();

    Task SaveItemAsync(int num, ItemRecord item);
    Task SaveNpcAsync(int num, NpcRecord npc);
    Task SaveShopAsync(int num, ShopRecord shop);
    Task SaveSpellAsync(int num, SpellRecord spell);
    Task SaveClassAsync(int num, ClassRecord cls);
    Task SaveQuestAsync(int num, QuestRecord quest);
    Task SaveConversationAsync(int num, ConversationRecord conversation);

    // ── Guilds ────────────────────────────────────────────────────────────────
    Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync();
    Task SaveGuildAsync(int num, GuildRecord guild);
    Task DeleteGuildAsync(int num);

    // ── Map groups ──────────────────────────────────────────────────────────────
    Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync();
    Task SaveMapGroupAsync(int num, MapGroupRecord group);

    /// <summary>Persist a finished season's final leaderboard to seasons/season{N}.json (archived
    /// in perpetuity).</summary>
    Task SaveSeasonArchiveAsync(int season, SeasonArchive archive);

    /// <summary>Load every archived season (seasons/season{N}.json), ascending by season number — the
    /// perpetual record surfaced by the historical-season browser. Empty if none exist.</summary>
    Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync();
    Task DeleteMapGroupAsync(int num);

    // Marketplace listings — unbounded, keyed by global listing id (mirrors the guild file pattern).
    Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync();
    Task SaveMarketListingAsync(int id, MarketListing listing);
    Task DeleteMarketListingAsync(int id);

    // Rolling marketplace sales history (single JSON file; the seller Sales tab + on-disk admin audit).
    Task<List<MarketSale>> LoadMarketSalesAsync();
    Task SaveMarketSalesAsync(List<MarketSale> sales);

    // Direct-trade write-ahead journals — one file per in-flight swap, keyed by trade id. Save is SYNCHRONOUS
    // and durable (the swap's commit point must hit disk before either character is saved with it applied);
    // load runs once at boot to replay any swap a crash interrupted; delete runs once both characters are saved.
    Task<List<TradeJournal>> LoadAllTradeJournalsAsync();
    void SaveTradeJournal(TradeJournal journal);
    Task DeleteTradeJournalAsync(int id);

    // ── Banlist ───────────────────────────────────────────────────────────────
    Task<bool> IsBannedAsync(string login, string ip);
    Task BanAsync(string login, string reason);
    Task RefreshBanListAsync();

    // ── Dropped map items ─────────────────────────────────────────────────────
    Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum);
    Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items);

    // ── MOTD ──────────────────────────────────────────────────────────────────
    Task<string> LoadMotdAsync();
    Task SaveMotdAsync(string motd);

    // ── Environment (Time of Day + Weather) ────────────────────────────────────
    Task<EnvironmentState?> LoadEnvironmentAsync();
    Task SaveEnvironmentAsync(EnvironmentState state);

    // ── Log ───────────────────────────────────────────────────────────────────
    Task AddLogAsync(string message, string chatType);
}
