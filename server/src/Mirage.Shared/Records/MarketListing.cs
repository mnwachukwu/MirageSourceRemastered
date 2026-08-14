namespace Mirage.Shared.Records;

/// <summary>One live marketplace listing: a single item stack a seller has put up for a fixed gold price,
/// with the item escrowed off the seller until it sells or is canceled. Carried whole to browsing clients,
/// so keep it a plain POCO. Player-to-player sales ride the delayed mail path (see MailMessage/MailSystem).</summary>
public sealed class MarketListing
{
    /// <summary>Stable global id (1-based), used to buy / cancel a specific listing.</summary>
    public int Id { get; set; }
    /// <summary>Seller account login — the sale payout and any cancellation return go here.</summary>
    public string Seller { get; set; } = "";
    /// <summary>The listed stack: 1-based item number, quantity / currency amount, and worn durability (gear).</summary>
    public int ItemNum { get; set; }
    public int Quantity { get; set; }
    public int Dur { get; set; }
    /// <summary>Total gold the buyer pays; the seller nets this minus the sale tax.</summary>
    public int Price { get; set; }
    /// <summary>UTC-seconds the listing was posted (drives newest-first ordering).</summary>
    public long ListedUtc { get; set; }

    /// <summary>All-value fields (and an immutable string), so a memberwise copy is a full deep copy.</summary>
    public MarketListing Clone() => (MarketListing)MemberwiseClone();
}
