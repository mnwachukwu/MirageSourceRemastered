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
    /// <summary>Whether an ACCOUNT is banned. A ban is stored against the login, never against a
    /// character — an operator types a character name because that is the handle they have, and the
    /// server resolves it to the account behind it.
    ///
    /// <para>This took an <c>ip</c> argument that was never read, above a comment claiming the check
    /// covered both. It did not, and could not: nothing records an IP when a ban is applied. An IP ban
    /// is a separate feature with its own storage, not something this signature can imply.</para></summary>
    Task<bool> IsBannedAsync(string login);
    Task BanAsync(string login, string reason);
    /// <summary>Removes a login's ban. False when there was nothing to remove, so an operator is told
    /// "not banned" rather than a lie about having lifted something.</summary>
    Task<bool> UnbanAsync(string login);
    /// <summary>The whole ban list, for an operator deciding what to lift. A copy — mutating the result
    /// must not reach the cache.</summary>
    Task<IReadOnlyList<BanEntry>> LoadBanListAsync();
    Task RefreshBanListAsync();

    /// <summary>Sweeps every account file for a kick or mute that has not yet run out, returning the
    /// matches and how many files were read.
    ///
    /// <para>O(accounts), and deliberately so: the penalty timers live on the account records that
    /// ENFORCE them, and a second index would be a copy of the truth that can disagree with it. Only ever
    /// called when an operator asks — never on a tick, never on a login.</para></summary>
    Task<(IReadOnlyList<AccountPenalty> penalties, int scanned)> LoadActivePenaltiesAsync(long nowUtc);

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
