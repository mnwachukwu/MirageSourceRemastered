using Avalonia.Controls;

namespace Mirage.Editor.Models;

/// <summary>One selectable entry in a type-ahead picker: a record's slot number and its name.
/// Renders as "id: name", which is also what the picker's text box shows once a value is chosen.</summary>
public record NamedEntry(int Id, string Name)
{
    public override string ToString() => $"{Id}: {Name}";
}

/// <summary>Match predicates for the <see cref="NamedEntry"/> type-ahead pickers. Both accept a
/// name substring or an id prefix, and both tolerate the box already holding a full "id: name"
/// value — they strip that prefix so re-opening a filled picker still lists matches.</summary>
public static class NamedEntryFilter
{
    // Empty/null search → show all (including the id=0 "(none)" sentinel at the top).
    // Non-empty search → hide id=0; match name substring, plus id-prefix when the query is all digits.
    // If the search looks like "id: name" (i.e. the full ToString of a selected entry is in
    // the box), strip the prefix so clicking a filled picker still shows relevant results.
    /// <summary>Filter for 1-based lists where id 0 is the "(none)" sentinel: shown when the box is
    /// empty, hidden as soon as the author types.</summary>
    public static AutoCompleteFilterPredicate<object> ByNameOrId { get; } =
        (search, item) =>
        {
            if (item is not NamedEntry e) return false;
            if (string.IsNullOrEmpty(search)) return true;
            var colonIdx = search.IndexOf(": ", StringComparison.Ordinal);
            var query = colonIdx >= 0 ? search[(colonIdx + 2)..] : search;
            if (string.IsNullOrEmpty(query)) return true;
            if (e.Id == 0) return false;
            if (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            return IsAllDigits(query) && e.Id.ToString().StartsWith(query, StringComparison.Ordinal);
        };

    // Like ByNameOrId but for 0-based lists where id 0 is a real entry (e.g. tilesets), not the
    // "(none)" sentinel: id 0 is always searchable and never hidden.
    /// <summary>Filter for 0-based lists (tilesets and the like) where id 0 is a real entry, so it is
    /// always searchable and never hidden.</summary>
    public static AutoCompleteFilterPredicate<object> ByNameOrIndex { get; } =
        (search, item) =>
        {
            if (item is not NamedEntry e) return false;
            if (string.IsNullOrEmpty(search)) return true;
            var colonIdx = search.IndexOf(": ", StringComparison.Ordinal);
            var query = colonIdx >= 0 ? search[(colonIdx + 2)..] : search;
            if (string.IsNullOrEmpty(query)) return true;
            if (e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            return IsAllDigits(query) && e.Id.ToString().StartsWith(query, StringComparison.Ordinal);
        };

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
            if (c < '0' || c > '9') return false;
        return true;
    }
}
