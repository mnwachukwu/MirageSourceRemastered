using Mirage.Shared.Serialization;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Records;

/// <summary>
/// One map tile.  Three visual tile-art stacks, each a fixed-length stack of packed
/// <see cref="LayerCell"/> values (index 0 is the bottom; the editor labels them 1..N; a value of
/// <see cref="LayerCell.Empty"/> (0) means unused): <see cref="Ground"/> draws below all entities,
/// <see cref="Fringe"/> draws between the ground- and fringe-layer entity passes (the walkable top of
/// a bridge where a fringe layer exists), and <see cref="Canopy"/> draws on top of everything.
///
/// Gameplay attributes are per LOGICAL layer: the inline <see cref="Type"/> + Data1..3 govern the
/// ground layer, while <see cref="FringeAttr"/> (non-null iff a walkable fringe layer exists here)
/// governs the fringe layer.  Read both uniformly via LayerLogic.AttrFor.
///
/// Serialized by <see cref="TileRecordConverter"/>.
/// </summary>
[JsonConverter(typeof(TileRecordConverter))]
public sealed class TileRecord
{
    public int[] Ground { get; set; } = new int[Constants.MaxGroundLayers];
    public int[] Fringe { get; set; } = new int[Constants.MaxFringeLayers];
    public int[] Canopy { get; set; } = new int[Constants.MaxCanopyLayers];
    public TileType Type { get; set; }
    public short Data1 { get; set; }
    public short Data2 { get; set; }
    public short Data3 { get; set; }
    public FringeAttr? FringeAttr { get; set; }
}
