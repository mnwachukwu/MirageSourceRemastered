using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System.Collections;
using System.Reflection;
namespace Mirage.Editor.Tests;

/// <summary>
/// Locks the map's online round-trip: every authored field on <see cref="MapRecord"/> survives
/// <see cref="EditorDataService.BuildSaveMapPacket"/> and <see cref="EditorDataService.MapRecordFromPacket"/>.
///
/// <para>The mappers are hand-written field lists, so a field added to the record and forgotten in one of
/// them is dropped in silence: a map saved online loses it and nothing reports anything. This walks
/// <see cref="MapRecord"/> by reflection and fails on a property it cannot populate, so a new field has to
/// be taught to the test before it can pass unnoticed.</para>
/// </summary>
[TestFixture]
public class MapPacketRoundTripTests
{
    // Fixed rather than generated: a light carries its id, so the round-trip has to preserve that too.
    private static readonly Guid LightId = new("11111111-2222-3333-4444-555555555555");

    private static MapRecord Populated()
    {
        var map = new MapRecord();
        int seed = 0;
        foreach (var p in Authored())
        {
            seed++;
            p.SetValue(map, ValueFor(p, seed));
        }

        map.Tile[3, 4] = new TileRecord
        {
            Ground = [11, 12, 0, 0, 0],
            Fringe = [13, 0, 0, 0, 0],
            Type = TileType.Warp,
            WarpMap = 21,
            WarpX = 5,
            WarpY = 6,
        };
        map.Npcs.Add(new MapNpcEntry(9, 2, 3));
        map.Lights.Add(new PlacedLight(LightId, 7, 8, LightSpec.Torch));
        return map;
    }

    // The collections are populated by hand above; the scalars come from reflection.
    private static PropertyInfo[] Authored() =>
        [.. typeof(MapRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.PropertyType != typeof(TileRecord[,])
                        && !typeof(IEnumerable).IsAssignableFrom(p.PropertyType))];

    // Distinct per property so a mapper that crosses two fields reads as a mismatch rather than a match.
    private static object ValueFor(PropertyInfo p, int n)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(string)) return $"v{n}-{p.Name}";
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return n + 100;
        if (t == typeof(short)) return (short)(n + 100);
        if (t.IsEnum) return Enum.GetValues(t).GetValue(1) ?? Enum.GetValues(t).GetValue(0)!;
        Assert.Fail($"MapRecord.{p.Name} is a {t.Name}, which this test cannot populate. Teach ValueFor "
                    + "about it, and check BuildSaveMapPacket/MapRecordFromPacket both carry it.");
        return null!;
    }

    private static MapRecord RoundTrip(MapRecord map) =>
        EditorDataService.MapRecordFromPacket(EditorDataService.BuildSaveMapPacket(12, map).Map);

    [Test]
    public void EveryAuthoredField_SurvivesTheOnlineRoundTrip()
    {
        var original = Populated();
        var back = RoundTrip(original);

        foreach (var p in Authored())
            Assert.That(p.GetValue(back), Is.EqualTo(p.GetValue(original)),
                $"MapRecord.{p.Name} did not survive the save/load round-trip. Both "
                + "BuildSaveMapPacket and MapRecordFromPacket have to carry it.");
    }

    [Test]
    public void Tiles_Npcs_AndLights_SurviveTheOnlineRoundTrip()
    {
        var original = Populated();
        var back = RoundTrip(original);
        var t = back.Tile[3, 4];

        Assert.Multiple(() =>
        {
            Assert.That(t.Ground, Is.EqualTo(original.Tile[3, 4].Ground));
            Assert.That(t.Fringe, Is.EqualTo(original.Tile[3, 4].Fringe));
            Assert.That(t.Type, Is.EqualTo(TileType.Warp));
            Assert.That(t.WarpMap, Is.EqualTo((short)21));
            Assert.That(back.Npcs, Has.Count.EqualTo(1));
            Assert.That(back.Npcs[0], Is.EqualTo(new MapNpcEntry(9, 2, 3)));
            Assert.That(back.Lights, Has.Count.EqualTo(1));
            Assert.That(back.Lights[0], Is.EqualTo(new PlacedLight(LightId, 7, 8, LightSpec.Torch)));
        });
    }

    /// <summary>A default tile stays default. The packet omits them, so a value here would mean the
    /// omit-and-rebuild path invents tiles.</summary>
    [Test]
    public void DefaultTiles_AreOmittedAndRebuiltDefault()
    {
        var back = RoundTrip(new MapRecord());

        for (int x = 0; x <= Constants.MaxMapX; x++)
            for (int y = 0; y <= Constants.MaxMapY; y++)
                Assert.That(back.Tile[x, y].Type, Is.EqualTo(TileType.Walkable));
    }
}
