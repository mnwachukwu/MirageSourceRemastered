using Microsoft.Extensions.Logging;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Server.Core.Net;

/// <summary>
/// The editor's account browser — CREATOR only, and the only editor surface that describes a person.
///
/// <para> <b>Everything here reads account FILES,</b> so none of it may run on the game thread. Each
/// handler starts its work off the loop and hops back once, for the two things only the loop knows: who
/// is online, and applying an edit to a live player.</para>
///
/// <para> <b>The password is never loaded into a packet, never sent, and never accepted back.</b> A
/// save re-reads the record from disk and copies only the fields a Creator may change onto it, so
/// anything absent from the wire is preserved rather than blanked — which is also what keeps the
/// moderation timers out of reach from here.</para>
/// </summary>
public sealed partial class EditorPacketHandler
{
    /// <summary>Never let a page ask for the whole account directory in one packet.</summary>
    private const int MaxAccountPageSize = 100;

    private void HandleEditorRequestAccounts(int editorIndex, EditorRequestAccountsPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;

        int pageSize = Math.Clamp(p.PageSize, 1, MaxAccountPageSize);
        int page = Math.Max(0, p.Page);
        string search = p.Search.Trim();

        RunAsync(SendAccountPageAsync(editorIndex, search, p.Access, page, pageSize), nameof(SendAccountPageAsync));
    }

    private async Task SendAccountPageAsync(int editorIndex, string search, AdminLevel? access, int page, int pageSize)
    {
        var (rows, total) = await _persistence.ListAccountsAsync(search, access, page * pageSize, pageSize);
        var online = await OnGameThreadAsync(OnlineByLogin);

        var accounts = rows.Select(r =>
        {
            bool isOnline = online.TryGetValue(r.Login, out string? playingAs);
            return new EditorAccountRow
            {
                Login = r.Login,
                Access = r.Access,
                IsOnline = isOnline,
                PlayingAs = playingAs ?? "",
                CharNames = [.. r.CharNames],
            };
        }).ToList();

        SendToEditorIfStillCreator(editorIndex, new EditorAccountListPacket
        {
            Accounts = accounts,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    private void HandleEditorRequestAccount(int editorIndex, EditorRequestAccountPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0) return;

        RunAsync(SendAccountAsync(editorIndex, login), nameof(SendAccountAsync));
    }

    private async Task SendAccountAsync(int editorIndex, string login)
    {
        var account = await _persistence.LoadAccountAsync(login);
        if (account is null) return;
        var online = await OnGameThreadAsync(OnlineByLogin);

        SendToEditorIfStillCreator(editorIndex, new EditorAccountPacket
        {
            Login = account.Login,
            Access = account.Access,
            IsOnline = online.ContainsKey(account.Login),
            Guild = account.Guild,
            GuildRank = account.GuildRank,
            Chars = [.. NamedCharRows(account)],
        });
    }

    private void HandleEditorSaveAccount(int editorIndex, EditorSaveAccountPacket p)
    {
        if (!RequireAccess(editorIndex, AdminLevel.Creator)) return;
        string login = p.Login.Trim();
        if (login.Length == 0) return;

        var session = _editors.GetSession(editorIndex);
        RunAsync(SaveAccountAsync(editorIndex, login, p, session?.Login ?? ""), nameof(SaveAccountAsync));
    }

    private async Task SaveAccountAsync(int editorIndex, string login, EditorSaveAccountPacket p, string byLogin)
    {
        // Read-modify-write through the SAME per-login chain every other account write uses, so an edit
        // cannot race a character autosave. The closure captures values only — it runs later, off-thread.
        var edits = p.Chars.Where(c => c.Slot >= 1 && c.Slot <= Constants.MaxChars).ToList();
        var access = p.Access;

        // Nobody edits their OWN access. A Creator who demotes themselves by mistake locks themselves
        // out of the section that could put it back, and the only repair is a hand-edited JSON file. The
        // editor greys the picker too, but this is the check that counts.
        bool self = string.Equals(byLogin, login, StringComparison.OrdinalIgnoreCase);
        if (self)
        {
            _logger.LogInformation("Editor {By} saved their own account; the access change was ignored.", byLogin);
        }

        await _saver.MutateAccountAsync(login, account =>
        {
            if (!self) account.Access = access;
            foreach (var e in edits)
            {
                var c = account.Chars[e.Slot];
                // An empty slot stays empty: an edit to a character that does not exist would otherwise
                // conjure a nameless one that the character-select screen would then offer.
                if (c.Name.Trim().Length == 0) continue;
                ApplyCharEdit(c, e);
            }
        });

        _logger.LogInformation("Editor {By} saved account {Login}.", byLogin, login);

        // The live player carries its own copy of everything above, so an edit that only reached the file
        // would not show until they relogged. Re-sync on the loop, then re-send the record so the form
        // shows what actually landed.
        await OnGameThreadAsync(() =>
        {
            foreach (int slot in _pm.Online)
            {
                if (!_pm[slot].IsPlaying) continue;
                if (!string.Equals(_pm[slot].Login, login, StringComparison.OrdinalIgnoreCase)) continue;

                if (!self) _pm[slot].Char.Access = access;
                foreach (var e in edits)
                {
                    if (!string.Equals(_pm[slot].Char.Name.Trim(), e.Name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    ApplyCharEdit(_pm[slot].Char, e);
                }
                // The join handshake WITHOUT the welcome: re-sends their own record and re-syncs the
                // region around them, which is what makes a moved or re-levelled character land.
                _joinLeave.SendJoinData(slot);
            }
            return true;
        });

        await SendAccountAsync(editorIndex, login);
    }

    // The fields a Creator may change, in one place so the file write and the live player cannot disagree
    // about what an edit means.
    private void ApplyCharEdit(PlayerRecord c, EditorCharRow e)
    {
        c.Level = Math.Clamp(e.Level, 1, Constants.MaxLevel);
        c.Exp = Math.Max(0, e.Exp);
        c.Str = Math.Max(0, e.Str);
        c.Def = Math.Max(0, e.Def);
        c.Spd = Math.Max(0, e.Spd);
        c.Int = Math.Max(0, e.Int);
        c.Points = Math.Max(0, e.Points);
        if (SlotValidation.IsValidMapNum(e.Map, _world.Limits.Maps))
        {
            c.Map = e.Map;
            c.X = Math.Clamp(e.X, 0, Constants.MaxMapX);
            c.Y = Math.Clamp(e.Y, 0, Constants.MaxMapY);
        }
    }

    private static List<EditorCharRow> NamedCharRows(AccountRecord account)
    {
        var rows = new List<EditorCharRow>();
        for (int i = 1; i < account.Chars.Length; i++)
        {
            var c = account.Chars[i];
            if (c.Name.Trim().Length == 0) continue;
            rows.Add(new EditorCharRow
            {
                Slot = i,
                Name = c.Name.Trim(),
                Class = c.Class,
                Level = c.Level,
                Exp = c.Exp,
                Map = c.Map,
                X = c.X,
                Y = c.Y,
                Str = c.Str,
                Def = c.Def,
                Spd = c.Spd,
                Int = c.Int,
                Points = c.Points,
            });
        }
        return rows;
    }

    // Who is logged in, by account login, valued by the character they are on. Game thread only.
    private Dictionary<string, string> OnlineByLogin()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (int slot in _pm.Online)
        {
            if (!_pm[slot].IsPlaying) continue;
            map[_pm[slot].Login] = _pm[slot].Char.Name.Trim();
        }
        return map;
    }

    /// <summary>Sends an account payload only if that session is still authenticated AND still a Creator.
    /// The gather is async, so access can have been taken away — or the slot handed to somebody else —
    /// between the request and the reply.</summary>
    private void SendToEditorIfStillCreator(int editorIndex, Mirage.Shared.Protocol.IPacket packet)
    {
        var session = _editors.GetSession(editorIndex);
        if (session is null || !session.IsAuthenticated || session.AdminLevel < AdminLevel.Creator) return;
        _dispatcher.SendToEditor(editorIndex, packet);
    }

    /// <summary>Runs <paramref name="read"/> on the game thread and awaits it. The editor dispatch runs
    /// off the loop, so anything touching player state has to make this hop.</summary>
    private Task<T> OnGameThreadAsync<T>(Func<T> read)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameLoop.Post(() =>
        {
            try { tcs.TrySetResult(read()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }
}
