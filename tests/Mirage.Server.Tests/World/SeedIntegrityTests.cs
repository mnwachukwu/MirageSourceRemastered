using System.Text.Json;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>The shipped seed in <c>server/src/Mirage.Server.Host/data</c>, checked against the engine that
/// has to load it.
///
/// <para><b>Why this fixture exists.</b> Every other economy test here is formula-level, so anything a
/// GENERATOR authors was unpinned by construction — and the generators live outside the repo entirely, in
/// <c>.Tools</c>, where no test can reach them. That gap has already produced real bugs of a kind the
/// compiler cannot see: <c>gen-npcs.mjs</c> hand-writes JSON and kept emitting a field the C# side had
/// renamed, which would have made every gold drop read as zero; and <c>gen-items --apply</c> rewrites all
/// 558 item files WITHOUT prices, so regenerating the armory for any reason silently zeroes every price
/// unless <c>seed-prices</c> is run after it. Neither errors. Both are caught below.</para>
///
/// <para><b>An absent or empty seed is a SKIP, not a failure.</b> <c>data/</c> is the shipped default
/// configuration and is deliberately allowed to be empty — a fresh clone boots an empty world and pads
/// blank maps. A fixture that failed on that would make the suite red for anyone who has not populated
/// it, which is the fastest way to get a fixture deleted.</para></summary>
[TestFixture]
public class SeedIntegrityTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static Dictionary<int, ItemRecord> _items = new();
    private static Dictionary<int, NpcRecord> _npcs = new();
    private static Dictionary<int, SpellRecord> _spells = new();
    private static Dictionary<int, ClassRecord> _classes = new();

    [OneTimeSetUp]
    public void LoadSeed()
    {
        string? data = FindDataDir();
        if (data is null) return;
        _items = LoadAll<ItemRecord>(data, "items", "item");
        _npcs = LoadAll<NpcRecord>(data, "npcs", "npc");
        _spells = LoadAll<SpellRecord>(data, "spells", "spell");
        _classes = LoadAll<ClassRecord>(data, "classes", "class");
    }

    // Walk up from the test binary to the repo root (marked by the solution file), rather than counting
    // "..\..\..\.." — the bin depth changes with configuration and target framework.
    private static string? FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mirage.slnx")))
            dir = dir.Parent;
        if (dir is null) return null;
        string data = Path.Combine(dir.FullName, "server", "src", "Mirage.Server.Host", "data");
        return Directory.Exists(data) ? data : null;
    }

    /// <summary>Keyed by the NUMBER in the filename, never by enumeration order: a directory listing sorts
    /// "item1, item10, item100, item2", so an index into a flat list returns the WRONG record rather than
    /// none — a failure mode that reads as bad data instead of a bad lookup.</summary>
    private static Dictionary<int, T> LoadAll<T>(string data, string folder, string prefix) where T : class
    {
        var result = new Dictionary<int, T>();
        string dir = Path.Combine(data, folder);
        if (!Directory.Exists(dir)) return result;
        foreach (string path in Directory.GetFiles(dir, prefix + "*.json"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(path).AsSpan(prefix.Length), out int num)) continue;
            var rec = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
            if (rec is not null) result[num] = rec;
        }
        return result;
    }

    private static void RequireSeed()
    {
        if (_items.Count == 0)
            Assert.Ignore("No seed authored in server/src/Mirage.Server.Host/data — nothing to check.");
    }

    // ── The seed is canonical on disk ─────────────────────────────────────────

    [Test]
    public void EveryRecord_IsAlreadyNormalized()
    {
        RequireSeed();
        // Normalize is what the server runs on load and the editor on save, and it CLEARS fields that do
        // not apply to a record's type. If running it changes a seed file, the file on disk is carrying
        // values the engine will silently discard — a generator writing Power onto a potion, say. The
        // seed should already be the canonical form of itself.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items)
            {
                var before = JsonSerializer.Serialize(item, Json);
                item.Normalize();
                Assert.That(JsonSerializer.Serialize(item, Json), Is.EqualTo(before),
                    $"item{num} ({item.Name}) is not canonical — Normalize changed it");
            }
            foreach (var (num, npc) in _npcs)
            {
                var before = JsonSerializer.Serialize(npc, Json);
                npc.Normalize();
                Assert.That(JsonSerializer.Serialize(npc, Json), Is.EqualTo(before),
                    $"npc{num} ({npc.Name}) is not canonical — Normalize changed it");
            }
        });
    }

    // ── Referential integrity ─────────────────────────────────────────────────

    [Test]
    public void EveryDropLine_NamesAnItemThatExists()
    {
        RequireSeed();
        // A drop naming a missing item is INERT, not loud: the roller skips it. So a drop table that lost
        // its footing during a renumber pays out nothing and reports nothing. The armory has been
        // renumbered twice in two days (potions 3 -> 15 tiers, then treasure), and both times this is the
        // check that would have caught a stale table.
        Assert.Multiple(() =>
        {
            foreach (var (num, npc) in _npcs)
                foreach (var d in npc.Drops ?? [])
                    Assert.That(_items.ContainsKey(d.ItemNum), Is.True,
                        $"npc{num} ({npc.Name}) drops item {d.ItemNum}, which does not exist");
        });
    }

    [Test]
    public void EveryStartingLoadout_NamesThingsThatExist()
    {
        RequireSeed();
        Assert.Multiple(() =>
        {
            foreach (var (num, cls) in _classes)
            {
                foreach (var s in cls.StartingItems ?? [])
                    Assert.That(_items.ContainsKey(s.ItemNum), Is.True,
                        $"class{num} ({cls.Name}) starts with item {s.ItemNum}, which does not exist");
                foreach (int spellNum in cls.StartingSpells ?? [])
                    Assert.That(_spells.ContainsKey(spellNum), Is.True,
                        $"class{num} ({cls.Name}) starts knowing spell {spellNum}, which does not exist");
            }
        });
    }

    [Test]
    public void EveryScroll_TeachesASpellThatExists()
    {
        RequireSeed();
        // A scroll is the ONLY way to learn a spell, so a scroll pointing at nothing is not a cosmetic
        // fault — it is a permanently unreachable ability.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items.Where(kv => kv.Value.Type == ItemType.Spell))
                Assert.That(_spells.ContainsKey(item.SpellNum), Is.True,
                    $"item{num} ({item.Name}) teaches spell {item.SpellNum}, which does not exist");
        });
    }

    // ── What the generators own, and nothing else guards ──────────────────────

    [Test]
    public void EveryPriceableItem_CarriesItsSeededPrice()
    {
        RequireSeed();
        // THE TRAP THIS EXISTS FOR: gen-items --apply rewrites every item file and does NOT write price;
        // that is seed-prices' stage. Regenerating the armory without re-running it leaves 558 items at
        // price 0 — a world of free gear, with nothing raising a hand.
        Assert.Multiple(() =>
        {
            foreach (var (num, item) in _items)
            {
                int derived = EconomyFormulas.ItemValue(item, _spells.GetValueOrDefault(item.SpellNum));
                if (derived <= 0) continue;   // currency, keys, treasure — authored or genuinely worthless
                Assert.That(item.Price, Is.EqualTo(derived),
                    $"item{num} ({item.Name}) is priced {item.Price} but the formula says {derived} — "
                    + "run seed-prices.cs after any gen-items --apply");
            }
        });
    }

    [Test]
    public void Durability_RisesWithTier_AndWithBulk()
    {
        RequireSeed();
        var gear = _items.Values.Where(i => ItemRecord.IsEquipment(i.Type)).ToArray();
        if (gear.Length == 0) Assert.Ignore("no equipment authored");

        // The curve lives in gen-items (sqrt of level x bulk) and is unreachable from here, so what is
        // pinned is its SHAPE: a higher tier is sturdier, and within a tier a heavier piece is sturdier
        // than a lighter one. Bulk is not stored, but Power carries it — both come off the same
        // multiplier — so within a slot and tier, more Power must mean more durability.
        var tiers = gear.Select(i => (int)i.LevelReq).Distinct().OrderBy(l => l).ToArray();
        Assert.Multiple(() =>
        {
            // Compared BAND TO BAND — floor against floor and ceiling against ceiling — not every piece
            // against every piece. A tier-5 Tower Shield genuinely outlasts a tier-10 Buckler, because
            // bulk spans 0.75-1.25 while one rung is a smaller step than that. What must hold is that the
            // whole range shifts up, and comparing the max of one tier to the min of the next would
            // forbid the bulk spread rather than test the curve.
            for (int i = 1; i < tiers.Length; i++)
            {
                var lower = gear.Where(g => g.LevelReq == tiers[i - 1]).ToArray();
                var higher = gear.Where(g => g.LevelReq == tiers[i]).ToArray();
                Assert.That(higher.Min(g => g.Durability), Is.GreaterThan(lower.Min(g => g.Durability)),
                    $"tier {tiers[i]}'s lightest piece is no sturdier than tier {tiers[i - 1]}'s");
                Assert.That(higher.Max(g => g.Durability), Is.GreaterThan(lower.Max(g => g.Durability)),
                    $"tier {tiers[i]}'s heaviest piece is no sturdier than tier {tiers[i - 1]}'s");
            }

            foreach (var slot in gear.GroupBy(g => (g.Type, g.LevelReq)))
            {
                var byPower = slot.OrderBy(g => g.Power).ToArray();
                for (int i = 1; i < byPower.Length; i++)
                    Assert.That(byPower[i].Durability, Is.GreaterThanOrEqualTo(byPower[i - 1].Durability),
                        $"{byPower[i].Name} carries more Power than {byPower[i - 1].Name} but less durability");
            }
        });
    }

    [Test]
    public void Treasure_IsPricedAndProtected()
    {
        RequireSeed();
        // Treasure is typed None and priced by hand — the one item family whose worth is authored rather
        // than derived, and therefore the one nothing else can check.
        var treasure = _items.Where(kv => kv.Value.Type == ItemType.None && kv.Value.Price > 0).ToArray();
        if (treasure.Length == 0) Assert.Ignore("no treasure authored");

        Assert.Multiple(() =>
        {
            foreach (var (num, t) in treasure)
            {
                Assert.That(t.NonJunkable, Is.True,
                    $"item{num} ({t.Name}) is treasure but junkable — the generic vendor would buy it and "
                    + "the fence would be pointless");
                Assert.That(t.Name, Is.Not.Empty, $"item{num} is priced treasure with no name");
                Assert.That(EconomyFormulas.ItemValue(t), Is.Zero,
                    $"item{num} ({t.Name}) must not be derivable, or seed-prices would overwrite it");
            }
            Assert.That(_items.Values.Any(i => i.Type == ItemType.Currency && i.NonJunkable), Is.True,
                "gold must be NonJunkable — dumping currency for a fraction of itself is nonsense");
        });
    }

    [Test]
    public void GoldDrops_CarryARealQuantity()
    {
        RequireSeed();
        // The bug this is written for actually happened. gen-npcs.mjs hand-writes its JSON, so when the C#
        // field was renamed Value -> Quantity the generator kept emitting the old key; every gold line
        // would have deserialized to quantity 0 (clamped to 1 at roll time) and the entire gold economy
        // would have vanished with no error anywhere. A JS generator is outside the compiler's reach, and
        // this is the only thing standing where the compiler would otherwise be.
        var goldLines = _npcs.SelectMany(kv => (kv.Value.Drops ?? []).Select(d => (Npc: kv.Value, Drop: d)))
                             .Where(x => x.Drop.ItemNum == Constants.GoldItemIndex)
                             .ToArray();
        if (goldLines.Length == 0) Assert.Ignore("no gold drops authored");

        Assert.Multiple(() =>
        {
            foreach (var (npc, drop) in goldLines)
                Assert.That(drop.Quantity, Is.GreaterThan(0),
                    $"{npc.Name} drops gold with no quantity — check that gen-npcs.mjs still emits "
                    + "'quantity', which the compiler cannot verify for a JS generator");
        });
    }
}
