using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

public sealed class ShopRecord
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
    /// <summary>Cached <see cref="Name"/>.TrimEnd() — the "you traded with {shop}" line TrimEnds the name.</summary>
    [JsonIgnore]
    public string TrimmedName => _trimmedName ??= _name.TrimEnd();

    public bool FixesItems { get; set; }
    public ShopType ShopType { get; set; }
    public bool AllowBanking { get; set; }

    // The NPC template number this shop/inn is assigned to (0 = none). Shops are not map-bound: an NPC whose
    // number matches a shop's Keeper is a vendor for it — interacting with that NPC opens
    // this shop, and the NPC shows the $ overhead glyph. Kept on the shop record (not NpcRecord) by design.
    public int Keeper { get; set; }

    // Trades this shop offers — a dense, 0-based list of real trades (author-defined count, capped at
    // Constants.MaxTrades). The purchase/display slot number on the wire stays 1-based (slot N =
    // TradeItem[N-1]) so the client and trade-request protocol are unchanged. Legacy shop JSON stored a
    // fixed 1-based array (a leading null at index 0 + slots 1..8); it deserializes into this list and is
    // compacted on load (drop null/empty) — see JsonPersistenceService and ShopRowViewModel.
    public List<TradeItemRecord> TradeItem { get; set; } = new();
}
