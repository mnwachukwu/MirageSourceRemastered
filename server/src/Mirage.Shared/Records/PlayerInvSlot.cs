namespace Mirage.Shared.Records;

/// <summary>One inventory slot. An empty slot is <see cref="Num"/> == 0 rather than a null entry, so the
/// bag is a fixed-length array and slot numbers stay stable across a drop or a sale.</summary>
public sealed class PlayerInvSlot
{
    /// <summary>Item slot number; 0 = the inventory slot is empty.</summary>
    public int Num { get; set; }
    /// <summary>Stack quantity, for a currency-type item.</summary>
    public int Value { get; set; }
    /// <summary>Current durability of this copy; equipment wears per-instance, not per-item-type.</summary>
    public int Dur { get; set; }
}
