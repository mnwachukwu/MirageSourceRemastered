using Mirage.Editor.ViewModels;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>
/// The recent-worlds menu shortens what it shows and keeps the whole path for the tooltip.
///
/// <para>A menu sized to a real world path is unreadable, and one that scrolls to fit is worse. What has
/// to survive the shortening is the leaf — the folder is what names the world — and enough of the root to
/// tell two checkouts of the same name apart.</para>
/// </summary>
[TestFixture]
public class RecentWorldTests
{
    private static RecentWorldViewModel Row(string path) => new(path, _ => Task.CompletedTask);

    [Test]
    public void AShortPath_IsShownWhole()
    {
        var row = Row(@"D:\Worlds\harbour");

        Assert.Multiple(() =>
        {
            Assert.That(row.DisplayPath, Is.EqualTo(@"D:\Worlds\harbour"));
            Assert.That(row.Path, Is.EqualTo(@"D:\Worlds\harbour"));
        });
    }

    [Test]
    public void ALongPath_IsShortenedButKeepsItsFolder()
    {
        string path = @"D:\Repos\MirageSourceRemastered\server\src\Mirage.Server.Host\data\brightwater";
        var row = Row(path);

        Assert.Multiple(() =>
        {
            Assert.That(row.DisplayPath, Does.EndWith("brightwater"), "the folder is what names the world");
            Assert.That(row.DisplayPath, Does.StartWith(@"D:\Repos"), "the root tells two checkouts apart");
            Assert.That(row.DisplayPath, Does.Contain("..."));
            Assert.That(row.DisplayPath, Has.Length.LessThanOrEqualTo(48));
            Assert.That(row.Path, Is.EqualTo(path), "the whole path is still there for the tooltip");
        });
    }

    /// <summary>A folder name longer than the whole budget. There is no head left to keep, and the result
    /// still has to fit.</summary>
    [Test]
    public void AFolderNameLongerThanTheBudget_StillFits()
    {
        var row = Row(@"D:\" + new string('w', 90));

        Assert.Multiple(() =>
        {
            Assert.That(row.DisplayPath, Has.Length.LessThanOrEqualTo(48));
            Assert.That(row.DisplayPath, Does.StartWith("..."));
        });
    }

    [Test]
    public void ATrailingSeparator_DoesNotCostTheFolderName()
    {
        var row = Row(@"D:\Repos\MirageSourceRemastered\server\src\Mirage.Server.Host\data\brightwater\");

        Assert.That(row.DisplayPath, Does.EndWith("brightwater"));
    }
}
