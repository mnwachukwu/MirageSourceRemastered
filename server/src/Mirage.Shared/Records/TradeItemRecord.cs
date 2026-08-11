namespace Mirage.Shared.Records;

/// <summary>One row of a shop's trade list, read from the SHOP's side: the shop gives
/// <see cref="GiveItem"/> and gets <see cref="GetItem"/> back. An ordinary purchase is simply a row
/// whose "get" side is the currency item.</summary>
public sealed class TradeItemRecord
{
    /// <summary>Item slot the shop hands over.</summary>
    public int GiveItem { get; set; }
    /// <summary>Quantity of <see cref="GiveItem"/> handed over.</summary>
    public int GiveValue { get; set; }
    /// <summary>Item slot the shop takes in exchange — the currency item for a normal sale.</summary>
    public int GetItem { get; set; }
    /// <summary>Quantity of <see cref="GetItem"/> required — the price.</summary>
    public int GetValue { get; set; }
}
