using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Mirage.Shared.Tests;

/// <summary>
/// Three sizes are 16x12, and they are three different things: how big a map is, how big the camera's
/// window is, and how far gameplay reaches.
///
/// <para>Because the three numbers match, one can be written in terms of another and everything still
/// works — until a map is 256x256, at which point that map silently hands its occupants a larger camera
/// and a longer spell range than a small one, with no test failing and nothing to see in a diff.</para>
///
/// <para>Only the source can be checked here: at runtime all three are the number 16, so an assertion that
/// they are equal proves nothing and an assertion that they differ would be false. So these read
/// <c>Constants.cs</c> and <c>WorldCoordHelper.cs</c> and hold each group to declaring its own literals.</para>
/// </summary>
[TestFixture]
public class MapSizeIndependenceTests
{
    private static string RepoRoot()
    {
        string root = typeof(MapSizeIndependenceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");
        return root;
    }

    private static string SharedSource(string file)
    {
        string path = Path.Combine(RepoRoot(), "shared", "src", "Mirage.Shared", file);
        Assert.That(File.Exists(path), Is.True, $"Source not found: {path}");
        return File.ReadAllText(path);
    }

    // The right-hand side of `public const int Name = ...;`, wherever it is declared.
    private static string Definition(string source, string name)
    {
        var m = Regex.Match(source, $@"public\s+const\s+int\s+{Regex.Escape(name)}\s*=\s*([^;]+);");
        Assert.That(m.Success, Is.True, $"No `public const int {name}` found. If it was renamed, this test "
                                      + "has to be taught the new name — do not delete it.");
        return m.Groups[1].Value.Trim();
    }

    // ── The camera does not read a map's size ─────────────────────────────────

    [TestCase("ViewportTilesX")]
    [TestCase("ViewportTilesY")]
    public void TheViewport_IsNotDerivedFromTheMapSize(string name)
    {
        string definition = Definition(SharedSource("Constants.cs"), name);

        Assert.That(definition, Does.Match(@"^\d+$"),
            $"Constants.{name} is `{definition}`, not a literal. The camera's window is a property of the "
            + "render target; deriving it from a map's size makes a large map show more of the world.");
    }

    // ── Gameplay reach does not read a map's size ─────────────────────────────

    /// <summary>The spell circle is pinned to the VIEWPORT's short half-extent. Off the map size it would
    /// grow with the map, so casting range would depend on where you are standing.</summary>
    [Test]
    public void TheSpellRadius_IsDerivedFromTheViewportAndNothingElse()
    {
        string definition = Definition(SharedSource("Constants.cs"), "SpellRangeTiles");

        Assert.Multiple(() =>
        {
            Assert.That(definition, Does.Contain("ViewportTilesY"),
                "the spell circle is the largest one fitting the viewport, so it is measured from the viewport");
            foreach (string mapName in (string[])["MaxMapX", "MaxMapY", "DefaultMapWidth", "DefaultMapHeight"])
            {
                Assert.That(definition, Does.Not.Contain(mapName),
                    $"SpellRangeTiles reads {mapName}: a large map would grant a longer cast.");
            }
        });
    }

    // ── The map size does not read the camera ─────────────────────────────────

    [TestCase("DefaultMapWidth")]
    [TestCase("DefaultMapHeight")]
    public void TheDefaultMapSize_IsNotDerivedFromTheViewport(string name)
    {
        string definition = Definition(SharedSource("Constants.cs"), name);

        Assert.That(definition, Does.Match(@"^\d+$"),
            $"Constants.{name} is `{definition}`, not a literal. A new map's size is an authoring default; "
            + "tying it to the camera means resizing the window resizes maps.");
    }

    /// <summary>WorldCoordHelper re-exports both groups under its own names. Each alias must point at its
    /// OWN group's constant; these four lines are where crossing them is easiest and least visible.</summary>
    [TestCase("ViewportTilesX", "Constants.ViewportTilesX")]
    [TestCase("ViewportTilesY", "Constants.ViewportTilesY")]
    [TestCase("MapTilesX", "Constants.DefaultMapWidth")]
    [TestCase("MapTilesY", "Constants.DefaultMapHeight")]
    public void WorldCoordHelper_AliasesEachGroupToItself(string name, string expected)
    {
        Assert.That(Definition(SharedSource("WorldCoordHelper.cs"), name), Is.EqualTo(expected));
    }

    // ── And the values themselves, so a typo in any literal is caught ─────────

    [Test]
    public void TheThreeGroupsStillAgreeOnTheNumbersTheyHappenToShare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Constants.DefaultMapWidth, Is.EqualTo(16));
            Assert.That(Constants.DefaultMapHeight, Is.EqualTo(12));
            Assert.That(Constants.MaxMapX, Is.EqualTo(15));
            Assert.That(Constants.MaxMapY, Is.EqualTo(11));
            Assert.That(Constants.ViewportTilesX, Is.EqualTo(16));
            Assert.That(Constants.ViewportTilesY, Is.EqualTo(12));
            Assert.That(Constants.SpellRangeTiles, Is.EqualTo(5), "the r=5 circle every range test is written against");
        });
    }
}
