using Mirage.Shared.Protocol.Packets;

namespace Mirage.Server.Core.Net;

/// <summary>
/// Who is holding which record open with unsaved changes.
///
/// <para>A lock is taken when an editor DIRTIES a record and given back when it saves or discards. Reading
/// takes nothing, so the table only ever names people who actually have changes in hand — and a session that
/// drops takes every one of its locks with it, so a crashed editor cannot wedge a record shut.</para>
///
/// <para>In memory only, and deliberately: a lock is a fact about a live connection, and one that outlived
/// the process would be a lock nobody can release.</para>
/// </summary>
public sealed class EditorLockRegistry
{
    private readonly record struct Key(string Section, int Num);
    private readonly Dictionary<Key, (int EditorIndex, string Login)> _held = [];
    private readonly Lock _gate = new();

    /// <summary>Claims a record for <paramref name="editorIndex"/>. True when it now holds it — including
    /// when it already did, since dirtying a record twice is not a conflict. False when somebody else has it.</summary>
    public bool TryAcquire(string section, int num, int editorIndex, string login)
    {
        lock (_gate)
        {
            var key = new Key(section, num);
            if (_held.TryGetValue(key, out var cur)) return cur.EditorIndex == editorIndex;
            _held[key] = (editorIndex, login);
            return true;
        }
    }

    /// <summary>Gives a record back. Only the holder can: an unlock from anyone else is ignored rather than
    /// honoured, or a second editor could free a record out from under the one still editing it.</summary>
    public bool Release(string section, int num, int editorIndex)
    {
        lock (_gate)
        {
            var key = new Key(section, num);
            if (!_held.TryGetValue(key, out var cur) || cur.EditorIndex != editorIndex) return false;
            _held.Remove(key);
            return true;
        }
    }

    /// <summary>Drops everything a session holds. True when it held anything, so the caller only broadcasts
    /// a table that actually changed.</summary>
    public bool ReleaseAll(int editorIndex)
    {
        lock (_gate)
        {
            var mine = _held.Where(kv => kv.Value.EditorIndex == editorIndex).Select(kv => kv.Key).ToList();
            foreach (var k in mine) _held.Remove(k);
            return mine.Count > 0;
        }
    }

    /// <summary>Who holds <paramref name="section"/>/<paramref name="num"/>, or null.</summary>
    public string? HolderOf(string section, int num)
    {
        lock (_gate)
        {
            return _held.TryGetValue(new Key(section, num), out var cur) ? cur.Login : null;
        }
    }

    /// <summary>What one session is holding. For the operator view, which reports a session by what it has
    /// open rather than by a slot number nobody recognises.</summary>
    public IReadOnlyList<(string Section, int Num)> HeldBy(int editorIndex)
    {
        lock (_gate)
        {
            return _held.Where(kv => kv.Value.EditorIndex == editorIndex)
                        .Select(kv => (kv.Key.Section, kv.Key.Num))
                        .OrderBy(h => h.Section, StringComparer.Ordinal).ThenBy(h => h.Num).ToList();
        }
    }

    /// <summary>The whole table, for the broadcast every editor agrees on.</summary>
    public EditorLocksPacket Snapshot()
    {
        lock (_gate)
        {
            return new EditorLocksPacket
            {
                Locks = _held.Select(kv => new EditorLocksPacket.Held(kv.Key.Section, kv.Key.Num, kv.Value.Login))
                             .OrderBy(h => h.Section, StringComparer.Ordinal).ThenBy(h => h.Num).ToArray(),
            };
        }
    }
}
