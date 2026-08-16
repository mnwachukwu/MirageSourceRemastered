using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using Mirage.Server.Shell.ViewModels;

namespace Mirage.Server.Shell.Views;

/// <summary>
/// The operator's window.
///
/// <para><see cref="FAAppWindow"/> rather than <see cref="Window"/>: on Windows it draws its own title
/// bar, which is what lets the frame carry the app's palette instead of the system's grey. It does that
/// ONLY under <c>OperatingSystem.IsWindows()</c> — elsewhere the window keeps native decorations and
/// every bit of native window behaviour with them.</para>
/// </summary>
public sealed partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.PropertyChanged += FollowTail;
        };
    }

    /// <summary>Keeps the console pinned to its newest line — unless the operator is holding a
    /// selection, in which case scrolling would drag the view out from under what they are trying to
    /// copy. That is the whole reason to check: the pane is worth following only while nobody is
    /// reading it.</summary>
    private void FollowTail(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.ConsoleText)) return;
        var box = this.FindControl<TextBox>("ConsoleOutput");
        if (box is null || box.SelectionStart != box.SelectionEnd) return;
        // Queued: the new text is on the control but has not been measured yet, so moving the caret now
        // would land one line short every time.
        Dispatcher.UIThread.Post(() => box.CaretIndex = box.Text?.Length ?? 0, DispatcherPriority.Background);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
