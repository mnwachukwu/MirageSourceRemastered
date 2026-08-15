using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Shell.Localization;
using Mirage.Server.Shell.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Shell.ViewModels;

/// <summary>
/// Supervise the server, relay its console, run its commands, and edit the rules it runs on.
///
/// <para>The rules get an explicit Save/Revert rather than writing on every toggle: they change what a
/// death costs every player, and a restart is what applies them.</para>
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Lines the console keeps. The real log is on disk with its own retention.</summary>
    private const int MaxConsoleLines = 5_000;

    // One string, not a list of lines: it is bound to a read-only TextBox so a selection can sweep
    // ACROSS lines, which is what copying a stack trace needs.
    private readonly System.Text.StringBuilder _log = new();
    private int _logLines;

    private readonly ServerProcess _server = new();
    private readonly string _configPath = ServerConfigStore.DefaultPath;

    public MainWindowViewModel()
    {
        _server.OutputReceived += line => Dispatcher.UIThread.Post(() => AppendLine(line));
        _server.StateChanged += _ => Dispatcher.UIThread.Post(OnStateChanged);
        LoadConfig();
        string current = ServerConfigStore.Load(_configPath).Config.Language;
        // Assigned to the backing field, not the property: the setter WRITES the file, and selecting
        // the value that is already in it would rewrite appsettings.json on every launch.
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Locale == current)
                            ?? AvailableLanguages.FirstOrDefault(l => l.Locale == "en");
        BuildCommands();
    }

    // ── Chrome ────────────────────────────────────────────────────────────────

    public string Title => ShellStrings.Format(ShellStrings.Window_Title, ("GameName", Constants.GameName));
    public string ConsoleTabHeader => ShellStrings.Get(ShellStrings.Tab_Console);
    public string ConfigurationTabHeader => ShellStrings.Get(ShellStrings.Tab_Configuration);
    public string CommandsTabHeader => ShellStrings.Get(ShellStrings.Tab_Commands);
    public string CommandsBlurb => ShellStrings.Get(ShellStrings.Commands_Blurb);
    public string RunLabel => ShellStrings.Get(ShellStrings.Commands_Run);
    public string ConfirmLabel => ShellStrings.Get(ShellStrings.Commands_Confirm);
    public string CancelLabel => ShellStrings.Get(ShellStrings.Commands_Cancel);
    public string StartLabel => ShellStrings.Get(ShellStrings.Action_Start);
    public string StopLabel => ShellStrings.Get(ShellStrings.Action_Stop);
    public string SendLabel => ShellStrings.Get(ShellStrings.Action_Send);
    public string SaveLabel => ShellStrings.Get(ShellStrings.Action_Save);
    public string RevertLabel => ShellStrings.Get(ShellStrings.Action_Revert);
    public string CommandHint => ShellStrings.Get(ShellStrings.Console_CommandHint);

    public string DeathPenaltyHeading => ShellStrings.Get(ShellStrings.Config_DeathPenaltyHeading);
    public string DeathPenaltyBlurb => ShellStrings.Get(ShellStrings.Config_DeathPenaltyBlurb);
    public string DurabilityLossLabel => ShellStrings.Get(ShellStrings.Config_DurabilityLoss);
    public string DurabilityLossHint => ShellStrings.Get(ShellStrings.Config_DurabilityLossHint);
    public string ItemDropLabel => ShellStrings.Get(ShellStrings.Config_ItemDrop);
    public string ItemDropHint => ShellStrings.Get(ShellStrings.Config_ItemDropHint);
    public string ExpLossLabel => ShellStrings.Get(ShellStrings.Config_ExpLoss);
    public string ExpLossHint => ShellStrings.Get(ShellStrings.Config_ExpLossHint);
    public string RestartRequiredNotice => ShellStrings.Get(ShellStrings.Config_RestartRequired);
    public string LanguageHeading => ShellStrings.Get(ShellStrings.Config_LanguageHeading);
    public string LanguageBlurb => ShellStrings.Get(ShellStrings.Config_LanguageBlurb);
    public string LanguageServerNote => ShellStrings.Get(ShellStrings.Config_LanguageServerNote);

    // ── Server supervision ────────────────────────────────────────────────────

    /// <summary>Everything the server has printed, as one selectable block.</summary>
    public string ConsoleText => _log.ToString();

    [ObservableProperty]
    public partial string StateLabel { get; private set; } = ShellStrings.Get(ShellStrings.State_Stopped);

    // Colour is on top of the label, never instead of it — the word is what works for a colourblind
    // reader and in a screenshot.
    private static readonly IImmutableSolidColorBrush RunningBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly IImmutableSolidColorBrush StoppingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly IImmutableSolidColorBrush StoppedBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));

    [ObservableProperty]
    public partial IImmutableSolidColorBrush StateBrush { get; private set; } = StoppedBrush;

    [ObservableProperty]
    public partial string CommandText { get; set; } = "";

    public bool CanStart => _server.State == ServerState.Stopped;
    public bool CanStop => _server.State == ServerState.Running;

    [RelayCommand]
    private void Start()
    {
        // Start returns the path it looked at when nothing was there.
        if (_server.Start() is { } missingPath)
            AppendLine(ShellStrings.Format(ShellStrings.Console_ServerNotFound, ("Path", missingPath)));
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        AppendLine(ShellStrings.Format(ShellStrings.Console_StoppingNotice,
            ("Seconds", (int)ServerProcess.ShutdownGrace.TotalSeconds)));
        await _server.StopAsync();
    }

    [RelayCommand]
    private void SendCommand()
    {
        string line = CommandText.Trim();
        if (line.Length == 0) return;
        // Echoed locally: stdin is a pipe, so nothing else shows what was typed.
        AppendLine("> " + line);
        _server.SendCommand(line);
        CommandText = "";
    }

    private void OnStateChanged()
    {
        StateLabel = ShellStrings.Get(_server.State switch
        {
            ServerState.Running => ShellStrings.State_Running,
            ServerState.Stopping => ShellStrings.State_Stopping,
            _ => ShellStrings.State_Stopped,
        });
        StateBrush = _server.State switch
        {
            ServerState.Running => RunningBrush,
            ServerState.Stopping => StoppingBrush,
            _ => StoppedBrush,
        };
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    private void AppendLine(string line)
    {
        _log.Append(line).Append('\n');
        _logLines++;
        // Whole lines off the front, not a character count.
        while (_logLines > MaxConsoleLines)
        {
            int firstBreak = -1;
            for (int i = 0; i < _log.Length; i++)
                if (_log[i] == '\n') { firstBreak = i; break; }
            if (firstBreak < 0) break;
            _log.Remove(0, firstBreak + 1);
            _logLines--;
        }
        OnPropertyChanged(nameof(ConsoleText));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IReadOnlyList<CommandGroup> CommandGroups { get; private set; } = [];

    /// <summary>Builds the command forms. Picker options come from the real enums, so the UI cannot
    /// offer a value the server would refuse.</summary>
    private void BuildCommands()
    {
        void Send(string line)
        {
            AppendLine("> " + line);
            _server.SendCommand(line);
        }

        static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();

        CommandGroups =
        [
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Players),
            [
                new ShellCommand("/who", ShellStrings.Get(ShellStrings.Commands_Who), Send),
                // The console's own bounds: 1 to 1440 minutes, defaulting to its 60.
                new ShellCommand("/kick", ShellStrings.Get(ShellStrings.Commands_Kick), Send,
                    CommandParameter.Text("name"), CommandParameter.Number("minutes", 1, 1440, 60)),
                new ShellCommand("/mute", ShellStrings.Get(ShellStrings.Commands_Mute), Send,
                    CommandParameter.Text("name"), CommandParameter.Number("minutes", 1, 1440, 60)),
                new ShellCommand("/ban", ShellStrings.Get(ShellStrings.Commands_Ban), Send,
                    CommandParameter.Text("name")) { NeedsConfirmation = true },
                new ShellCommand("/setaccess", ShellStrings.Get(ShellStrings.Commands_SetAccess), Send,
                    CommandParameter.Choice("level", Names<AdminLevel>()),
                    CommandParameter.Text("name")) { NeedsConfirmation = true },
                new ShellCommand("/refreshbanlist", ShellStrings.Get(ShellStrings.Commands_RefreshBanList), Send),
            ]),
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_World),
            [
                new ShellCommand("/tod", ShellStrings.Get(ShellStrings.Commands_Tod), Send,
                    CommandParameter.Choice("phase", Names<TimePhase>())),
                new ShellCommand("/weather", ShellStrings.Get(ShellStrings.Commands_Weather), Send,
                    CommandParameter.Choice("type", Names<WeatherType>())),
                new ShellCommand("/motd", ShellStrings.Get(ShellStrings.Commands_Motd), Send,
                    CommandParameter.Paragraph("text")),
                // Maps are numeric ids, so the box is one — bounded to what the server would accept.
                new ShellCommand("/respawn", ShellStrings.Get(ShellStrings.Commands_Respawn), Send,
                    CommandParameter.Number("map", 1, Constants.MaxMaps, 1)),
                new ShellCommand("/mapreport", ShellStrings.Get(ShellStrings.Commands_MapReport), Send),
            ]),
            new CommandGroup(ShellStrings.Get(ShellStrings.Commands_Guilds),
            [
                new ShellCommand("/startwar", ShellStrings.Get(ShellStrings.Commands_StartWar), Send),
                new ShellCommand("/advancewar", ShellStrings.Get(ShellStrings.Commands_AdvanceWar), Send),
                new ShellCommand("/endwar", ShellStrings.Get(ShellStrings.Commands_EndWar), Send),
                new ShellCommand("/guildreset", ShellStrings.Get(ShellStrings.Commands_GuildReset), Send,
                    CommandParameter.Choice("scope", Names<SettlementScope>())),
            ]),
        ];
        OnPropertyChanged(nameof(CommandGroups));
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial bool DurabilityLoss { get; set; } = true;

    [ObservableProperty]
    public partial bool ItemDrop { get; set; } = true;

    [ObservableProperty]
    public partial bool ExpLoss { get; set; } = true;

    /// <summary>The last thing that happened to the config file — saved, or why not. Empty until
    /// something has.</summary>
    [ObservableProperty]
    public partial string ConfigStatus { get; private set; } = "";

    [RelayCommand]
    private void SaveConfig()
    {
        // Amended with `with`, not built fresh: this form owns three switches, not the whole file. A new
        // ServerConfig would reset the port and language every time someone pressed Save.
        var (existing, _) = ServerConfigStore.Load(_configPath);
        var config = existing with
        {
            DeathPenalty = new DeathPenaltyConfig
            {
                DurabilityLoss = DurabilityLoss,
                ItemDrop = ItemDrop,
                ExpLoss = ExpLoss,
            },
        };
        ConfigStatus = ServerConfigStore.Save(_configPath, config) is { } error
            ? ShellStrings.Format(ShellStrings.Config_SaveFailed, ("Error", error))
            : ShellStrings.Format(ShellStrings.Config_Saved, ("Path", _configPath));
    }

    [RelayCommand]
    private void RevertConfig() => LoadConfig();

    // ── Language ──────────────────────────────────────────────────────────────

    /// <summary>The locales with a shell translation on disk, by their own name.</summary>
    public IReadOnlyList<LanguageChoice> AvailableLanguages { get; } =
        ShellStrings.GetAvailableLanguages(ShellLangDir)
            .Select(l => new LanguageChoice(l.Locale, l.DisplayName))
            .ToList();

    private LanguageChoice? _selectedLanguage;

    /// <summary>Applied on selection, unlike the rules: a display preference that needed confirming
    /// would read as broken.</summary>
    public LanguageChoice? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || value.Locale == _selectedLanguage?.Locale) return;
            _selectedLanguage = value;
            OnPropertyChanged();
            ApplyLanguage(value.Locale);
        }
    }

    private void ApplyLanguage(string locale)
    {
        // Carries the current values through so a language change cannot revert another setting.
        var (existing, _) = ServerConfigStore.Load(_configPath);
        string? error = ServerConfigStore.Save(_configPath, existing with { Language = locale });
        if (error is not null)
        {
            ConfigStatus = error;
            return;
        }
        ShellStrings.Load(ShellLangDir, locale);
        BuildCommands();
        // Null name = everything changed, which a language swap is. Listing the properties instead would
        // go stale the next time a string is added.
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(null));
        OnStateChanged();   // the state word came from the table too
        ConfigStatus = "";
    }

    /// <summary>lang/shell/, NOT lang/ — the server's own table lives there. See the csproj note.</summary>
    private static string ShellLangDir => Path.Combine(AppContext.BaseDirectory, "lang", "shell");

    /// <summary>One row in the language picker. A named type rather than a tuple so the ComboBox can
    /// bind a DisplayMemberBinding to it under compiled bindings.</summary>
    public sealed record LanguageChoice(string Locale, string DisplayName);

    private void LoadConfig()
    {
        var (config, error) = ServerConfigStore.Load(_configPath);
        DurabilityLoss = config.DeathPenalty.DurabilityLoss;
        ItemDrop = config.DeathPenalty.ItemDrop;
        ExpLoss = config.DeathPenalty.ExpLoss;
        // On a malformed file the switches show stock rules, which is what the server would run — the
        // message is what separates that from "these are your settings".
        ConfigStatus = error is null ? "" : ShellStrings.Format(ShellStrings.Config_LoadFailed, ("Error", error));
    }

    public void Dispose() => _server.Dispose();
}
