namespace Mirage.Shared.Protocol;

/// <summary>
/// A snapshot of what the server currently IS, for an operator's dashboard: who is online and the
/// world state they are in.
///
/// <para>A structured message rather than something scraped out of the console. The console prints a
/// human-facing table, localized into four languages and free to be reworded — a UI built on parsing
/// it breaks the first time somebody edits a string.</para>
/// </summary>
public sealed record ServerStatus
{
    /// <summary>The line prefix that marks a status message on an otherwise human-readable stream. Both
    /// shells filter it out of the console view; a terminal never receives one, because the emitter is
    /// off unless a consumer asked for it.</summary>
    public const string LinePrefix = "@@MIRAGE-STATUS@@ ";

    public string TimePhase { get; init; } = "";
    public string Weather { get; init; } = "";
    public string Motd { get; init; } = "";

    /// <summary>Seconds since the world finished loading.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>The game port, so a dashboard can state it without being told separately.</summary>
    public int Port { get; init; }

    /// <summary>How many operators are attached over the management port.</summary>
    public int Operators { get; init; }

    public IReadOnlyList<PlayerSummary> Players { get; init; } = [];
}

/// <summary>One online player, in the terms a dashboard row needs. <see cref="Login"/> is carried
/// because the account is what /ban and /setaccess act on, while the NAME is what an operator reads.</summary>
public sealed record PlayerSummary
{
    public int Slot { get; init; }
    public string Name { get; init; } = "";
    public string Login { get; init; } = "";
    public int Level { get; init; }
    public string Class { get; init; } = "";
    public int Map { get; init; }
    public string Access { get; init; } = "";
}
