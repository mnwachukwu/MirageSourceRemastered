using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ClassRecord
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — class-requirement messages TrimEnd
    /// the class name when a player tries to learn an off-class spell.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public int Sprite { get; set; }
    public int Str { get; set; }
    public int Def { get; set; }
    public int Spd { get; set; }
    public int Int { get; set; }

    // Computed via StatFormulas at runtime — not persisted.
    [JsonIgnore] public int Hp { get; set; }
    [JsonIgnore] public int Mp { get; set; }
    [JsonIgnore] public int Sp { get; set; }
}
