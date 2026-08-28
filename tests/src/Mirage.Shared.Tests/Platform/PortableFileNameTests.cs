using Mirage.Shared;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Shared.Tests;

/// <summary>
/// Naming rules that answer the same on every platform.
///
/// <para>🔴 The cases below are not hypothetical. <c>a:b</c> and <c>why?</c> are legal directory names on
/// Linux and macOS and illegal on Windows, so a check built on <see cref="Path.GetInvalidFileNameChars"/>
/// passes them on two of the three platforms the game ships to — and a world folder, an exported PNG or a
/// config file named that way cannot be cloned, unzipped or checked out on Windows at all.</para>
///
/// <para>A Windows developer cannot reach these by running the suite locally: there the platform set and the
/// portable set are identical and every assertion passes either way. They fail only on the Linux and macOS
/// CI legs, which is exactly why the convention test at the bottom reads the source instead.</para>
/// </summary>
[TestFixture]
public class PortableFileNameTests
{
    /// <summary>Illegal on Windows, legal on POSIX — the whole point of the type.</summary>
    [TestCase("what/now")]
    [TestCase("a:b")]
    [TestCase("why?")]
    [TestCase("star*")]
    [TestCase("pipe|line")]
    [TestCase("quote\"d")]
    [TestCase("back\\slash")]
    [TestCase("less<than")]
    [TestCase("more>than")]
    public void ANameWindowsRefuses_IsRejectedEverywhere(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    [TestCase("Demo Landia")]
    [TestCase("Untitled World")]
    [TestCase("Mundo sin titulo")]
    [TestCase("Monde sans titre")]
    [TestCase("map-0007-Town Square.png")]
    [TestCase("under_scored")]
    [TestCase(".hidden")]
    public void AnOrdinaryName_IsAccepted(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.True);
    }

    /// <summary>Every locale's untitled-world folder name has to be creatable on every platform, since that
    /// is the folder an unnamed world gets.</summary>
    [Test]
    public void EveryUntitledWorldName_IsPortable()
    {
        foreach (string name in new[] { "Untitled World", "Mundo sin titulo", "Monde sans titre", "Mundo sem titulo" })
        {
            Assert.That(PortableFileName.IsValid(name), Is.True, name);
        }
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase("..")]
    public void ANameThatIsNotAName_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    /// <summary>Windows drops a trailing dot or space without saying so, so the folder that appears is not
    /// the one that was asked for — and a later lookup by the requested name misses it.</summary>
    [TestCase("trailing ")]
    [TestCase("trailing.")]
    public void ANameWindowsWouldSilentlyRewrite_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    /// <summary>DOS device names are refused by Windows in any casing and at any extension. POSIX takes them,
    /// so a world called <c>aux</c> authored on Linux is unopenable on Windows.</summary>
    [TestCase("NUL")]
    [TestCase("nul")]
    [TestCase("CON")]
    [TestCase("aux")]
    [TestCase("COM1")]
    [TestCase("lpt9")]
    [TestCase("nul.png")]
    public void AReservedDeviceName_IsRejected(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.False);
    }

    [TestCase("CONtinue")]
    [TestCase("NULL")]
    [TestCase("COM10")]
    public void ANameThatMerelyStartsLikeADevice_IsAccepted(string name)
    {
        Assert.That(PortableFileName.IsValid(name), Is.True);
    }

    // ── Sanitize ────────────────────────────────────────────────────────────────

    [TestCase("a:b", "a_b")]
    [TestCase("why?", "why_")]
    [TestCase("what/now", "what_now")]
    [TestCase("Town Square", "Town Square")]
    [TestCase("trailing.", "trailing")]
    [TestCase("trailing ", "trailing")]
    public void Sanitize_RewritesWhatItMust_AndLeavesTheRest(string input, string expected)
    {
        Assert.That(PortableFileName.Sanitize(input), Is.EqualTo(expected));
    }

    /// <summary>Whatever goes in, what comes out is usable — including the inputs that sanitize to nothing.</summary>
    [TestCase("")]
    [TestCase("???")]
    [TestCase("...")]
    [TestCase("   ")]
    [TestCase("NUL")]
    [TestCase("a:b")]
    [TestCase("Town Square")]
    public void Sanitize_AlwaysProducesAValidName(string input)
    {
        Assert.That(PortableFileName.IsValid(PortableFileName.Sanitize(input)), Is.True);
    }

    /// <summary>The hardcoded set has to remain at least as strict as whatever platform is running the
    /// suite — the one place asking the OS is the right move, because here it is the lower bound being
    /// checked rather than the rule being enforced. On Windows this pins the full 41; on Linux and macOS it
    /// confirms the two.</summary>
    [Test]
    public void TheHardcodedSet_CoversThisPlatformsOwn()
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            // Control characters are covered by the c < ' ' arm rather than by the array.
            if (c < ' ') continue;
            Assert.That(PortableFileName.InvalidChars, Does.Contain(c),
                $"This platform rejects U+{(int)c:X4} and PortableFileName does not.");
        }
    }

    // ── The convention ──────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 Production code never asks the OS what a legal filename is.
    ///
    /// <para>This is a source scan because it cannot be a behavioural test: on Windows
    /// <c>Path.GetInvalidFileNameChars()</c> returns the same set <see cref="PortableFileName.InvalidChars"/>
    /// declares, so a call to it behaves identically and every assertion above still passes. The difference
    /// appears only on Linux and macOS — which means a reintroduction is invisible on the machine that writes
    /// it and breaks CI for everyone else.</para>
    /// </summary>
    [Test]
    public void NoProductionCode_AsksThePlatformWhatALegalNameIs()
    {
        string root = typeof(PortableFileNameTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
        Assert.That(Directory.Exists(root), Is.True, $"Repository root not found: {root}");

        var offenders = new List<string>();
        foreach (string area in new[] { "client", "server", "editor", "shared" })
        {
            string dir = Path.Combine(root, area);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                // The one file allowed to name them is the one that replaces them.
                if (Path.GetFileName(file) == "PortableFileName.cs") continue;

                string text = File.ReadAllText(file);
                if (text.Contains("GetInvalidFileNameChars") || text.Contains("GetInvalidPathChars"))
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.That(offenders, Is.Empty,
            "These ask the CURRENT OS which characters a filename may hold. Windows names 41 and POSIX names "
            + "2, so the rule they enforce depends on who ran the code, and a name accepted on Linux can be a "
            + "file Windows cannot check out. Use PortableFileName.IsValid / .Sanitize instead:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}
