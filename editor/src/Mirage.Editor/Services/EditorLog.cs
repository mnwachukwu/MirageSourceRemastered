using Avalonia.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace Mirage.Editor.Services;

/// <summary>
/// The editor's file log: one daily-rolling file under <see cref="EditorPaths.Logs"/>, written through a
/// level switch so the capture level changes from Help &gt; Logging without a restart.
///
/// <para>Every call site goes through the static helpers here rather than holding a logger, because the
/// editor is a view-model app with no container to inject one from. <see cref="Write"/> is the only place
/// that touches Serilog, so a call made before <see cref="Initialize"/> is a no-op instead of a crash.</para>
/// </summary>
public static class EditorLog
{
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static Logger? _logger;
    private static DispatcherTimer? _stallWatchdog;
    private static long _lastTickMs;

    /// <summary>How late a watchdog tick has to be before it counts as a stall. The timer asks for one
    /// second; anything past this means the UI thread was busy or blocked in between.</summary>
    private const long StallThresholdMs = 1_500;
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    /// <summary>Where the log files are written. Shown in the configuration window.</summary>
    public static string Directory => EditorPaths.Logs;

    /// <summary>Opens the sink and records the session header. Safe to call once, at startup.</summary>
    public static void Initialize()
    {
        Reconfigure(AppSettings.Current.Logging);
        WriteSessionHeader();
    }

    /// <summary>Applies a level and retention. The level rides a switch and takes effect immediately; a
    /// retention change needs a new sink, so the file is closed and reopened.</summary>
    public static void Reconfigure(LoggingSetting setting)
    {
        LevelSwitch.MinimumLevel = ToSerilog(setting.Level);

        var previous = _logger;
        try
        {
            System.IO.Directory.CreateDirectory(EditorPaths.Logs);
            _logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                .WriteTo.File(
                    Path.Combine(EditorPaths.Logs, "editor-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: setting.RetainedFileCount,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch
        {
            // A log that cannot open must not stop the editor opening. The app runs unlogged.
            _logger = null;
        }
        previous?.Dispose();
    }

    /// <summary>Closes the sink, flushing anything buffered. Called on shutdown.</summary>
    public static void Shutdown(string reason)
    {
        Info("Session ending: {Reason}", reason);
        _stallWatchdog?.Stop();
        _stallWatchdog = null;
        _logger?.Dispose();
        _logger = null;
    }

    // ── Level-tagged helpers ──────────────────────────────────────────────────

    public static void Verbose(string template, params object?[] args) => Write(LogEventLevel.Verbose, null, template, args);
    public static void Debug(string template, params object?[] args) => Write(LogEventLevel.Debug, null, template, args);
    public static void Info(string template, params object?[] args) => Write(LogEventLevel.Information, null, template, args);
    public static void Warn(string template, params object?[] args) => Write(LogEventLevel.Warning, null, template, args);
    public static void Warn(Exception ex, string template, params object?[] args) => Write(LogEventLevel.Warning, ex, template, args);
    public static void Error(string template, params object?[] args) => Write(LogEventLevel.Error, null, template, args);
    public static void Error(Exception ex, string template, params object?[] args) => Write(LogEventLevel.Error, ex, template, args);

    /// <summary>Whether a level would be written. Guards call sites that would otherwise pay to build an
    /// argument the sink is going to drop — the per-tile and per-packet ones.</summary>
    public static bool IsEnabled(LogEventLevel level) => _logger is not null && LevelSwitch.MinimumLevel <= level;

    private static void Write(LogEventLevel level, Exception? ex, string template, object?[] args)
    {
        var log = _logger;
        if (log is null) return;
        try { log.Write(level, ex, template, args); }
        catch { /* a logger that throws must never take the editor with it */ }
    }

    // ── UI-thread stall watchdog ──────────────────────────────────────────────

    /// <summary>Starts the one-second heartbeat that reports a UI thread which stopped answering. A tick
    /// that arrives late by more than <see cref="StallThresholdMs"/> means nothing else ran in between:
    /// the gap is the freeze, and its size is how long the window was unresponsive.
    /// <para>Timed on the dispatcher deliberately — a background timer would prove nothing about the
    /// thread that draws and handles input.</para></summary>
    public static void StartStallWatchdog()
    {
        if (_stallWatchdog is not null) return;
        _lastTickMs = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
        _stallWatchdog = new DispatcherTimer(DispatcherPriority.Background) { Interval = WatchdogInterval };
        _stallWatchdog.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
            long gap = now - _lastTickMs;
            _lastTickMs = now;
            if (gap >= StallThresholdMs)
                Warn("UI thread stalled for {Gap} ms (heartbeat expected every {Interval} ms).",
                     gap, (long)WatchdogInterval.TotalMilliseconds);
            else
                Verbose("Heartbeat: {Gap} ms.", gap);
        };
        _stallWatchdog.Start();
        Info("UI-thread stall watchdog started (threshold {Threshold} ms).", StallThresholdMs);
    }

    // ── Session header ────────────────────────────────────────────────────────

    private static void WriteSessionHeader()
    {
        var setting = AppSettings.Current.Logging;
        Info("─────────────────────────────────────────────────────────────");
        Info("Mirage editor starting. Version {Version}, {Runtime} on {OS} ({Arch}).",
             typeof(EditorLog).Assembly.GetName().Version?.ToString() ?? "unknown",
             Environment.Version.ToString(),
             System.Runtime.InteropServices.RuntimeInformation.OSDescription,
             System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        Info("Capture level {Level}, retention {Retention}.", setting.Level, setting.Retention);
        Info("Config dir  {Path}", EditorPaths.Config);
        Info("Data dir    {Path}", EditorPaths.Data);
        Info("Assets dir  {Path}", EditorPaths.Assets);
        Info("Log dir     {Path}", EditorPaths.Logs);
        Info("Language {Language}.", AppSettings.Current.Language);
    }

    private static LogEventLevel ToSerilog(LogCaptureLevel level) => level switch
    {
        LogCaptureLevel.Error => LogEventLevel.Error,
        LogCaptureLevel.Warning => LogEventLevel.Warning,
        LogCaptureLevel.Debug => LogEventLevel.Debug,
        LogCaptureLevel.Verbose => LogEventLevel.Verbose,
        _ => LogEventLevel.Information,
    };
}
