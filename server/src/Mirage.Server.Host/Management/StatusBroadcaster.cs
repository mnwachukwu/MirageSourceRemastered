using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Host.Management;

/// <summary>
/// Assembles <see cref="ServerStatus"/> and hands it to whoever asked for it.
///
/// <para>OFF unless something is listening. The stream it writes to is also a human console, so a
/// terminal must never see a sentinel line: the local shell opts in with a command-line flag on the
/// child it spawned, and a remote operator opts in over its own socket. With neither, this emits
/// nothing at all.</para>
/// </summary>
public sealed class StatusBroadcaster : IHostedService, IDisposable
{
    /// <summary>The backstop cadence. Changes push immediately; this only exists so a missed event
    /// heals itself rather than leaving a dashboard quietly wrong.</summary>
    public static readonly TimeSpan Backstop = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        // The default encoder escapes apostrophes, which a MOTD is full of. It round-trips either way,
        // but this line is read by a human debugging the wire often enough to be worth keeping legible.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly GameWorld _world;
    private readonly PlayerManager _pm;
    private readonly GameLoop _gameLoop;
    private readonly int _port;
    private readonly long _startedAtMs = Environment.TickCount64;

    private Timer? _timer;
    private CancellationTokenSource? _cts;

    public StatusBroadcaster(GameWorld world, PlayerManager pm, GameLoop gameLoop, ServerConfig config)
    {
        _world = world;
        _pm = pm;
        _gameLoop = gameLoop;
        _port = config.Port;
    }

    /// <summary>How many operators are attached. Set by <see cref="ManagementListener"/>, which is the
    /// only thing that knows; kept here so the snapshot has one assembly point.</summary>
    public int OperatorCount { get; set; }

    /// <summary>Set by <c>Program</c> when the child was started with the status flag: the local shell
    /// is the only reader of that stdout, so a sentinel there is private to it.</summary>
    public bool WriteToStdout { get; set; }

    /// <summary>Raised with a serialized snapshot for consumers that are not stdout — the management
    /// sessions that asked for status. Empty when nobody has.</summary>
    public event Action<string>? SnapshotReady;

    /// <summary>True when anything at all is listening. Nothing is assembled when nothing is.</summary>
    public bool HasConsumers => WriteToStdout || SnapshotReady is not null;

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Push on the change itself, so a roster is right the moment somebody joins. The timer is only
        // the backstop that heals a missed one.
        _pm.RosterChanged += Publish;
        _timer = new Timer(_ => Publish(), null, Backstop, Backstop);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _pm.RosterChanged -= Publish;
        _cts?.Cancel();
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    /// <summary>Asks for a snapshot. Safe to call from anywhere: the assembly itself is posted to the
    /// game thread, which is the only place this state is consistent.</summary>
    public void Publish()
    {
        if (!HasConsumers || _cts is null || _cts.IsCancellationRequested) return;
        _gameLoop.Post(() =>
        {
            string line = ServerStatus.LinePrefix + JsonSerializer.Serialize(Snapshot(), Json);
            if (WriteToStdout) System.Console.WriteLine(line);
            SnapshotReady?.Invoke(line);
        });
    }

    // Runs ON the game thread. Reads only; nothing here may mutate world state.
    private ServerStatus Snapshot()
    {
        var players = new List<PlayerSummary>();
        for (int i = 1; i <= Constants.MaxPlayers; i++)
        {
            var sp = _pm[i];
            if (!sp.IsPlaying) continue;
            var c = sp.Char;
            players.Add(new PlayerSummary
            {
                Slot = i,
                Name = c.Name.Trim(),
                Login = sp.Login,
                Level = c.Level,
                Class = ClassName(c.Class),
                Map = c.Map,
                Access = c.Access.ToString(),
            });
        }

        return new ServerStatus
        {
            TimePhase = _world.TimePhase.ToString(),
            Weather = _world.Weather.ToString(),
            Motd = _world.Motd,
            UptimeSeconds = (Environment.TickCount64 - _startedAtMs) / 1000,
            Port = _port,
            Operators = OperatorCount,
            Players = players,
        };
    }

    private string ClassName(int index) =>
        index >= 0 && index < _world.Classes.Length ? _world.Classes[index].Name.Trim() : "";

    public void Dispose()
    {
        _timer?.Dispose();
        _cts?.Dispose();
    }
}
