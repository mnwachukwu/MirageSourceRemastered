namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Everything an operator configures about their server: port, operator language, remote access, and the
/// switchable game rules. appsettings.json configures the APPLICATION (Serilog) and is hand-authored;
/// this is machine-owned and rewritten by the shell.
///
/// <para>Only values NO CLIENT READS may live here. <c>Constants</c> is in <c>Mirage.Shared</c> and its
/// values are <c>const</c>, so anything the client compiles against is inlined into it — moving one here
/// desyncs every connected client silently. Cooldowns, the blood model and the record ceilings stay
/// const for that reason.</para>
/// </summary>
public sealed record ServerConfig
{
    /// <summary>The stock rules: what the game does with no config file present. Shared, and safe to
    /// share, because the whole graph is immutable.</summary>
    public static readonly ServerConfig Default = new();

    /// <summary>The TCP port the game listens on.</summary>
    public int Port { get; init; } = Mirage.Shared.Constants.GamePort;

    /// <summary>The OPERATOR's language — console, logs, and the shell's chrome. Players are unaffected;
    /// their messages resolve through each session's own locale.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Where the world lives. Empty means <c>data/</c> beside the executable, which is what a
    /// stock install runs; an absolute path puts it on another drive or somewhere that gets backed up.
    ///
    /// <para>Here rather than in appsettings.json because it configures the SERVER, which is the line the
    /// two files are split on. <c>Program.cs</c> still honors the old <c>DataDir</c> key in appsettings.json
    /// when this is empty, so an install predating the move keeps working.</para></summary>
    public string DataDir { get; init; } = "";

    /// <summary>What a player loses when they die.</summary>
    public DeathPenaltyConfig DeathPenalty { get; init; } = new();

    /// <summary>How many players this server accepts at once, and the size of every per-player array it
    /// allocates. Deliberately SMALL by default: the right number depends on the machine, and the server
    /// window's load benchmark measures it rather than asking an operator to guess.
    ///
    /// <para>Clamped to <c>Constants.MaxPlayers</c>, the protocol ceiling. Above that the server would
    /// issue slot numbers a shipped client cannot index.</para></summary>
    /// <para>Every server-side scan walks <c>PlayerManager.Slots</c> — this value — rather than the
    /// constant, so a small world does no work and holds no array for slots it will never use.</para>
    public int MaxPlayers
    {
        get;
        init => field = Math.Clamp(value, 1, Mirage.Shared.Constants.MaxPlayers);
    } = 20;

    /// <summary>Where a character starts, and where one respawns without a purchased spawn point.</summary>
    public SpawnConfig Spawn { get; init; } = new();

    /// <summary>When the weekly territory contest runs.</summary>
    public ScheduleConfig Schedule { get; init; } = new();

    /// <summary>Remote operator access. Off unless configured.</summary>
    public ManagementConfig Management { get; init; } = new();
}

/// <summary>
/// The world's front door: where a new character is placed, and where anyone without a purchased spawn
/// point comes back.
///
/// <para>The defaults are the middle of map 1, which is what these were as computed constants. Nothing on
/// the client reads them, so they are a plain server setting.</para>
/// </summary>
public sealed record SpawnConfig
{
    public int Map { get; init; } = 1;
    public int X { get; init; } = (Mirage.Shared.Constants.MaxMapX + 1) / 2;
    public int Y { get; init; } = (Mirage.Shared.Constants.MaxMapY + 1) / 2;
}

/// <summary>
/// The weekly territory contest. Server-local, so a world keeps the evening its players actually play on
/// rather than one derived from UTC.
///
/// <para>The DAILY guild settlement is deliberately not here: it runs at midnight on the host box and
/// <c>GuildScheduleSystem</c> walks whole calendar days, which is what makes a slot missed during downtime
/// replay correctly on the next boot.</para>
/// </summary>
public sealed record ScheduleConfig
{
    public DayOfWeek WarNightDay { get; init; } = DayOfWeek.Saturday;

    /// <summary>0-23, server-local.</summary>
    public int WarNightHour { get; init; } = 20;

    /// <summary>The weekly boundary — territory income snapshots, season weeks and the weekly quest reset.
    /// DERIVED as the day after war night, never configured separately: the two were a pair of constants
    /// documented as "the day after", and two settings could be moved out of step with each other.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DayOfWeek WeekResetDay => (DayOfWeek)(((int)WarNightDay + 1) % 7);
}

/// <summary>
/// The remote management channel: the same console the local shell drives, reachable over a socket.
///
/// <para>Both fields must be set for the listener to start. Either one alone is a misconfiguration, not a
/// half-enabled server — an open port with no token would be an unauthenticated console.</para>
/// </summary>
public sealed record ManagementConfig
{
    /// <summary>Listening port, or 0 for off. Separate from the game port so remote administration can be
    /// firewalled without touching the port players connect to.</summary>
    public int Port { get; init; }

    /// <summary>The shared secret an attaching shell must present. Empty refuses the listener.</summary>
    public string Token { get; init; } = "";

    /// <summary>True when this server should accept remote operators. Derived, so it is kept out of the
    /// file — a serialized copy would be a second place the answer could be written down.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEnabled => Port > 0 && Token.Length > 0;
}

/// <summary>
/// The three components of a death penalty, each switchable on its own.
///
/// <para>Two things survive whatever these say: the sub-level-10 spare, and the ORDER at each death site
/// — drops run before durability damage so a piece that breaks still gets its equipped drop chance. The
/// switches gate the penalty helpers from the inside so no call site moves.</para>
/// </summary>
public sealed record DeathPenaltyConfig
{
    /// <summary>Worn gear loses durability on death. Casting reagents ride this switch rather than the
    /// item drop: they are the caster's durability, priced against the same repair curve.</summary>
    public bool DurabilityLoss { get; init; } = true;

    /// <summary>Items fall out of the bag on death.</summary>
    public bool ItemDrop { get; init; } = true;

    /// <summary>Death costs EXP, and enough of it costs levels. Off also means a PvP killer earns
    /// nothing — that reward is transferred out of the victim's loss, so there is nothing to hand
    /// over. PvE EXP is unaffected.</summary>
    public bool ExpLoss { get; init; } = true;
}
