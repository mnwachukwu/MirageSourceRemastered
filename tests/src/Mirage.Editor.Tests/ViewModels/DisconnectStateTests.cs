using Mirage.Editor.Services;
using Mirage.Editor.ViewModels;
using NUnit.Framework;

namespace Mirage.Editor.Tests;

/// <summary>
/// What the window shows after a live session ends.
///
/// <para>The content area holds the open editor and the "no world open" message in one grid, and exactly
/// one of them is ever visible. A section left selected on the way out of a connection would put the
/// message on top of a fully populated editor — a right-hand panel, a tile palette and a toolbar, all for
/// a world that is not there.</para>
///
/// <para>A folder opened BEFORE connecting is still open afterwards and is what the window falls back to,
/// so the two cases answer differently.</para>
/// </summary>
[TestFixture]
public class DisconnectStateTests
{
    private static MainWindowViewModel Online()
    {
        var vm = new MainWindowViewModel(new EditorDataService(), new EditorConnection(), new EditorBitmapCache())
        {
            IsOnline = true,
        };
        vm.SelectedSection = vm.Sections.First();
        return vm;
    }

    /// <summary>The reported case: connect from the empty state, then disconnect. There is no world to
    /// fall back to, so the window has to go back to being empty.</summary>
    [Test]
    public async Task DisconnectingWithNoWorldOpen_LeavesTheWindowEmpty()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsOnline, Is.False);
            Assert.That(vm.ShowEmptyWorld, Is.True, "the empty-state message is shown");
            Assert.That(vm.CurrentEditor, Is.Null, "and nothing is shown underneath it");
            Assert.That(vm.SelectedSection, Is.Null, "so no rail row reads as selected either");
        });
    }

    /// <summary>The invariant the content area is built on, stated on its own: the message and an editor
    /// are never both up.</summary>
    [Test]
    public async Task TheMessageAndAnEditor_AreNeverBothShown()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();

        Assert.That(vm.ShowEmptyWorld && vm.CurrentEditor is not null, Is.False);
    }

    [Test]
    public async Task DisconnectingTwice_IsHarmless()
    {
        var vm = Online();

        await vm.ForceDisconnectAsync();
        await vm.ForceDisconnectAsync();

        Assert.That(vm.ShowEmptyWorld, Is.True);
    }
}
