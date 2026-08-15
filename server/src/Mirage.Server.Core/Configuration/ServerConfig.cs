namespace Mirage.Server.Core.Configuration;

/// <summary>
/// Everything an operator configures about their server: port, operator language, and the game rules
/// that used to be compile-time constants. appsettings.json configures the APPLICATION (Serilog) and is
/// hand-authored; this is machine-owned and rewritten by the shell.
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

    /// <summary>What a player loses when they die.</summary>
    public DeathPenaltyConfig DeathPenalty { get; init; } = new();
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
