using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Logging;
using Mirage.Shared;
using Mirage.Shared.Records;
using Mirage.Shared.Security;
using System.Text.Json;

namespace Mirage.Server.Core.Persistence;

public sealed class JsonPersistenceService : IPersistenceService
{
    private readonly string _dataPath;
    private readonly ILogger<JsonPersistenceService> _logger;
    private readonly IChatLog _chatLog;
    private readonly SemaphoreSlim _accountLock = new(1, 1);
    private readonly SemaphoreSlim _banLock = new(1, 1);
    private List<BanEntry>? _bansCache;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,   // editor writes map JSON in PascalCase; server writes camelCase
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public JsonPersistenceService(string dataPath, ILogger<JsonPersistenceService> logger, IChatLog chatLog)
    {
        _dataPath = dataPath;
        _logger = logger;
        _chatLog = chatLog;
        Directory.CreateDirectory(AccountsPath);
        Directory.CreateDirectory(MapsPath);
        Directory.CreateDirectory(MapItemsPath);
        Directory.CreateDirectory(ItemsPath);
        Directory.CreateDirectory(QuestsPath);
        Directory.CreateDirectory(ConversationsPath);
        Directory.CreateDirectory(NpcsPath);
        Directory.CreateDirectory(ShopsPath);
        Directory.CreateDirectory(SpellsPath);
        Directory.CreateDirectory(ClassesPath);
        Directory.CreateDirectory(GuildsPath);
        Directory.CreateDirectory(MapGroupsPath);
        Directory.CreateDirectory(SeasonsPath);
        Directory.CreateDirectory(MarketListingsPath);
        Directory.CreateDirectory(TradeJournalsPath);
    }

    private string AccountsPath => Path.Combine(_dataPath, "accounts");
    private string MapsPath => Path.Combine(_dataPath, "maps");
    private string MapItemsPath => Path.Combine(_dataPath, "map_items");
    private string ItemsPath => Path.Combine(_dataPath, "items");
    private string QuestsPath => Path.Combine(_dataPath, "quests");
    private string ConversationsPath => Path.Combine(_dataPath, "conversations");
    private string NpcsPath => Path.Combine(_dataPath, "npcs");
    private string ShopsPath => Path.Combine(_dataPath, "shops");
    private string SpellsPath => Path.Combine(_dataPath, "spells");
    private string ClassesPath => Path.Combine(_dataPath, "classes");
    private string GuildsPath => Path.Combine(_dataPath, "guilds");
    private string MapGroupsPath => Path.Combine(_dataPath, "mapgroups");
    private string SeasonsPath => Path.Combine(_dataPath, "seasons");
    private string MarketListingsPath => Path.Combine(_dataPath, "market");

    private string AccountFile(string login) =>
        Path.Combine(AccountsPath, $"{login.ToLowerInvariant()}.json");

    private string MapFile(int mapNum) =>
        Path.Combine(MapsPath, $"map{mapNum}.json");
    private string DroppedItemFile(int mapNum) =>
        Path.Combine(MapItemsPath, $"map{mapNum}.json");

    private string ItemFile(int num) => Path.Combine(ItemsPath, $"item{num}.json");
    private string QuestFile(int num) => Path.Combine(QuestsPath, $"quest{num}.json");
    private string ConversationFile(int num) => Path.Combine(ConversationsPath, $"conversation{num}.json");
    private string NpcFile(int num) => Path.Combine(NpcsPath, $"npc{num}.json");
    private string ShopFile(int num) => Path.Combine(ShopsPath, $"shop{num}.json");
    private string SpellFile(int num) => Path.Combine(SpellsPath, $"spell{num}.json");
    private string ClassFile(int num) => Path.Combine(ClassesPath, $"class{num}.json");
    private string GuildFile(int num) => Path.Combine(GuildsPath, $"guild{num}.json");
    private string MapGroupFile(int num) => Path.Combine(MapGroupsPath, $"mapgroup{num}.json");
    private string SeasonFile(int num) => Path.Combine(SeasonsPath, $"season{num}.json");
    private string MarketListingFile(int id) => Path.Combine(MarketListingsPath, $"listing{id}.json");
    private string MarketSalesFile => Path.Combine(MarketListingsPath, "sales.json");
    private string TradeJournalsPath => Path.Combine(_dataPath, "trades");
    private string TradeJournalFile(int id) => Path.Combine(TradeJournalsPath, $"journal{id}.json");

    // ── Account ───────────────────────────────────────────────────────────────

    public Task<bool> AccountExistsAsync(string login) =>
        Task.FromResult(File.Exists(AccountFile(login)));

    // Canonical creation-uniqueness: scan the account files (each named for its lowercased login) and
    // compare identity keys, so underscores/case can't spoof an existing account. Creation is rare, so an
    // O(files) scan is fine; login/delete/change-password keep the exact AccountExistsAsync above.
    public Task<bool> AccountNameTakenAsync(string name)
    {
        string key = NameRules.Key(name);
        if (key.Length == 0) return Task.FromResult(false);   // no identity to collide on (rejected upstream)
        foreach (string file in Directory.EnumerateFiles(AccountsPath, "*.json"))
        {
            if (NameRules.Key(Path.GetFileNameWithoutExtension(file)) == key)
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<bool> PasswordOkAsync(string login, string password)
    {
        var account = await LoadAccountAsync(login);
        if (account is null) return false;

        return PasswordHasher.Verify(password, account.Password);
    }

    public async Task<AccountRecord?> LoadAccountAsync(string login)
    {
        string path = AccountFile(login);
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AccountRecord>(json, Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load account {Login}", login);
            return null;
        }
    }

    public async Task SaveAccountAsync(AccountRecord account)
    {
        string path = AccountFile(account.Login);
        await _accountLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(account, Options);
            await WriteAllTextAtomicAsync(path, json);
        }
        finally { _accountLock.Release(); }
    }

    /// <summary>Write a file atomically: stream to a sibling temp file (flushed through to disk), then
    /// rename it over the target. A same-directory rename is atomic on every OS, so a crash or power loss
    /// mid-write leaves the target either wholly the old contents or wholly the new — never truncated or
    /// half-written. The account file gets this because its corruption would lose a player's characters,
    /// bank, and any in-flight trade escrow; a torn write on a plain overwrite could orphan an entire
    /// account. It also makes each trade participant's post-swap record write all-or-nothing per file.</summary>
    private static async Task WriteAllTextAtomicAsync(string path, string contents)
    {
        string tmp = path + ".tmp";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                         bufferSize: 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await fs.WriteAsync(bytes);
            await fs.FlushAsync();
        }
        File.Move(tmp, path, overwrite: true);   // atomic same-volume rename over the live file
    }

    /// <summary>Synchronous twin of <see cref="WriteAllTextAtomicAsync"/> — same temp-file + flush-to-disk +
    /// atomic rename, but blocking. Used for the trade-journal commit, which must be durable before the caller
    /// (the game thread) proceeds to apply the swap. Kept tiny (one small file, only on a trade completion).</summary>
    private static void WriteAllTextAtomicSync(string path, string contents)
    {
        string tmp = path + ".tmp";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public async Task CreateAccountAsync(string login, string password)
    {
        var account = new AccountRecord { Login = login, Password = PasswordHasher.Hash(password) };
        await SaveAccountAsync(account);
    }

    public async Task ChangePasswordAsync(string login, string newPassword)
    {
        var account = await LoadAccountAsync(login);
        if (account is null) return;
        account.Password = PasswordHasher.Hash(newPassword);
        await SaveAccountAsync(account);
    }

    public async Task DeleteAccountAsync(string login)
    {
        // Free up every character name registered to this account before removing the file.
        var account = await LoadAccountAsync(login);
        if (account is not null)
        {
            for (int i = 1; i <= Constants.MaxChars; i++)
            {
                string charName = account.Chars[i].Name.Trim();
                if (!string.IsNullOrEmpty(charName))
                    await DeleteCharNameAsync(charName);
            }
        }

        string path = AccountFile(login);
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Characters ────────────────────────────────────────────────────────────

    private string CharNamesFile => Path.Combine(_dataPath, "charnames.json");

    public async Task<bool> CharExistsAsync(string name)
    {
        // Canonical (case- and underscore-insensitive) collision check: compare identity keys, so "B_o_b"
        // can't be created alongside "Bob". The stored set keeps its lowercased form; keying both sides here
        // also matches any pre-existing underscore entries without a data migration.
        var names = await LoadCharNamesAsync();
        string key = NameRules.Key(name);
        foreach (var n in names)
            if (NameRules.Key(n) == key) return true;
        return false;
    }

    public async Task AddCharNameAsync(string name)
    {
        // Same lock as SaveAccountAsync — concurrent char creates against the same file would
        // otherwise both load, both add, both write, and one entry would silently disappear.
        await _accountLock.WaitAsync();
        try
        {
            var names = await LoadCharNamesAsync();
            names.Add(name.ToLowerInvariant());
            await File.WriteAllTextAsync(CharNamesFile, JsonSerializer.Serialize(names, Options));
        }
        finally { _accountLock.Release(); }
    }

    public async Task DeleteCharNameAsync(string name)
    {
        await _accountLock.WaitAsync();
        try
        {
            var names = await LoadCharNamesAsync();
            names.Remove(name.ToLowerInvariant());
            await File.WriteAllTextAsync(CharNamesFile, JsonSerializer.Serialize(names, Options));
        }
        finally { _accountLock.Release(); }
    }

    private async Task<HashSet<string>> LoadCharNamesAsync()
    {
        if (!File.Exists(CharNamesFile)) return new HashSet<string>();
        string json = await File.ReadAllTextAsync(CharNamesFile);
        var list = JsonSerializer.Deserialize<List<string>>(json, Options);
        return new HashSet<string>(list ?? []);
    }

    // ── Maps ──────────────────────────────────────────────────────────────────

    public async Task<MapRecord?> LoadMapAsync(int mapNum)
    {
        string path = MapFile(mapNum);
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MapRecord>(json, Options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load map {MapNum}", mapNum);
            return null;
        }
    }

    public async Task SaveMapAsync(int mapNum, MapRecord map)
    {
        string json = JsonSerializer.Serialize(map, Options);
        await File.WriteAllTextAsync(MapFile(mapNum), json);
    }

    // ── Dropped map items ─────────────────────────────────────────────────────

    public async Task<DroppedItemSaveData[]> LoadDroppedItemsAsync(int mapNum)
    {
        string path = DroppedItemFile(mapNum);
        if (!File.Exists(path)) return [];
        string json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<DroppedItemSaveData[]>(json, Options) ?? [];
    }

    public async Task SaveDroppedItemsAsync(int mapNum, DroppedItemSaveData[] items)
    {
        await File.WriteAllTextAsync(DroppedItemFile(mapNum), JsonSerializer.Serialize(items, Options));
    }

    // ── Game data arrays ──────────────────────────────────────────────────────

    public async Task<(ItemRecord[] records, int padded)> LoadAllItemsAsync()
    {
        var result = new ItemRecord[Constants.MaxItems + 1];
        for (int i = 0; i <= Constants.MaxItems; i++) result[i] = new ItemRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxItems, ItemFile);
        return (result, padded);
    }

    public async Task<(NpcRecord[] records, int padded)> LoadAllNpcsAsync()
    {
        var result = new NpcRecord[Constants.MaxNpcs + 1];
        for (int i = 0; i <= Constants.MaxNpcs; i++) result[i] = new NpcRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxNpcs, NpcFile);
        // Size 0 ("not defined" in a legacy or blank record) normalizes to the 1x1 default so the whole
        // server and the editor see a valid footprint class. Sentinel handling, not a data migration.
        for (int i = 1; i <= Constants.MaxNpcs; i++)
        {
            if (result[i].Size < 1) result[i].Size = 1;
            // THIS one IS a data migration: it folds a pre-table record's single DropChance/DropItem/
            // DropItemValue into Drops, so every reader downstream sees one shape. Runs on load rather
            // than at editor-save like items and spells, because an NPC authored before the table has to
            // work on a server nobody has opened the editor against.
            result[i].Normalize();
        }
        return (result, padded);
    }

    public async Task<(ShopRecord[] records, int padded)> LoadAllShopsAsync()
    {
        var result = new ShopRecord[Constants.MaxShops + 1];
        for (int i = 0; i <= Constants.MaxShops; i++) result[i] = new ShopRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxShops, ShopFile);
        // Compact each shop's trades: drop the legacy null-at-index-0 and any empty slots so the in-memory
        // list is dense (matching how the editor authors + saves them). Legacy shop JSON stored a fixed
        // 1-based array ([null, slot1..slot8]); this normalizes it on load — no file rewrite required.
        foreach (var shop in result)
        {
            shop.TradeItem = shop.TradeItem
                .Where(t => t is not null && (t.GiveItem > 0 || t.GetItem > 0))
                .ToList();
        }

        return (result, padded);
    }

    public async Task<(SpellRecord[] records, int padded)> LoadAllSpellsAsync()
    {
        var result = new SpellRecord[Constants.MaxSpells + 1];
        for (int i = 0; i <= Constants.MaxSpells; i++) result[i] = new SpellRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxSpells, SpellFile);
        return (result, padded);
    }

    public async Task<(ClassRecord[] records, int padded)> LoadAllClassesAsync()
    {
        var result = new ClassRecord[Constants.MaxClasses + 1];
        for (int i = 0; i <= Constants.MaxClasses; i++) result[i] = new ClassRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxClasses, ClassFile);
        return (result, padded);
    }

    public async Task<(QuestRecord[] records, int padded)> LoadAllQuestsAsync()
    {
        var result = new QuestRecord[Constants.MaxQuests + 1];
        for (int i = 0; i <= Constants.MaxQuests; i++) result[i] = new QuestRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxQuests, QuestFile);
        return (result, padded);
    }

    public async Task<(ConversationRecord[] records, int padded)> LoadAllConversationsAsync()
    {
        var result = new ConversationRecord[Constants.MaxConversations + 1];
        for (int i = 0; i <= Constants.MaxConversations; i++) result[i] = new ConversationRecord();
        int padded = await CheckAndLoadRecordsAsync(result, Constants.MaxConversations, ConversationFile);
        return (result, padded);
    }

    // Mirrors the map loop in MirageServerService: loads each slot file if it exists,
    // creates a blank file if it doesn't. Returns the count of blank files created.
    private async Task<int> CheckAndLoadRecordsAsync<T>(T[] result, int max, Func<int, string> filePath)
    {
        int created = 0;
        for (int i = 1; i <= max; i++)
        {
            string path = filePath(i);
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    result[i] = JsonSerializer.Deserialize<T>(json, Options) ?? result[i];
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
            }
            else
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result[i], Options));
                created++;
            }
        }
        return created;
    }

    public async Task SaveItemAsync(int num, ItemRecord item)
    {
        if (!SlotValidation.IsValidItemNum(num)) return;
        await File.WriteAllTextAsync(ItemFile(num), JsonSerializer.Serialize(item, Options));
    }

    public async Task SaveNpcAsync(int num, NpcRecord npc)
    {
        if (!SlotValidation.IsValidNpcNum(num)) return;
        await File.WriteAllTextAsync(NpcFile(num), JsonSerializer.Serialize(npc, Options));
    }

    public async Task SaveShopAsync(int num, ShopRecord shop)
    {
        if (!SlotValidation.IsValidShopNum(num)) return;
        await File.WriteAllTextAsync(ShopFile(num), JsonSerializer.Serialize(shop, Options));
    }

    public async Task SaveSpellAsync(int num, SpellRecord spell)
    {
        if (!SlotValidation.IsValidSpellNum(num)) return;
        await File.WriteAllTextAsync(SpellFile(num), JsonSerializer.Serialize(spell, Options));
    }

    public async Task SaveClassAsync(int num, ClassRecord cls)
    {
        if (!SlotValidation.IsValidClassNum(num)) return;
        await File.WriteAllTextAsync(ClassFile(num), JsonSerializer.Serialize(cls, Options));
    }

    public async Task SaveQuestAsync(int num, QuestRecord quest)
    {
        if (!SlotValidation.IsValidQuestNum(num)) return;
        await File.WriteAllTextAsync(QuestFile(num), JsonSerializer.Serialize(quest, Options));
    }

    public async Task SaveConversationAsync(int num, ConversationRecord conversation)
    {
        if (!SlotValidation.IsValidConversationNum(num)) return;
        await File.WriteAllTextAsync(ConversationFile(num), JsonSerializer.Serialize(conversation, Options));
    }

    // ── Guilds ────────────────────────────────────────────────────────────────
    // Runtime-created and UNBOUNDED: load every guild{N}.json present (keyed by its N), rather than
    // scanning a fixed 1..Max range or blank-padding. Unused numbers simply have no entry.
    public async Task<Dictionary<int, GuildRecord>> LoadAllGuildsAsync()
    {
        var result = new Dictionary<int, GuildRecord>();
        if (!Directory.Exists(GuildsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(GuildsPath, "guild*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "guild{N}"
            if (stem.Length <= 5 || !int.TryParse(stem.AsSpan(5), out int index) || index < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var guild = JsonSerializer.Deserialize<GuildRecord>(json, Options);
                if (guild is not null) result[index] = guild;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveGuildAsync(int num, GuildRecord guild)
    {
        if (num < 1) return;
        await File.WriteAllTextAsync(GuildFile(num), JsonSerializer.Serialize(guild, Options));
    }

    public Task DeleteGuildAsync(int num)
    {
        if (num < 1) return Task.CompletedTask;
        string path = GuildFile(num);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Marketplace listings mirror guilds: UNBOUNDED, load every listing{N}.json present (keyed by its id).
    public async Task<Dictionary<int, MarketListing>> LoadAllMarketListingsAsync()
    {
        var result = new Dictionary<int, MarketListing>();
        if (!Directory.Exists(MarketListingsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(MarketListingsPath, "listing*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "listing{N}"
            if (stem.Length <= 7 || !int.TryParse(stem.AsSpan(7), out int id) || id < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var listing = JsonSerializer.Deserialize<MarketListing>(json, Options);
                if (listing is not null) result[id] = listing;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveMarketListingAsync(int id, MarketListing listing)
    {
        if (id < 1) return;
        await File.WriteAllTextAsync(MarketListingFile(id), JsonSerializer.Serialize(listing, Options));
    }

    public Task DeleteMarketListingAsync(int id)
    {
        if (id < 1) return Task.CompletedTask;
        string path = MarketListingFile(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<List<MarketSale>> LoadMarketSalesAsync()
    {
        if (!File.Exists(MarketSalesFile)) return new();
        try
        {
            string json = await File.ReadAllTextAsync(MarketSalesFile);
            return JsonSerializer.Deserialize<List<MarketSale>>(json, Options) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load {File}", MarketSalesFile);
            return new();
        }
    }

    public Task SaveMarketSalesAsync(List<MarketSale> sales)
        => File.WriteAllTextAsync(MarketSalesFile, JsonSerializer.Serialize(sales, Options));

    // Trade journals mirror market listings: UNBOUNDED, load every journal{N}.json present at boot.
    public async Task<List<TradeJournal>> LoadAllTradeJournalsAsync()
    {
        var result = new List<TradeJournal>();
        if (!Directory.Exists(TradeJournalsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(TradeJournalsPath, "journal*.json"))
        {
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var journal = JsonSerializer.Deserialize<TradeJournal>(json, Options);
                if (journal is not null) result.Add(journal);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    // SYNCHRONOUS + durable: the swap's commit point must be on disk (fsync'd) BEFORE either character is saved
    // with it applied, so this blocks the caller through a temp-file → flush-to-disk → atomic rename.
    public void SaveTradeJournal(TradeJournal journal)
    {
        if (journal.Id < 1) return;
        WriteAllTextAtomicSync(TradeJournalFile(journal.Id), JsonSerializer.Serialize(journal, Options));
    }

    public Task DeleteTradeJournalAsync(int id)
    {
        if (id < 1) return Task.CompletedTask;
        string path = TradeJournalFile(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Map groups mirror guilds exactly: UNBOUNDED, load every mapgroup{N}.json present (keyed by its N).
    public async Task<Dictionary<int, MapGroupRecord>> LoadAllMapGroupsAsync()
    {
        var result = new Dictionary<int, MapGroupRecord>();
        if (!Directory.Exists(MapGroupsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(MapGroupsPath, "mapgroup*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);   // "mapgroup{N}"
            if (stem.Length <= 8 || !int.TryParse(stem.AsSpan(8), out int index) || index < 1) continue;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                var group = JsonSerializer.Deserialize<MapGroupRecord>(json, Options);
                if (group is not null) result[index] = group;
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        return result;
    }

    public async Task SaveMapGroupAsync(int num, MapGroupRecord group)
    {
        if (num < 1) return;
        await File.WriteAllTextAsync(MapGroupFile(num), JsonSerializer.Serialize(group, Options));
    }

    public async Task SaveSeasonArchiveAsync(int season, SeasonArchive archive)
    {
        if (season < 1) return;
        await File.WriteAllTextAsync(SeasonFile(season), JsonSerializer.Serialize(archive, Options));
    }

    public async Task<List<SeasonArchive>> LoadAllSeasonArchivesAsync()
    {
        var result = new List<SeasonArchive>();
        if (!Directory.Exists(SeasonsPath)) return result;
        foreach (string path in Directory.EnumerateFiles(SeasonsPath, "season*.json"))
        {
            try
            {
                string json = await File.ReadAllTextAsync(path);
                if (JsonSerializer.Deserialize<SeasonArchive>(json, Options) is { } a) result.Add(a);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to load {File}", path); }
        }
        result.Sort((a, b) => a.Season.CompareTo(b.Season));
        return result;
    }

    public Task DeleteMapGroupAsync(int num)
    {
        if (num < 1) return Task.CompletedTask;
        string path = MapGroupFile(num);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // ── Banlist ───────────────────────────────────────────────────────────────

    private string BanFile => Path.Combine(_dataPath, "banlist.json");

    private async Task<List<BanEntry>> LoadBansAsync()
    {
        if (_bansCache is not null) return _bansCache;
        await _banLock.WaitAsync();
        try
        {
            if (_bansCache is not null) return _bansCache;
            if (!File.Exists(BanFile))
            {
                _bansCache = [];
                return _bansCache;
            }
            string json = await File.ReadAllTextAsync(BanFile);
            _bansCache = JsonSerializer.Deserialize<List<BanEntry>>(json, Options) ?? [];
            return _bansCache;
        }
        finally { _banLock.Release(); }
    }

    public async Task<bool> IsBannedAsync(string login, string ip)
    {
        var bans = await LoadBansAsync();
        return bans.Any(b => string.Equals(b.Login, login, StringComparison.OrdinalIgnoreCase));
    }

    public async Task BanAsync(string login, string reason)
    {
        await _banLock.WaitAsync();
        try
        {
            var bans = _bansCache;
            if (bans is null)
            {
                bans = File.Exists(BanFile)
                    ? JsonSerializer.Deserialize<List<BanEntry>>(await File.ReadAllTextAsync(BanFile), Options) ?? []
                    : [];
            }
            if (!bans.Any(b => string.Equals(b.Login, login, StringComparison.OrdinalIgnoreCase)))
                bans.Add(new BanEntry(login, reason));
            await File.WriteAllTextAsync(BanFile, JsonSerializer.Serialize(bans, Options));
            _bansCache = bans;
        }
        finally { _banLock.Release(); }
    }

    public async Task RefreshBanListAsync()
    {
        await _banLock.WaitAsync();
        try
        {
            _bansCache = File.Exists(BanFile)
                ? JsonSerializer.Deserialize<List<BanEntry>>(await File.ReadAllTextAsync(BanFile), Options) ?? []
                : [];
        }
        finally { _banLock.Release(); }
    }

    // ── MOTD ──────────────────────────────────────────────────────────────────

    private string MotdFile => Path.Combine(_dataPath, "motd.json");

    public async Task<string> LoadMotdAsync()
    {
        if (!File.Exists(MotdFile)) return "";
        string json = await File.ReadAllTextAsync(MotdFile);
        return JsonSerializer.Deserialize<string>(json, Options) ?? "";
    }

    public async Task SaveMotdAsync(string motd)
    {
        await File.WriteAllTextAsync(MotdFile, JsonSerializer.Serialize(motd, Options));
    }

    // ── Environment (Time of Day + Weather) ────────────────────────────────────

    private string EnvironmentFile => Path.Combine(_dataPath, "environment.json");

    public async Task<EnvironmentState?> LoadEnvironmentAsync()
    {
        if (!File.Exists(EnvironmentFile)) return null;
        string json = await File.ReadAllTextAsync(EnvironmentFile);
        return JsonSerializer.Deserialize<EnvironmentState>(json, Options);
    }

    public async Task SaveEnvironmentAsync(EnvironmentState state)
    {
        await File.WriteAllTextAsync(EnvironmentFile, JsonSerializer.Serialize(state, Options));
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    public Task AddLogAsync(string message, string chatType)
    {
        _chatLog.Write(message, chatType);
        return Task.CompletedTask;
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private sealed record BanEntry(string Login, string Reason);
}
