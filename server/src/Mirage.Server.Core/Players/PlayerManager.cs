using Mirage.Shared;

namespace Mirage.Server.Core.Players;

public sealed class PlayerManager
{
    // 1-based; indices 1..Slots; index 0 unused
    private readonly ServerPlayer[] _players;

    /// <summary>This server's configured limit, and the highest valid slot. NOT
    /// <c>Slots</c>, which is only the protocol ceiling a client allocates for — every
    /// scan below walks THIS, so a 20-player world does no work for the 480 slots it will never use.</summary>
    public int Slots { get; }

    public PlayerManager(Configuration.ServerConfig? config = null)
    {
        Slots = (config ?? Configuration.ServerConfig.Default).MaxPlayers;
        _players = new ServerPlayer[Slots + 1];
        for (int i = 1; i <= Slots; i++)
            _players[i] = new ServerPlayer { DamageByPlayer = new int[Slots + 1] };
    }

    public ServerPlayer this[int index] => _players[index];

    /// <summary>Whether <paramref name="slot"/> indexes a real slot ON THIS SERVER.
    ///
    /// <para>Use this, not <c>SlotValidation.IsValidPlayerSlot</c>, anywhere a slot number is about to
    /// index a player. That one bounds by the PROTOCOL ceiling, which is what a client allocates for and
    /// is far larger than a typical server's array — a client-supplied slot of 400 would pass it and then
    /// throw indexing a 20-slot world.</para></summary>
    public bool IsValidSlot(int slot) => slot >= 1 && slot <= Slots;

    /// <summary>Someone joined or left. Raised ON THE GAME THREAD by <see cref="GameLogic.JoinLeaveSystem"/>
    /// so an operator's roster does not have to wait for a poll. Nothing in the game subscribes; this
    /// exists for the host's status broadcaster, which is why it is an event rather than a dependency.</summary>
    public event Action? RosterChanged;

    internal void NotifyRosterChanged() => RosterChanged?.Invoke();

    /// <summary>Flag a player so <see cref="GameLogic.GameLoop"/> persists it at the end of the current
    /// game tick. Call on any state change a player could otherwise undo by hard-disconnecting before
    /// the 60 s autosave — item drop/pickup, durability break, death, level-up, inventory sort. Cheap
    /// and idempotent.</summary>
    public void MarkDirty(int index)
    {
        if (index >= 1 && index <= Slots)
            _players[index].SaveDirty = true;
    }

    /// <summary>Returns the index (1..MaxPlayers) of the first disconnected, non-ghost slot, or 0 if all slots are full.</summary>
    public int FindOpenSlot()
    {
        for (int i = 1; i <= Slots; i++)
            if (!_players[i].IsConnected && !_players[i].IsGhost) return i;
        return 0;
    }

    /// <summary>
    /// Returns the slot index of a combat ghost whose account matches <paramref name="login"/>, or 0 if none.
    /// </summary>
    public int FindGhostByLogin(string login)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (_players[i].IsGhost &&
                string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>Index (1..MaxPlayers) of the CONNECTED, in-game player on account
    /// <paramref name="login"/>, or 0 if that account isn't currently playing.</summary>
    public int FindOnlineByLogin(string login)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (_players[i].IsPlaying &&
                string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Returns the index (1..MaxPlayers) of the in-game player whose name equals
    /// <paramref name="name"/> (case-insensitive, whole-name match), or 0 if not found.
    /// </summary>
    public int FindPlayerByName(string name)
    {
        string target = name.TrimEnd();
        for (int i = 1; i <= Slots; i++)
        {
            if (!_players[i].IsPlaying) continue;
            string pName = _players[i].Char.TrimmedName;
            if (string.Equals(pName, target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    public bool IsMultiAccount(string login, int excludeIndex)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (i == excludeIndex) continue;
            if (_players[i].IsConnected &&
                string.Equals(_players[i].Login, login, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool IsMultiIp(string ip, int excludeIndex)
    {
        for (int i = 1; i <= Slots; i++)
        {
            if (i == excludeIndex) continue;
            if (_players[i].IsConnected && _players[i].RemoteIp == ip)
                return true;
        }
        return false;
    }

    public int TotalOnline
    {
        get
        {
            int count = 0;
            for (int i = 1; i <= Slots; i++)
                if (_players[i].IsConnected && _players[i].InGame) count++;
            return count;
        }
    }

    public int GetTotalMapPlayers(int mapNum)
    {
        int count = 0;
        for (int i = 1; i <= Slots; i++)
            if (_players[i].IsPlaying && _players[i].Char.Map == mapNum) count++;
        return count;
    }
}
