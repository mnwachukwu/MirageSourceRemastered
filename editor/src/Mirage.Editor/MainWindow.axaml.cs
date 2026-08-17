using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Windowing;
using Mirage.Editor.Localization;
using Mirage.Editor.ViewModels;
using Mirage.Editor.Views;
using Mirage.Shared;

namespace Mirage.Editor;

/// <summary>
/// Code-behind for the editor shell window. Owns the three things a view-model cannot: the
/// localized menu chrome, persisted window geometry, and dialog ownership.
/// <para><see cref="OnDataContextChanged"/> supplies the view-model's <c>Show…Async</c> delegates,
/// which is how <see cref="MainWindowViewModel"/> opens dialogs without referencing a View type.</para>
///
/// <para><see cref="FAAppWindow"/> rather than <see cref="Window"/>, matching the server window: on
/// Windows it draws its own title bar, so the frame carries the app's palette instead of the system's
/// grey. It does that ONLY under <c>OperatingSystem.IsWindows()</c> — elsewhere the window keeps native
/// decorations and every bit of native window behaviour with them. The editor was left on a plain
/// Window when the shell was converted, which is why the two apps disagreed about their own chrome.</para>
/// </summary>
public partial class MainWindow : FAAppWindow
{
    // Set once the unsaved-changes prompt has been answered, so the second Close() call that
    // actually shuts the window down doesn't re-run the guard and prompt again.
    private bool _skipCloseGuard;

    public MainWindow()
    {
        InitializeComponent();

        ApplyStrings();
        RefreshLanguageMenu();

        var settings = AppSettings.Current;
        if (settings.WindowWidth.HasValue) Width = settings.WindowWidth.Value;
        if (settings.WindowHeight.HasValue) Height = settings.WindowHeight.Value;
        if (settings.WindowMaximized) WindowState = WindowState.Maximized;

        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
    }

    /// <summary>Push the current language's strings into the window chrome. Re-run whenever the
    /// language changes, since these captions are set in code rather than bound.</summary>
    private void ApplyStrings()
    {
        Title = $"{Constants.GameName} — {EditorStrings.Get(EditorStrings.MainWindow_Title)}";
        _helpMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpMenu);
        _helpMapEditorItem.Header = EditorStrings.Get(EditorStrings.MainWindow_HelpMapEditor);
        _languageMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_LanguageMenu);
        _exportMenu.Header = EditorStrings.Get(EditorStrings.MainWindow_ExportMenu);
        _exportMapItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportMapButton);
        _exportAreaItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportAreaButton);
        _exportWorldItem.Header = EditorStrings.Get(EditorStrings.MapEditor_ExportWorldButton);
        ToolTip.SetTip(_exportMapItem, EditorStrings.Get(EditorStrings.MapEditor_ExportMapTooltip));
        ToolTip.SetTip(_exportAreaItem, EditorStrings.Get(EditorStrings.MapEditor_ExportAreaTooltip));
        ToolTip.SetTip(_exportWorldItem, EditorStrings.Get(EditorStrings.MapEditor_ExportWorldTooltip));
        _connectBtn.Content = EditorStrings.Get(EditorStrings.Common_Connect);
        _disconnectBtn.Content = EditorStrings.Get(EditorStrings.MainWindow_DisconnectButton);
    }

    /// <summary>Rebuild the language menu from the locale files found on disk, with the active one
    /// checked. Each item captures its own locale so the click handler picks the right one.</summary>
    private void RefreshLanguageMenu()
    {
        _languageMenu.ItemsSource = null;
        var items = new List<MenuItem>();
        string current = AppSettings.Current.Language;
        foreach (var (locale, displayName) in EditorStrings.GetAvailableLanguages(EditorStrings.LangDir))
        {
            string capturedLocale = locale;
            var item = new MenuItem
            {
                Header = displayName,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = locale == current,
            };
            item.Click += (_, _) => OnLanguageSelected(capturedLocale);
            items.Add(item);
        }
        _languageMenu.ItemsSource = items;
    }

    /// <summary>Persist the chosen language and re-localize the chrome in place — no restart.</summary>
    private void OnLanguageSelected(string locale)
    {
        if (locale == AppSettings.Current.Language) return;
        AppSettings.Current.Language = locale;
        AppSettings.Current.Save();
        EditorStrings.Load(EditorStrings.LangDir, locale);
        ApplyStrings();
        RefreshLanguageMenu();
    }

    // Position is restored on Opened rather than in the constructor: before the window is shown the
    // platform can still move it, so an earlier assignment would be overwritten.
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        var settings = AppSettings.Current;
        if (!settings.WindowMaximized && settings.WindowX.HasValue && settings.WindowY.HasValue)
            Position = new PixelPoint((int)settings.WindowX.Value, (int)settings.WindowY.Value);
    }

    /// <summary>Close guard. With unsaved work or a live connection the close is canceled and
    /// resumed asynchronously (a Closing handler cannot await), otherwise it proceeds straight
    /// through after saving window state.</summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_skipCloseGuard)
        {
            SaveWindowState();
            return;
        }
        if (DataContext is not MainWindowViewModel vm)
        {
            SaveWindowState();
            return;
        }
        if (!vm.HasAnyDirty && !vm.IsOnline)
        {
            SaveWindowState();
            return;
        }
        e.Cancel = true;
        _ = HandleCloseAsync(vm);
    }

    /// <summary>Persist window geometry plus each editor view's panel layout. Geometry is only
    /// captured while in the Normal state, so a maximized session doesn't overwrite the restore size.</summary>
    private void SaveWindowState()
    {
        this.FindDescendantOfType<MapEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ItemEditorView>()?.SavePanelState();
        this.FindDescendantOfType<NpcEditorView>()?.SavePanelState();
        this.FindDescendantOfType<SpellEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ShopEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ClassEditorView>()?.SavePanelState();
        this.FindDescendantOfType<QuestEditorView>()?.SavePanelState();
        this.FindDescendantOfType<ConversationEditorView>()?.SavePanelState();
        var settings = AppSettings.Current;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
        }
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        settings.Save();
    }

    /// <summary>Finish a close that <see cref="OnWindowClosing"/> canceled: prompt for unsaved work,
    /// disconnect, then close for real. Returns without closing if the author backs out.</summary>
    private async Task HandleCloseAsync(MainWindowViewModel vm)
    {
        if (!await vm.HandleDirtyForCloseAsync()) return;
        await vm.ForceDisconnectAsync();
        _skipCloseGuard = true;
        Close();
    }

    /// <summary>Wire the view-model's dialog and file-picker delegates. Keeping them here is what lets
    /// the view-model stay free of View references while still driving modal dialogs.</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel vm)
        {
            vm.ShowConnectDialogAsync = async dlgVm =>
            {
                var dlg = new ConnectDialog { DataContext = dlgVm };
                dlgVm.CloseRequested += () => dlg.Close();
                await dlg.ShowDialog(this);
            };

            vm.ShowPushChangesDialogAsync = async dlgVm =>
            {
                var dlg = new PushChangesDialog { DataContext = dlgVm };
                dlgVm.ProceedConfirmed += () => dlg.Close();
                dlgVm.Canceled += () => dlg.Close();
                await dlg.ShowDialog(this);
            };

            vm.MapEditor.ConfirmAsync = async msg =>
                await new ConfirmDialog(msg).ShowDialog<bool>(this);

            vm.ShowAlertAsync = async msg =>
                await new ConfirmDialog(msg, alertOnly: true).ShowDialog<bool>(this);

            vm.MapEditor.ShowAlertAsync = vm.ShowAlertAsync;

            vm.MapEditor.SaveFilePngAsync = async suggestedName =>
            {
                var options = new FilePickerSaveOptions
                {
                    Title = EditorStrings.Get(EditorStrings.MapEditor_ExportPngDialogTitle),
                    SuggestedFileName = suggestedName,
                    DefaultExtension = "png",
                    FileTypeChoices = [new FilePickerFileType("PNG Image") { Patterns = ["*.png"] }],
                };
                if (await StorageProvider.TryGetFolderFromPathAsync(EditorPaths.Data) is { } folder)
                    options.SuggestedStartLocation = folder;
                var file = await StorageProvider.SaveFilePickerAsync(options);
                return file?.TryGetLocalPath();
            };

            vm.ShowDisconnectDialogAsync = async dlgVm =>
            {
                var dlg = new DisconnectDialog { DataContext = dlgVm };
                bool decided = false;
                // ConnectDialog must be owned by this dialog, not the main window,
                // because ShowDialog blocks the owner's message loop.
                dlgVm.ShowConnectDialogAsync = async connectVm =>
                {
                    var connectDlg = new ConnectDialog { DataContext = connectVm };
                    connectVm.CloseRequested += () => connectDlg.Close();
                    await connectDlg.ShowDialog(dlg);
                };
                dlgVm.CloseRequested += () => { decided = true; dlg.Close(); };
                // Force a decision — the user cannot dismiss this with the X button.
                dlg.Closing += (_, e) => { if (!decided) e.Cancel = true; };
                await dlg.ShowDialog(this);
            };
        }
    }

    private async void HelpMapControls_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new HelpDialog();
        await dlg.ShowDialog(this);
    }
}
