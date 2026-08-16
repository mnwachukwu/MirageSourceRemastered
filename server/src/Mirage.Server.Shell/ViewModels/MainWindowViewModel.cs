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

    private readonly string _configPath = ServerConfigStore.DefaultPath;
    private readonly string _appSettingsPath = AppSettingsStore.DefaultPath;
    private readonly string _settingsPath = ShellSettings.DefaultPath;

    /// <summary>The server this window is driving. Swapped when the operator changes connection mode;
    /// everything downstream binds to the interface and never asks which kind it got.</summary>
    private IServerConnection _server = null!;

    public MainWindowViewModel()
    {
        var settings = ShellSettings.Load(_settingsPath);
        _isRemote = settings.Mode == ConnectionMode.Remote;
        _remoteHost = settings.RemoteHost;
        _remotePort = settings.RemotePort > 0 ? settings.RemotePort : DefaultManagementPort;
        _remoteToken = settings.RemoteToken;

        AttachConnection(CreateConnection());
        LoadConfig();
        string current = ServerConfigStore.Load(_configPath).Config.Language;
        // Assigned to the backing field, not the property: the setter WRITES the file, and selecting
        // the value that is already in it would rewrite serverconfig.json on every launch.
        _selectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Locale == current)
                            ?? AvailableLanguages.FirstOrDefault(l => l.Locale == "en");
        BuildCommands();
    }

    /// <summary>What the port box starts at when nothing has been configured. One above the game port, so
    /// the two are adjacent and neither is a number anyone has to remember.</summary>
    private const int DefaultManagementPort = Constants.GamePort + 1;

    private IServerConnection CreateConnection() =>
        IsRemote ? new RemoteServerConnection(RemoteHost, _remotePort, RemoteToken) : new ServerProcess();

    private void AttachConnection(IServerConnection connection)
    {
        _server = connection;
        _server.OutputReceived += line => Dispatcher.UIThread.Post(() => AppendLine(line));
        _server.StateChanged += _ => Dispatcher.UIThread.Post(OnStateChanged);
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
    public string SendLabel => ShellStrings.Get(ShellStrings.Action_Send);
    public string SaveLabel => ShellStrings.Get(ShellStrings.Action_Save);
    public string RevertLabel => ShellStrings.Get(ShellStrings.Action_Revert);
    public string GenerateLabel => ShellStrings.Get(ShellStrings.Action_Generate);
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

    /// <summary>The Start/Stop buttons say what they will actually do: a local server is started and
    /// stopped, a remote one is attached to and detached from. Nothing else in the window changes.</summary>
    public string StartLabel => ShellStrings.Get(IsRemote ? ShellStrings.Action_Attach : ShellStrings.Action_Start);
    public string StopLabel => ShellStrings.Get(IsRemote ? ShellStrings.Action_Detach : ShellStrings.Action_Stop);

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRemote)
            AppendLine(ShellStrings.Format(ShellStrings.Console_Attaching,
                ("Host", RemoteHost), ("Port", RemotePort)));

        if (await _server.StartAsync() is not { } failure) return;

        AppendLine(failure switch
        {
            RemoteServerConnection.RemoteError.Rejected => ShellStrings.Get(ShellStrings.Console_Rejected),
            RemoteServerConnection.RemoteError.Unreachable => ShellStrings.Format(
                ShellStrings.Console_Unreachable, ("Host", RemoteHost), ("Port", RemotePort)),
            // A local start reports the path it looked at, which is the message itself.
            _ => ShellStrings.Format(ShellStrings.Console_ServerNotFound, ("Path", failure)),
        });
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        // Only the local server is being asked to save and exit. Detaching costs a remote server nothing,
        // so promising it a 30-second wait would be a lie.
        if (!IsRemote)
            AppendLine(ShellStrings.Format(ShellStrings.Console_StoppingNotice,
                ("Seconds", (int)ServerProcess.ShutdownGrace.TotalSeconds)));
        await _server.StopAsync();
    }

    [RelayCommand]
    private void SendCommand()
    {
        string line = CommandText.Trim();
        if (line.Length == 0) return;
        Send(line);
        CommandText = "";
    }

    /// <summary>The one way a command reaches the server, whether it was typed on the console tab or
    /// composed by a form on the commands tab.</summary>
    private void Send(string line)
    {
        // Shutting a remote server down is a one-way door: nothing here can start it again, and the
        // operator would have to go to the machine. Refused rather than confirmed, because there is no
        // undo to offer. This is a guard on the window, not on the server — anything holding the token
        // can still send it, and a server that must go down has its own console.
        if (!_server.CanSupervise && IsShutdown(line))
        {
            AppendLine(ShellStrings.Get(ShellStrings.Console_ShutdownBlocked));
            return;
        }

        // Echoed locally: stdin is a pipe, so nothing else shows what was typed.
        AppendLine("> " + line);
        _server.SendCommand(line);
    }

    /// <summary>Whether a line is the shutdown command. Matched the way the server matches it — first
    /// token, case-insensitive — so "/SHUTDOWN now" is not a way around the check.</summary>
    public static bool IsShutdown(string line)
    {
        ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
        int space = trimmed.IndexOf(' ');
        if (space >= 0) trimmed = trimmed[..space];
        return trimmed.Equals("/shutdown", StringComparison.OrdinalIgnoreCase);
    }

    private void OnStateChanged()
    {
        // Remote says attached/not attached rather than running/stopped: from over here, a quiet socket
        // means this window lost the server, not that the server went down.
        StateLabel = ShellStrings.Get((_server.State, IsRemote) switch
        {
            (ServerState.Running, true) => ShellStrings.State_Attached,
            (ServerState.Running, false) => ShellStrings.State_Running,
            (ServerState.Stopping, _) => ShellStrings.State_Stopping,
            (_, true) => ShellStrings.State_Detached,
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
        OnPropertyChanged(nameof(CanEditConnection));
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

    // ── Connection ────────────────────────────────────────────────────────────

    public string WindowGroupHeading => ShellStrings.Get(ShellStrings.Config_WindowGroup);
    public string WindowGroupHint => ShellStrings.Get(ShellStrings.Config_WindowGroupHint);
    public string ServerGroupHeading => ShellStrings.Get(ShellStrings.Config_ServerGroup);
    public string ServerGroupHint => ShellStrings.Get(ShellStrings.Config_ServerGroupHint);

    public string ConnectionHeading => ShellStrings.Get(ShellStrings.Connection_Heading);
    public string ConnectionBlurb => ShellStrings.Get(ShellStrings.Connection_Blurb);
    public string LocalLabel => ShellStrings.Get(ShellStrings.Connection_Local);
    public string LocalHint => ShellStrings.Get(ShellStrings.Connection_LocalHint);
    public string RemoteLabel => ShellStrings.Get(ShellStrings.Connection_Remote);
    public string RemoteHint => ShellStrings.Get(ShellStrings.Connection_RemoteHint);
    public string HostLabel => ShellStrings.Get(ShellStrings.Connection_Host);
    public string PortLabel => ShellStrings.Get(ShellStrings.Connection_Port);
    public string TokenLabel => ShellStrings.Get(ShellStrings.Connection_Token);
    public string RemoteTokenHint => ShellStrings.Get(ShellStrings.Connection_TokenHint);
    public string RevealLabel => ShellStrings.Get(ShellStrings.Connection_Reveal);

    private bool _isRemote;
    private string _remoteHost = "";
    private int _remotePort;
    private string _remoteToken = "";

    /// <summary>Where the target can be changed at all. Swapping the connection under a live session
    /// would leave a server running with nothing watching it, so the fields lock while attached.</summary>
    public bool CanEditConnection => _server.State == ServerState.Stopped;

    public bool IsRemote
    {
        get => _isRemote;
        set
        {
            if (_isRemote == value) return;
            _isRemote = value;
            SwapConnection();
        }
    }

    /// <summary>Local is the inverse of remote, as a bindable of its own — a two-way RadioButton needs a
    /// property per option.</summary>
    public bool IsLocal
    {
        get => !_isRemote;
        set { if (value) IsRemote = false; }
    }

    public string RemoteHost
    {
        get => _remoteHost;
        set { if (SetProperty(ref _remoteHost, value)) SaveSettings(); }
    }

    public decimal RemotePort
    {
        get => _remotePort;
        set
        {
            int port = (int)value;
            if (_remotePort == port) return;
            _remotePort = port;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public string RemoteToken
    {
        get => _remoteToken;
        set { if (SetProperty(ref _remoteToken, value)) SaveSettings(); }
    }

    /// <summary>Whether the token box shows its contents. Off by default: a server console is the sort of
    /// thing that gets screen-shared.</summary>
    [ObservableProperty]
    public partial bool RevealTokens { get; set; }

    // Tears down the old connection and builds one for the new mode. Only reachable while stopped, so
    // nothing is being dropped mid-session.
    private void SwapConnection()
    {
        _server.Dispose();
        AttachConnection(CreateConnection());
        SaveSettings();
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(CanEditManagement));
        OnStateChanged();
    }

    private void SaveSettings() => new ShellSettings
    {
        Mode = _isRemote ? ConnectionMode.Remote : ConnectionMode.Local,
        RemoteHost = _remoteHost,
        RemotePort = _remotePort,
        RemoteToken = _remoteToken,
    }.Save(_settingsPath);

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

    // ── Logging ───────────────────────────────────────────────────────────────
    // The other config file. appsettings.json is hand-authored structure; only these five values are
    // edited here, and each is greyed out rather than guessed at when the file does not expose it.

    public string LoggingHeading => ShellStrings.Get(ShellStrings.Logging_Heading);
    public string LoggingBlurb => ShellStrings.Get(ShellStrings.Logging_Blurb);
    public string LogLevelLabel => ShellStrings.Get(ShellStrings.Logging_Level);
    public string LogLevelHint => ShellStrings.Get(ShellStrings.Logging_LevelHint);
    public string OutgoingPacketsLabel => ShellStrings.Get(ShellStrings.Logging_OutgoingPackets);
    public string IncomingPacketsLabel => ShellStrings.Get(ShellStrings.Logging_IncomingPackets);
    public string PacketsHint => ShellStrings.Get(ShellStrings.Logging_PacketsHint);
    public string ServerRetentionLabel => ShellStrings.Get(ShellStrings.Logging_ServerRetention);
    public string NetworkRetentionLabel => ShellStrings.Get(ShellStrings.Logging_NetworkRetention);
    public string RetentionHint => ShellStrings.Get(ShellStrings.Logging_RetentionHint);
    public string LoggingUnavailableNotice => ShellStrings.Get(ShellStrings.Logging_Unavailable);

    /// <summary>The levels an operator picks between, straight from the shared list so the picker cannot
    /// offer one Serilog would not take.</summary>
    public IReadOnlyList<string> LogLevels { get; } = LogSettings.Levels;

    private LogKnobs _logKnobs = LogKnobs.All;

    [ObservableProperty]
    public partial string LogLevel { get; set; } = "Information";

    [ObservableProperty]
    public partial bool LogOutgoingPackets { get; set; }

    [ObservableProperty]
    public partial bool LogIncomingPackets { get; set; }

    [ObservableProperty]
    public partial decimal ServerRetentionDays { get; set; } = 7;

    [ObservableProperty]
    public partial decimal NetworkRetentionDays { get; set; } = 3;

    public bool CanEditLogLevel => _logKnobs.HasFlag(LogKnobs.MinimumLevel);
    public bool CanEditOutgoingPackets => _logKnobs.HasFlag(LogKnobs.OutgoingPackets);
    public bool CanEditIncomingPackets => _logKnobs.HasFlag(LogKnobs.IncomingPackets);
    public bool CanEditServerRetention => _logKnobs.HasFlag(LogKnobs.ServerRetention);
    public bool CanEditNetworkRetention => _logKnobs.HasFlag(LogKnobs.NetworkRetention);

    /// <summary>True when anything at all failed to resolve, which is what shows the explanation.</summary>
    public bool LoggingIncomplete => _logKnobs != LogKnobs.All;

    private void LoadLogSettings()
    {
        var (log, _) = AppSettingsStore.Load(_appSettingsPath);
        _logKnobs = log.Available;
        LogLevel = log.MinimumLevel;
        LogOutgoingPackets = log.LogOutgoingPackets;
        LogIncomingPackets = log.LogIncomingPackets;
        ServerRetentionDays = log.ServerLogRetentionDays;
        NetworkRetentionDays = log.NetworkLogRetentionDays;

        OnPropertyChanged(nameof(CanEditLogLevel));
        OnPropertyChanged(nameof(CanEditOutgoingPackets));
        OnPropertyChanged(nameof(CanEditIncomingPackets));
        OnPropertyChanged(nameof(CanEditServerRetention));
        OnPropertyChanged(nameof(CanEditNetworkRetention));
        OnPropertyChanged(nameof(LoggingIncomplete));
    }

    // ── Remote management, as the server's own setting ────────────────────────

    public string ManagementHeading => ShellStrings.Get(ShellStrings.Management_Heading);
    public string ManagementBlurb => ShellStrings.Get(ShellStrings.Management_Blurb);
    public string ManagementEnableLabel => ShellStrings.Get(ShellStrings.Management_Enable);
    public string ManagementEnableHint => ShellStrings.Get(ShellStrings.Management_EnableHint);
    public string ManagementPortLabel => ShellStrings.Get(ShellStrings.Management_Port);
    public string ManagementTokenLabel => ShellStrings.Get(ShellStrings.Management_Token);
    public string ManagementTokenHint => ShellStrings.Get(ShellStrings.Management_TokenHint);
    public string ManagementLocalOnlyNotice => ShellStrings.Get(ShellStrings.Management_LocalOnly);

    /// <summary>These describe the server whose serverconfig.json is beside this shell. Attached to a
    /// remote server, that file is not the one being run, so the section reads as unavailable rather than
    /// quietly editing the wrong machine's settings.</summary>
    public bool CanEditManagement => !IsRemote;

    [ObservableProperty]
    public partial bool ManagementEnabled { get; set; }

    [ObservableProperty]
    public partial decimal ManagementPort { get; set; } = DefaultManagementPort;

    [ObservableProperty]
    public partial string ManagementToken { get; set; } = "";

    /// <summary>Mints a token. 32 random bytes in URL-safe base64: long enough that the failure limit on
    /// the listener is belt-and-braces, and short enough to paste into a chat window.</summary>
    [RelayCommand]
    private void GenerateToken() =>
        ManagementToken = System.Security.Cryptography.RandomNumberGenerator.GetHexString(48, lowercase: true);

    [RelayCommand]
    private void SaveConfig()
    {
        // Amended with `with`, not built fresh: this form owns some of the file, not all of it. A new
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
            // Unticking writes port 0 rather than clearing the token, so turning remote access back on
            // does not mean redistributing a new secret to everyone who had the old one.
            Management = new ManagementConfig
            {
                Port = ManagementEnabled ? (int)ManagementPort : 0,
                Token = ManagementToken,
            },
        };
        if (ServerConfigStore.Save(_configPath, config) is { } error)
        {
            ConfigStatus = ShellStrings.Format(ShellStrings.Config_SaveFailed, ("Error", error));
            return;
        }

        // The log settings live in the other file, but Save is one button: an operator changing two things
        // on one form and having half of it persist would be the surprise.
        var log = new LogSettings
        {
            MinimumLevel = LogLevel,
            LogOutgoingPackets = LogOutgoingPackets,
            LogIncomingPackets = LogIncomingPackets,
            ServerLogRetentionDays = (int)ServerRetentionDays,
            NetworkLogRetentionDays = (int)NetworkRetentionDays,
            Available = _logKnobs,
        };
        ConfigStatus = AppSettingsStore.Save(_appSettingsPath, log) is { } logError
            ? ShellStrings.Format(ShellStrings.Config_SaveFailed, ("Error", logError))
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
        ManagementEnabled = config.Management.IsEnabled;
        ManagementPort = config.Management.Port > 0 ? config.Management.Port : DefaultManagementPort;
        ManagementToken = config.Management.Token;
        LoadLogSettings();
        // On a malformed file the switches show stock rules, which is what the server would run — the
        // message is what separates that from "these are your settings".
        ConfigStatus = error is null ? "" : ShellStrings.Format(ShellStrings.Config_LoadFailed, ("Error", error));
    }

    public void Dispose() => _server.Dispose();
}
