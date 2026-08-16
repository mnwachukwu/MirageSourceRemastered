using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mirage.Server.Core.Configuration;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Logging;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Server.Host.Logging;
using Mirage.Server.Host.Management;
using Mirage.Server.Host.Net;
using Mirage.Server.Host.Services;
using Mirage.Shared;
using Serilog;
using Velopack;

// Server entry point and composition root: registers every singleton the game needs, then hands
// control to the generic host. MirageServerService (a hosted service, registered at the bottom) is
// what actually starts the world, the game loop, and the TCP acceptor.
//
// The first three steps are order-sensitive:
//   1. Velopack runs BEFORE anything else — an install or update step may exit the process outright.
//   2. The working directory is pinned to the exe directory, so data/log paths in appsettings.json
//      resolve against the install rather than wherever the process happened to be launched from.
//   3. A bootstrap console logger is installed before the host exists, so failures during startup
//      are still reported; the host replaces it with the appsettings-configured Serilog pipeline.
VelopackApp.Build().Run();

// Resolve data-relative paths in appsettings.json against the exe directory.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// ── Operator settings ─────────────────────────────────────────────────────────
// Read before anything else, because the language it carries decides what every line below is written
// in — including the complaint about the file itself, which is why THAT one is in English.
// A bad config never blocks a boot: the server runs on stock settings and says so.
var (serverConfig, configError) = ServerConfigStore.Load(ServerConfigStore.DefaultPath);
string langDir = Path.Combine(AppContext.BaseDirectory, "lang");
ServerStrings.Load(langDir, serverConfig.Language);

// ── Console capture, for remote operators ─────────────────────────────────────
// Installed BEFORE any logger exists, so it catches the whole pipeline as well as the console commands'
// own writes. Serilog's console sink resolves Console.Out; whether it does so once or per line, by this
// point Console.Out is already the tee.
var consoleTee = new ConsoleTee(Console.Out);
Console.SetOut(consoleTee);

// ── Bootstrap logger (used during startup before appsettings.json is loaded) ──
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Reported now rather than at load time: the bootstrap logger did not exist yet up there. Always said
// out loud, because the alternative is an operator whose settings silently do nothing.
if (configError is not null) Log.Warning("{ConfigError}", configError);

// ── Build and run the host ────────────────────────────────────────────────────

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, lc) => lc.ReadFrom.Configuration(context.Configuration))
    .ConfigureServices((ctx, services) =>
    {
        // ── Shared singletons ─────────────────────────────────────────────────

        // Wall clock and the source of chance. Every system takes these as OPTIONAL constructor
        // parameters defaulting to these same implementations, so registering them changes nothing at
        // runtime — it just makes the production wiring explicit rather than implicit in a null-coalesce,
        // and gives one place to swap them (a fixed clock for a replay harness, a seeded generator for a
        // reproducible stress run). Tests pin them per-system instead of through this container.
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IRandomSource>(SharedRandom.Instance);

        // Registered like the two above: systems take it as an optional parameter defaulting to
        // ServerConfig.Default, so this line is what makes the FILE take effect.
        services.AddSingleton(serverConfig);

        // World state (all mutable game arrays)
        services.AddSingleton<GameWorld>();

        // Player / editor session managers (1-based arrays)
        services.AddSingleton<PlayerManager>();
        services.AddSingleton<EditorSessionManager>();

        // ── Persistence ───────────────────────────────────────────────────────
        // Data directory defaults to "data/" relative to the executable.
        string dataDir = ctx.Configuration["DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "data");
        string logsDir = ctx.Configuration["LogsDir"] ?? Path.Combine(AppContext.BaseDirectory, "logs");

        Serilog.ILogger chatSerilogLogger = new Serilog.LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(logsDir, "chat", ".log"),
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:HH:mm:ss}] [{ChatType}] {Message:lj}{NewLine}")
            .CreateLogger();
        services.AddSingleton<IChatLog>(new SerilogChatLog(chatSerilogLogger));

        services.AddSingleton<IPersistenceService>(sp =>
            new JsonPersistenceService(dataDir,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonPersistenceService>>(),
                sp.GetRequiredService<IChatLog>()));
        // Off-thread player saves (game thread snapshots, this writes the file).
        services.AddSingleton<PlayerSaver>();
        // Tracks fire-and-forget persistence tasks: logs faults, drains on shutdown.
        services.AddSingleton<IBackgroundPersistence, BackgroundPersistence>();

        // ── Transport layer ───────────────────────────────────────────────────
        // TcpPacketDispatcher is both the IPacketDispatcher and the concrete type needed
        // by TcpConnectionAcceptor to call RegisterPlayer / RegisterEditor.
        services.AddSingleton<TcpPacketDispatcher>();
        services.AddSingleton<IPacketDispatcher>(sp =>
            sp.GetRequiredService<TcpPacketDispatcher>());

        // ── Game logic ────────────────────────────────────────────────────────
        services.AddSingleton<MovementSystem>();
        services.AddSingleton<CombatSystem>();
        services.AddSingleton<ItemSystem>();
        services.AddSingleton<SpellSystem>();
        services.AddSingleton<ShopSystem>();
        services.AddSingleton<BankSystem>();
        services.AddSingleton<PlayerSpawnSystem>();
        services.AddSingleton<PartySystem>();
        services.AddSingleton<GuildSystem>();
        services.AddSingleton<GuildScheduleSystem>();
        services.AddSingleton<GuildTerritorySystem>();
        services.AddSingleton<GuildWarSystem>();
        services.AddSingleton<MailSystem>();
        services.AddSingleton<MarketSystem>();
        services.AddSingleton<TradeSystem>();
        services.AddSingleton<ObjectiveSystem>();
        services.AddSingleton<QuestSystem>();
        services.AddSingleton<ConversationSystem>();
        // Lazy CombatSystem for QuestSystem — defers resolution to break the CombatSystem<->JoinLeaveSystem
        // <->QuestSystem construction cycle (QuestSystem only needs it for level-up at reward time).
        services.AddSingleton(p => new Lazy<CombatSystem>(() => p.GetRequiredService<CombatSystem>()));
        services.AddSingleton<SocialSystem>();
        services.AddSingleton<SpawnSystem>();
        services.AddSingleton<JoinLeaveSystem>();
        services.AddSingleton<NpcAiSystem>();
        services.AddSingleton<RegenerationSystem>();
        services.AddSingleton<PkExpirySystem>();
        services.AddSingleton<TimeOfDaySystem>();
        services.AddSingleton<WeatherSystem>();
        services.AddSingleton<BloodSystem>();
        services.AddSingleton<GameLoop>();

        // ── Packet handlers ───────────────────────────────────────────────────
        // Game traffic and editor traffic are dispatched by separate handlers; they share the world
        // and the dispatcher but almost nothing else.
        services.AddSingleton<PacketHandler>();
        services.AddSingleton<EditorPacketHandler>();

        // ── Connection acceptor ───────────────────────────────────────────────
        services.AddSingleton<TcpConnectionAcceptor>();

        // ── Remote management ─────────────────────────────────────────────────
        // Off unless serverconfig.json carries both a port and a token.
        services.AddSingleton(consoleTee);

        // ── Hosted services ───────────────────────────────────────────────────
        // MirageServerService starts the world, game loop, and TCP acceptor.
        services.AddHostedService<MirageServerService>();
        // ConsoleCommands reads admin commands from stdin. Registered as itself as well as a hosted
        // service, because the management listener runs commands through the same instance.
        services.AddSingleton<ConsoleCommands>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsoleCommands>());
        services.AddHostedService<ManagementListener>();
    })
    .Build();

// Wire the per-player locale resolver so ServerStrings.ForPlayer(index, …) can read each
// session's Language without ServerStrings holding a direct PlayerManager reference.
var playerManager = host.Services.GetRequiredService<PlayerManager>();
ServerStrings.SetPlayerLocaleResolver(index => playerManager[index].Language);

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server crashed");
}
finally
{
    Log.CloseAndFlush();
}
