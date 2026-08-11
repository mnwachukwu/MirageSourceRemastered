using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class SpellRecord
{
    private string _name = string.Empty;
    private string? _trimmedName;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _trimmedName = null;
        }
    }
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — cast-announcement messages TrimEnd
    /// the spell name on every successful cast.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public int ClassReq { get; set; }
    public SpellType Type { get; set; }
    public short Data1 { get; set; }
    public short Data2 { get; set; }
    public short Data3 { get; set; }
}
