using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Localization;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Persistence;
using Mirage.Server.Core.Players;
using Mirage.Shared;
using Mirage.Shared.Protocol;

namespace Mirage.Server.Host.Services;

/// <summary>
/// Reads admin commands from <see cref="System.Console.ReadLine"/> in a background task.
/// Runs until the host is stopping.
///
/// Available commands:
///   /who                        — list online players
///   /kick name [minutes]        — kick a player (default 60 min, range 1-1440)
///   /ban  name                  — ban a player
///   /mute name [minutes]        — mute a player (default 60 min, range 1-1440)
///   /refreshbanlist             — reload banlist.json from disk
///   /shutdown                   — request graceful shutdown
///   /help                       — list commands
/// </summary>
public sealed class ConsoleCommands : IHostedService
{
    private const int DefaultPenaltyMinutes = 60;
    private const int MaxPenaltyMinutes = 1440;

    private readonly IHostApplicationLifetime _lifetime;
    private readonly PlayerManager _pm;
    private readonly IPacketDispatcher _dispatcher;
    private readonly GameLoop _gameLoop;
    private readonly IPersistenceService _persistence;
    private readonly IBackgroundPersistence _bg;
    private readonly PlayerSaver _saver;
    private readonly ILogger<ConsoleCommands> _logger;

    private Task? _loopTask;
    private CancellationTokenSource? _cts;

    public ConsoleCommands(
        IHostApplicationLifetime lifetime,
        PlayerManager pm,
        IPacketDispatcher dispatcher,
        GameLoop gameLoop,
        IPersistenceService persistence,
        IBackgroundPersistence bg,
        PlayerSaver saver,
        ILogger<ConsoleCommands> logger)
    {
        _lifetime = lifetime;
        _pm = pm;
        _dispatcher = dispatcher;
        _gameLoop = gameLoop;
        _persistence = persistence;
        _bg = bg;
        _saver = saver;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => InputLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.WaitAsync(ct).ConfigureAwait(false); }
            catch { /* ignore cancellation */ }
        }
    }

    // ── Command loop ──────────────────────────────────────────────────────────

    private async Task InputLoopAsync(CancellationToken ct)
    {
        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_Prompt, ("GameName", Constants.GameName)));

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                // ReadLine blocks; Task.Run offloads it so cancellation still works
                line = await Task.Run(System.Console.ReadLine, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }

            if (line is null) break;  // EOF / Ctrl-C
            line = line.Trim();
            if (line.Length == 0) continue;

            ExecuteCommand(line);
        }
    }

    private void ExecuteCommand(string input)
    {
        string cmd = input;
        string args = "";

        int space = input.IndexOf(' ');
        if (space >= 0)
        {
            cmd = input[..space].ToLowerInvariant();
            args = input[(space + 1)..].Trim();
        }
        else
        {
            cmd = input.ToLowerInvariant();
        }

        switch (cmd)
        {
            case "/help":
                System.Console.WriteLine(ServerStrings.Get(ServerStrings.Console_Help));
                break;

            case "/who":
                // Reads player state — run on the game thread so it sees a consistent snapshot.
                _gameLoop.Post(CmdWho);
                break;

            case "/kick":
                _gameLoop.Post(() => CmdKick(args));
                break;

            case "/ban":
                _gameLoop.Post(() => CmdBan(args));
                break;

            case "/mute":
                _gameLoop.Post(() => CmdMute(args));
                break;

            case "/refreshbanlist":
                CmdRefreshBanList();
                break;

            case "/shutdown":
                System.Console.WriteLine(ServerStrings.Get(ServerStrings.Console_Shutdown));
                _logger.LogInformation("Console operator requested shutdown.");
                _lifetime.StopApplication();
                break;

            default:
                System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_UnknownCommand, ("Cmd", cmd)));
                break;
        }
    }

    // ── /who ─────────────────────────────────────────────────────────────────

    private void CmdWho()
    {
        int count = 0;
        for (int i = 1; i <= Constants.MaxPlayers; i++)
        {
            if (!_pm[i].IsPlaying) continue;
            var c = _pm[i].Char;
            System.Console.WriteLine(
                $"  [{i,2}] {c.Name.Trim(),-20}  map={c.Map,4}  ({_pm[i].Login})");
            count++;
        }
        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_WhoTotal, ("Count", count)));
    }

    // ── Penalty helpers ──────────────────────────────────────────────────────

    /// <summary>Splits "name [minutes]" into a name and a validated minutes value. Returns false (with
    /// a usage or invalid-minutes message printed) when the input can't be applied.</summary>
    private bool TryParsePenaltyArgs(string args, string usageKey, out string name, out int minutes)
    {
        name = "";
        minutes = 0;
        if (string.IsNullOrEmpty(args))
        {
            System.Console.WriteLine(ServerStrings.Get(usageKey));
            return false;
        }

        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        name = parts[0];

        if (parts.Length == 1)
        {
            minutes = DefaultPenaltyMinutes;
            return true;
        }
        if (!int.TryParse(parts[1], out int parsed) || parsed < 1 || parsed > MaxPenaltyMinutes)
        {
            System.Console.WriteLine(ServerStrings.Get(ServerStrings.AdminCommand_InvalidMinutes));
            return false;
        }

        minutes = parsed;
        return true;
    }

    private string ConsoleOperatorName => ServerStrings.Get(ServerStrings.AdminCommand_ConsoleOperatorName);

    private bool ResolveTarget(string name, out int slot, out string charName)
    {
        slot = _pm.FindPlayerByName(name);
        charName = "";
        if (slot <= 0)
        {
            System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_PlayerNotOnline, ("Name", name)));
            return false;
        }
        if (_pm[slot].Char.Access != AdminLevel.Player)
        {
            System.Console.WriteLine(ServerStrings.Get(ServerStrings.AdminCommand_CannotTargetAdmin));
            return false;
        }
        charName = _pm[slot].Char.Name.Trim();
        return true;
    }

    // ── /kick ─────────────────────────────────────────────────────────────────

    private void CmdKick(string args)
    {
        if (!TryParsePenaltyArgs(args, ServerStrings.Console_KickUsage, out string name, out int minutes)) return;
        if (!ResolveTarget(name, out int slot, out string charName)) return;

        string targetLogin = _pm[slot].Login;
        long expiryUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minutes * 60L;
        ApplyAccountKick(targetLogin, expiryUtc);

        _dispatcher.SendToAll(PacketBuilder.ChatMsg(
            ServerStrings.Format(ServerStrings.AdminCommand_KickBroadcast,
                ("Target", charName), ("GameName", Constants.GameName), ("Admin", ConsoleOperatorName), ("Minutes", minutes)),
            12, ChatChannel.Notice));
        _dispatcher.SendTo(slot, PacketBuilder.Alert(ServerStrings.Format(
            ServerStrings.AdminCommand_Kicked, ("Admin", ConsoleOperatorName), ("Minutes", minutes))));
        _dispatcher.GracefulDisconnect(slot);

        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_Kicked,
            ("Name", charName), ("Slot", slot), ("Minutes", minutes)));
        _logger.LogInformation("Console kicked player {Name} (slot {Slot}) for {Minutes} minute(s).", charName, slot, minutes);
    }

    private void ApplyAccountKick(string login, long expiryUtc) =>
        _saver.MutateAccountInBackground(login, a => a.KickedUntilUtc = expiryUtc);

    // ── /ban ──────────────────────────────────────────────────────────────────

    private void CmdBan(string args)
    {
        if (string.IsNullOrEmpty(args))
        {
            System.Console.WriteLine(ServerStrings.Get(ServerStrings.Console_BanUsage));
            return;
        }
        if (!ResolveTarget(args.Trim(), out int slot, out string charName)) return;

        string targetLogin = _pm[slot].Login;
        _bg.Run(_persistence.BanAsync(targetLogin, $"Banned by {ConsoleOperatorName}"),
                nameof(IPersistenceService.BanAsync));

        _dispatcher.SendToAll(PacketBuilder.ChatMsg(
            ServerStrings.Format(ServerStrings.AdminCommand_BanBroadcast,
                ("Target", charName), ("GameName", Constants.GameName), ("Admin", ConsoleOperatorName)),
            12, ChatChannel.Notice));
        _dispatcher.SendTo(slot, PacketBuilder.Alert(ServerStrings.Format(
            ServerStrings.Auth_Banned, ("GameName", Constants.GameName))));
        _dispatcher.GracefulDisconnect(slot);

        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_Banned,
            ("Name", charName), ("Slot", slot)));
        _logger.LogInformation("Console banned player {Name} (slot {Slot}).", charName, slot);
    }

    // ── /mute ─────────────────────────────────────────────────────────────────

    private void CmdMute(string args)
    {
        if (!TryParsePenaltyArgs(args, ServerStrings.Console_MuteUsage, out string name, out int minutes)) return;
        if (!ResolveTarget(name, out int slot, out string charName)) return;

        string targetLogin = _pm[slot].Login;
        long expiryUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minutes * 60L;
        _pm[slot].MutedUntilUtc = expiryUtc;
        ApplyAccountMute(targetLogin, expiryUtc);

        _dispatcher.SendToAll(PacketBuilder.ChatMsg(
            ServerStrings.Format(ServerStrings.AdminCommand_MuteBroadcast,
                ("Target", charName), ("Admin", ConsoleOperatorName), ("Minutes", minutes)),
            12, ChatChannel.Notice));

        System.Console.WriteLine(ServerStrings.Format(ServerStrings.Console_Muted,
            ("Name", charName), ("Slot", slot), ("Minutes", minutes)));
        _logger.LogInformation("Console muted player {Name} (slot {Slot}) for {Minutes} minute(s).", charName, slot, minutes);
    }

    private void ApplyAccountMute(string login, long expiryUtc) =>
        _saver.MutateAccountInBackground(login, a => a.MutedUntilUtc = expiryUtc);

    // ── /refreshbanlist ──────────────────────────────────────────────────────

    private void CmdRefreshBanList()
    {
        _bg.Run(_persistence.RefreshBanListAsync(), nameof(IPersistenceService.RefreshBanListAsync));
        System.Console.WriteLine(ServerStrings.Get(ServerStrings.AdminCommand_BanListRefreshed));
        _logger.LogInformation("Console refreshed ban list from disk.");
    }
}
