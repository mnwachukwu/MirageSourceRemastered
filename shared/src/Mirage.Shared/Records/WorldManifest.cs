using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// What a world folder says about itself, read from <c>world.json</c> at its root.
///
/// <para>A world's size is a property of the world, not of the program that opens it. A server states its
/// own in the pre-login hello and the editor sizes to match; offline there is nobody to ask, so the folder
/// carries the same answer. Two worlds of different sizes can then sit side by side and each open at its
/// own ceiling.</para>
///
/// <para>The file is optional: absent or unreadable, <see cref="RecordLimits.Default"/> applies.</para>
/// </summary>
public sealed record WorldManifest
{
    /// <summary>The file's name at the root of a world folder.</summary>
    [JsonIgnore]
    public const string FileName = "world.json";

    /// <summary>How many of each record family this world has room for. Clamped on read, so a hand-edited
    /// file cannot ask for a zero-length family or an allocation measured in gigabytes.</summary>
    public RecordLimits Records
    {
        get;
        init => field = (value ?? RecordLimits.Default).Clamped(RecordLimits.Ceiling);
    } = RecordLimits.Default;
}
