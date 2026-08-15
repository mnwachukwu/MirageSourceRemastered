using System.Diagnostics;
using Mirage.Shared;

namespace Mirage.Server.Shell.Services;

/// <summary>"Running" means the process is alive, NOT that the world has finished loading — the shell
/// has no way to know that until the management protocol exists.</summary>
public enum ServerState { Stopped, Running, Stopping }

/// <summary>
/// Supervises the server as a child process: starts it, relays its console output, forwards commands to
/// its stdin, and shuts it down.
///
/// <para>Stopping is a request, not a kill. The server drains four write queues on the way out, and
/// killing it mid-drain loses whatever had not landed — so Stop writes <c>/shutdown</c> and waits, and
/// kills only as a backstop.</para>
///
/// <para>State and output arrive as EVENTS rather than being read off the process, so a remote
/// connection can drop in behind the same interface (#75).</para>
/// </summary>
public sealed class ServerProcess : IDisposable
{
    /// <summary>How long a graceful shutdown is given before the process is killed. Generous on
    /// purpose — the alternative to waiting is losing writes.</summary>
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(30);

    private readonly object _lock = new();
    private Process? _process;

    /// <summary>One line of server console output. Raised from a background thread — a UI subscriber
    /// has to marshal.</summary>
    public event Action<string>? OutputReceived;

    /// <summary>Raised on every state transition, from whichever thread caused it.</summary>
    public event Action<ServerState>? StateChanged;

    public ServerState State { get; private set; } = ServerState.Stopped;

    /// <summary>The server executable, beside this one. They ship in the same package, so the shell
    /// never has to be told where the server is. Built from <see cref="Constants.GameName"/> rather than
    /// hardcoded, because that is the same single value the build derives the assembly name from.</summary>
    public static string DefaultExecutablePath =>
        Path.Combine(AppContext.BaseDirectory,
            Constants.GameName.Replace(' ', '-') + "-Server" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public string ExecutablePath { get; set; } = DefaultExecutablePath;

    /// <summary>Launches the server. Returns null on success, or a message explaining why not —
    /// a missing executable is by far the likeliest, and it is worth naming the path it looked at.</summary>
    public string? Start()
    {
        lock (_lock)
        {
            if (State != ServerState.Stopped) return null;
            if (!File.Exists(ExecutablePath)) return ExecutablePath;

            var info = new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                // The server pins its own working directory to its exe folder on startup, but setting it
                // here too means the data and log paths resolve identically whether it was launched from
                // the shell, a terminal, or a service manager.
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
            };

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
            process.Exited += (_, _) => SetState(ServerState.Stopped);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }
        SetState(ServerState.Running);
        return null;
    }

    /// <summary>Types a line at the server's console. Everything <c>ConsoleCommands</c> understands —
    /// /who, /kick, /ban, /mute, /refreshbanlist, /help — arrives this way, so the shell inherits the
    /// whole command set without reimplementing any of it.</summary>
    public void SendCommand(string line)
    {
        lock (_lock)
        {
            if (_process is not { HasExited: false }) return;
            try { _process.StandardInput.WriteLine(line); _process.StandardInput.Flush(); }
            catch (IOException) { /* the pipe closed under us; Exited will follow */ }
        }
    }

    /// <summary>Asks the server to shut down and waits for it to finish draining. Kills it only if it
    /// outstays <see cref="ShutdownGrace"/>.</summary>
    public async Task StopAsync()
    {
        Process? process;
        lock (_lock)
        {
            if (_process is not { HasExited: false }) return;
            process = _process;
        }
        SetState(ServerState.Stopping);

        SendCommand("/shutdown");
        using var timeout = new CancellationTokenSource(ShutdownGrace);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Out of patience. Anything still queued is lost, which is why the grace above is long.
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        SetState(ServerState.Stopped);
    }

    private void SetState(ServerState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    public void Dispose()
    {
        // A shell that closes must not orphan a running server with no console attached to it — there
        // would be no way left to shut it down gracefully. Ask, wait, and only then let go.
        try { StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        lock (_lock)
        {
            _process?.Dispose();
            _process = null;
        }
    }
}
