using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Controls;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
namespace Mirage.Editor.ViewModels;

/// <summary>The undo/redo stacks and the batching that groups a drag-paint into one operation,
/// plus the per-tile snapshot/restore those operate on.</summary>
public sealed partial class MapEditorViewModel : ObservableObject
{
    // ── Selected attribute description (shown in attribute mode tools panel) ──
    // Each description opens with the attribute's name, which EditorVocabulary supplies in English
    // for every language; the explanation after it is translated.
    public string SelectedAttributeDescription => AttributeDescription(SelectedAttributeTool);

    private static string AttributeDescription(AttributeTool tool)
    {
        string key = tool switch
        {
            AttributeTool.Blocked => EditorStrings.MapEditor_AttrDesc_Blocked,
            AttributeTool.Warp => EditorStrings.MapEditor_AttrDesc_Warp,
            AttributeTool.Item => EditorStrings.MapEditor_AttrDesc_Item,
            AttributeTool.NpcAvoid => EditorStrings.MapEditor_AttrDesc_NpcAvoid,
            AttributeTool.Key => EditorStrings.MapEditor_AttrDesc_Key,
            AttributeTool.KeyOpen => EditorStrings.MapEditor_AttrDesc_KeyOpen,
            AttributeTool.NpcSpawn => EditorStrings.MapEditor_AttrDesc_NpcSpawn,
            AttributeTool.LayerRamp => EditorStrings.MapEditor_AttrDesc_LayerRamp,
            _ => "",
        };
        return key.Length == 0
            ? EditorVocabulary.NameOf(tool)
            : EditorStrings.Format(key, ("Name", EditorVocabulary.NameOf(tool)));
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────────

    // Two-layer world: the snapshot also carries the third visual stack (Canopy) and the fringe-layer attribute
    // sub-record (FringeAttr) so authoring either survives undo/redo.  FringeAttr is deep-copied (it is a
    // mutable ref type) so an undo entry can't be mutated by a later edit to the live tile.
    private sealed record TileSnapshot(int[] Ground, int[] Fringe, int[] Canopy, FringeAttr? Fa, TileType T, short D1, short D2, short D3);
    private static TileSnapshot Snap(TileRecord t) =>
        new((int[])t.Ground.Clone(), (int[])t.Fringe.Clone(), (int[])t.Canopy.Clone(), CloneFringeAttr(t.FringeAttr),
            t.Type, t.Data1, t.Data2, t.Data3);
    private static void Restore(TileRecord t, TileSnapshot s)
    {
        Array.Copy(s.Ground, t.Ground, Math.Min(s.Ground.Length, t.Ground.Length));
        Array.Copy(s.Fringe, t.Fringe, Math.Min(s.Fringe.Length, t.Fringe.Length));
        Array.Copy(s.Canopy, t.Canopy, Math.Min(s.Canopy.Length, t.Canopy.Length));
        t.FringeAttr = CloneFringeAttr(s.Fa);
        t.Type = s.T;
        t.Data1 = s.D1;
        t.Data2 = s.D2;
        t.Data3 = s.D3;
    }
    private static FringeAttr? CloneFringeAttr(FringeAttr? fa) =>
        fa is null ? null : new FringeAttr { Type = fa.Type, Data1 = fa.Data1, Data2 = fa.Data2, Data3 = fa.Data3 };

    // An undo entry: a tile change (Tile/Attribute modes) OR a placed-light change (Light Sources mode,
    // at most one light per tile). Both carry the (x,y) they touch so undo can invalidate that cell.
    private abstract record UndoOp(int X, int Y);
    private sealed record TileOp(int X, int Y, TileSnapshot Before, TileSnapshot After) : UndoOp(X, Y);
    private sealed record LightOp(int X, int Y, PlacedLight? Before, PlacedLight? After) : UndoOp(X, Y);
    // A fixed NPC-spawn pin change (Attribute mode, NpcSpawn tool; at most one pin per tile). Before/After = the
    // index of the Npcs entry pinned at (x,y), or null for none. Symmetric with LightOp — a per-(x,y) op fully
    // captures the change; a row removal shifts every later index, so RemoveNpcRow fixes up these ops in place.
    private sealed record NpcSpawnOp(int X, int Y, WorldLayer Layer, int? Before, int? After) : UndoOp(X, Y);

    // Batch accumulates changes during a single pointer press/drag.
    private readonly List<UndoOp> _batch = [];
    private bool _batchOpen;

    public void BeginBatch()
    {
        _batch.Clear();
        _batchOpen = true;
    }

    public void CommitBatch()
    {
        _batchOpen = false;
        if (_batch.Count == 0) return;
        _undoStack.Push([.. _batch]);
        _redoStack.Clear();
        _batch.Clear();
        UpdateUndoRedo();
    }

    // Records a tile change.  If a batch is open it's accumulated; otherwise pushed immediately.
    private void Record(int x, int y, TileSnapshot before, TileSnapshot after)
    {
        if (before.Equals(after)) return;
        PushOp(new TileOp(x, y, before, after));
    }

    // Records a placed-light change (before/after = the light at (x,y); null = no light there).
    private void RecordLight(int x, int y, PlacedLight? before, PlacedLight? after)
    {
        if (before == after) return;
        PushOp(new LightOp(x, y, before, after));
    }

    // Records a fixed NPC-spawn pin change (before/after = the entry index pinned at (x,y); null = no pin there).
    private void RecordNpcSpawn(int x, int y, WorldLayer layer, int? before, int? after)
    {
        if (before == after) return;
        PushOp(new NpcSpawnOp(x, y, layer, before, after));
    }

    private void PushOp(UndoOp op)
    {
        if (_batchOpen)
        {
            _batch.Add(op);
        }
        else
        {
            _undoStack.Push([op]);
            _redoStack.Clear();
            UpdateUndoRedo();
        }
    }

    private readonly Stack<List<UndoOp>> _undoStack = new();
    private readonly Stack<List<UndoOp>> _redoStack = new();

    private void UpdateUndoRedo()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
