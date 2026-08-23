using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.Services;

/// <summary>
/// Who is holding which record, as this session last heard it.
///
/// <para>The server owns the truth and sends the whole table whenever it moves, so this is a cache and never
/// a decision: it drives what the lists show and what the editors refuse, and a save the server disagrees
/// with is still refused there.</para>
///
/// <para>Empty while offline. Locks are about other people, and offline there are none.</para>
/// </summary>
public sealed class EditorLockState
{
    private Dictionary<(string Section, int Num), string> _held = [];

    /// <summary>The account this session is signed in as, so its own locks can be told from everyone else's.</summary>
    public string MyLogin { get; set; } = "";

    /// <summary>Raised after the table changes, on whatever thread the packet arrived on — subscribers that
    /// touch view-model state marshal it themselves.</summary>
    public event Action? Changed;

    public void Apply(EditorLocksPacket p)
    {
        _held = p.Locks.ToDictionary(h => (h.Section, h.Num), h => h.Login);
        Changed?.Invoke();
    }

    /// <summary>Drops everything — used on disconnect, where every lock this session knew about belongs to a
    /// conversation that is over.</summary>
    public void Clear()
    {
        if (_held.Count == 0 && MyLogin.Length == 0) return;
        _held = [];
        MyLogin = "";
        Changed?.Invoke();
    }

    /// <summary>The account holding a record, or null when nobody is.</summary>
    public string? HolderOf(string section, int num) =>
        _held.TryGetValue((section, num), out string? login) ? login : null;

    /// <summary>Somebody ELSE has it. What greys a row out and refuses an edit — a lock of your own is just
    /// your own unsaved work, and must never lock you out of it.</summary>
    public bool IsHeldByOther(string section, int num)
    {
        string? holder = HolderOf(section, num);
        return holder is not null && !string.Equals(holder, MyLogin, StringComparison.OrdinalIgnoreCase);
    }
}
