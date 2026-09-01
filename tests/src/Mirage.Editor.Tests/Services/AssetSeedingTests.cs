using Mirage.Editor;
using Mirage.Editor.Services;
using NUnit.Framework;
using System;
using System.IO;

namespace Mirage.Editor.Tests;

/// <summary>
/// Deleting a shipped sheet has to outlive a restart.
///
/// <para>The editor re-seeds its assets folder on every launch, copying each bundled file it finds missing.
/// That is what fills a fresh install and what carries new defaults in from an update — and it is also what
/// silently undoes a deletion, because a sheet moved to the recycle bin looks exactly like a sheet that was
/// never copied. Nothing in the recycle bin survives that on its own.</para>
/// </summary>
[TestFixture]
public class AssetSeedingTests
{
    private string _root = "";
    private string _bundled = "";
    private string _assets = "";
    private string _bin = "";

    [SetUp]
    public void MakeFolders()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirage-seed-" + Guid.NewGuid().ToString("N"));
        _bundled = Path.Combine(_root, "bundled");
        _assets = Path.Combine(_root, "assets");
        _bin = Path.Combine(_assets, SheetLibrary.RecycleFolder);

        Directory.CreateDirectory(Path.Combine(_bundled, "tiles"));
        File.WriteAllText(Path.Combine(_bundled, "tiles", "0_Tiles.bmp"), "shipped art");
    }

    [TearDown]
    public void DropFolders()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a temp folder is not worth failing on */ }
    }

    private string SeededSheet => Path.Combine(_assets, "tiles", "0_Tiles.bmp");

    /// <summary>The ordinary job: a missing bundled file is put back. Without this half the test below
    /// would pass against a seeder that copies nothing at all.</summary>
    [Test]
    public void AMissingBundledSheetIsSeeded()
    {
        EditorPaths.SeedAssetsFrom(_bundled, _assets);

        Assert.That(File.Exists(SeededSheet), Is.True);
    }

    /// <summary>A sheet the author deleted stays deleted across a restart. This is the whole reason the
    /// recycle bin keeps a record at all.</summary>
    [Test]
    public void ADeletedBundledSheetIsNotSeededBack()
    {
        EditorPaths.SeedAssetsFrom(_bundled, _assets);
        var sheet = SheetLibrary.Scan(Path.Combine(_assets, "tiles"), 256, Path.Combine(_bundled, "tiles")).Sheets[0];
        Assert.That(sheet.IsBundled, Is.True, "the sheet has to be recognised as a shipped one");

        SheetLibrary.Delete(sheet, _bin, "tiles/0_Tiles.bmp");
        EditorPaths.SeedAssetsFrom(_bundled, _assets);

        Assert.That(File.Exists(SeededSheet), Is.False, "the seeder put back a sheet that was deleted");
    }

    /// <summary>Restoring hands the sheet back to the seeder's care, so a later disappearance is filled in
    /// again. Without this the record would be a one-way door.</summary>
    [Test]
    public void ARestoredSheetIsSeededAgainOnceItGoesMissing()
    {
        EditorPaths.SeedAssetsFrom(_bundled, _assets);
        var sheet = SheetLibrary.Scan(Path.Combine(_assets, "tiles"), 256, Path.Combine(_bundled, "tiles")).Sheets[0];
        SheetLibrary.Delete(sheet, _bin, "tiles/0_Tiles.bmp");

        string recycled = SheetLibrary.ListRecycled(_bin)[0];
        SheetLibrary.Restore(recycled, Path.Combine(_assets, "tiles"), 0, "tiles/0_Tiles.bmp");
        File.Delete(SeededSheet);
        EditorPaths.SeedAssetsFrom(_bundled, _assets);

        Assert.That(File.Exists(SeededSheet), Is.True);
    }

    /// <summary>A sheet the author added themselves is never touched by the seeder, deleted or not — it was
    /// never a bundled file, so there is nothing to put back.</summary>
    [Test]
    public void AnImportedSheetIsNeverSeeded()
    {
        string tiles = Path.Combine(_assets, "tiles");
        Directory.CreateDirectory(tiles);
        File.WriteAllText(Path.Combine(tiles, "4_mine.bmp"), "my art");

        var sheet = SheetLibrary.Scan(tiles, 256, Path.Combine(_bundled, "tiles")).Sheets[0];
        Assert.That(sheet.IsBundled, Is.False);

        SheetLibrary.Delete(sheet, _bin, "tiles/4_mine.bmp");
        EditorPaths.SeedAssetsFrom(_bundled, _assets);

        Assert.That(File.Exists(Path.Combine(tiles, "4_mine.bmp")), Is.False);
    }
}
