using CommunityToolkit.Mvvm.Input;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// One entry of the recent-worlds menu.
///
/// <para>It carries its own command, so the menu item binds to itself and never reaches back up the visual
/// tree for the window's view-model. Bindings here are resolved by reflection at run time, so an ancestor
/// lookup that names a type is checked by nothing until the window is built.</para>
///
/// <para>Nothing on it is localized: a path is a path.</para>
/// </summary>
public sealed class RecentWorldViewModel(string path, Func<string, Task> open)
{
    /// <summary>The whole path, shown on hover.</summary>
    public string Path { get; } = path;

    /// <summary>What the menu shows. A world path is long enough to make a menu unreadable, so the middle
    /// is dropped and the two ends kept: the folder is what identifies the world, and the root is what
    /// tells two checkouts apart.</summary>
    public string DisplayPath { get; } = Shorten(path, 48);

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(path));

    private static string Shorten(string path, int max)
    {
        if (path.Length <= max) return path;

        string leaf = System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        // The leaf alone can be longer than the budget, in which case there is no head to keep.
        int head = max - leaf.Length - 4;
        return head < 4 ? string.Concat("...", leaf[Math.Max(0, leaf.Length - max + 3)..])
                        : $"{path[..head]}...{System.IO.Path.DirectorySeparatorChar}{leaf}";
    }
}
