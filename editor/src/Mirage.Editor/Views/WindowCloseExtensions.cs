using Avalonia.Controls;
using Avalonia.Threading;

namespace Mirage.Editor.Views;

/// <summary>Closing a dialog on a later dispatcher beat instead of inside the input handler that asked.</summary>
internal static class WindowCloseExtensions
{
    /// <summary>Queues the close, so window disposal runs after the current input dispatch and render commit
    /// rather than on top of them.
    ///
    /// <para>Closing straight from a Click or KeyDown handler puts <c>WindowImpl.Dispose</c> on the stack while
    /// the compositor is mid-commit. On the Win32 WinUI composition backend the disposing thread then blocks on
    /// the composition lock the compositor holds while it waits for that commit, and the UI thread stops
    /// answering for good — the surface keeps presenting its last frame, so the window still looks alive.</para></summary>
    public static void CloseDeferred(this Window window) =>
        Dispatcher.UIThread.Post(() => window.Close(), DispatcherPriority.Background);

    /// <inheritdoc cref="CloseDeferred(Window)"/>
    public static void CloseDeferred(this Window window, object? result) =>
        Dispatcher.UIThread.Post(() => window.Close(result), DispatcherPriority.Background);
}
