using NUnit.Framework;
using System.Reflection;
using System.Text.Json;

namespace Mirage.Server.Tests;

/// <summary>
/// A map group's number lives in two places: its filename, and an <c>index</c> field inside the record.
///
/// <para>The field is not redundant — the guild, territory and combat code holds a <c>MapGroupRecord</c>
/// detached from any dictionary key and asks it which group it is. But nothing used to keep the two in
/// agreement: both loaders key by filename and neither stamped the field, so a hand-edited or copied file
/// could carry someone else's number and be believed. That surfaces as territory scored against the wrong
/// group, which is a long way from the file that caused it.</para>
///
/// <para>Both loaders now stamp <c>Index</c> from the filename, making the field derived rather than
/// authored. This is the other half: the files on disk agree too, so nothing depends on the repair.</para>
/// </summary>
[TestFixture]
public class MapGroupIndexTests
{
    /// <summary>The seed's map-group folder, or null when the seed carries none.
    /// <para>Repo root comes from the baked-in metadata rather than a walk up from the output directory,
    /// so a skip here means one thing only — the seed has no map groups — and never "the test could not
    /// find the repository", which would look identical from the outside.</para></summary>
    private static string? MapGroupsDir()
    {
        string root = typeof(MapGroupIndexTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");

        string path = Path.Combine(root, "server", "src", "Mirage.Server.Host", "data", "map_groups");
        return Directory.Exists(path) ? path : null;
    }

    [Test]
    public void EveryMapGroupFile_AgreesWithItsFilename()
    {
        string? dir = MapGroupsDir();
        if (dir is null) Assert.Ignore("The tracked seed carries no map_groups; the five in the live world are untracked.");

        var problems = new List<string>();
        int checked_ = 0;
        foreach (string path in Directory.GetFiles(dir!, "mapgroup*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(stem.AsSpan("mapgroup".Length), out int fromName)) continue;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("index", out var idx)) continue;
            checked_++;
            if (idx.GetInt32() != fromName)
                problems.Add($"{Path.GetFileName(path)} carries index {idx.GetInt32()}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
            Assert.That(checked_, Is.GreaterThan(0), "no map group carried an index field to check");
        });
    }
}
