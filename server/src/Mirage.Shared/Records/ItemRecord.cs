using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ItemRecord
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — record names are stored fixed-width and
    /// every item message string TrimEnds them.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public short Pic { get; set; }
    public ItemType Type { get; set; }
    public short Data1 { get; set; }
    public short Data2 { get; set; }
    public short Data3 { get; set; }

    /// <summary>Item restriction flags. Each blocks exactly one action; banking is always allowed.
    /// Absent = false, so existing item data is unaffected. All four are enforced server-side:
    /// <see cref="NonTradeable"/> in TradeSystem, <see cref="NonListable"/> in MarketSystem,
    /// <see cref="NonMailable"/> on the mail-attach path, and <see cref="DestroyOnDrop"/> in
    /// ItemSystem's drop paths.</summary>
    public bool NonTradeable { get; set; }   // can't be staged in a player trade
    public bool NonListable { get; set; }    // can't be sold on the marketplace
    public bool NonMailable { get; set; }    // can't be attached to / sent by mail
    public bool DestroyOnDrop { get; set; }  // dropping it (voluntary or on death) destroys it
}
