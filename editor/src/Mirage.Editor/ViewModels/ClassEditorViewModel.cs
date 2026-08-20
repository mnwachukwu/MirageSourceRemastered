using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Mirage.Editor.Localization;
using Mirage.Editor.Models;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>The class-list editor. Also derives the selectable sprite range from the loaded sprite
/// sheet, so the picker never offers a sprite the art doesn't contain.</summary>
public sealed partial class ClassEditorViewModel : EditorViewModelBase<ClassRowViewModel>
{
    [ObservableProperty] private ClassRowViewModel? _selectedClass;
    public override ClassRowViewModel? Selected => SelectedClass;
    protected override void SetSelected(ClassRowViewModel? row) => SelectedClass = row;
    public ObservableCollection<ClassRowViewModel> Classes { get; } = [];
    public override ObservableCollection<ClassRowViewModel> Items => Classes;
    /// <inheritdoc/>
    protected override string GetFilterText(ClassRowViewModel row) => row.DisplayName;
    [ObservableProperty] private Bitmap? _spriteBitmap;
    /// <summary>Selectable sprite indices, derived from the loaded sheet's height (one 32px row each),
    /// so the picker can't offer a sprite the art doesn't have.</summary>
    public IReadOnlyList<int> SpriteEntries { get; private set; } = [];
    partial void OnSpriteBitmapChanged(Bitmap? value)
    {
        int count = value is null ? 0 : (int)(value.Size.Height / 32) - 1;
        SpriteEntries = Enumerable.Range(0, Math.Max(0, count) + 1).ToArray();
        OnPropertyChanged(nameof(SpriteEntries));
    }

    public ClassEditorViewModel(EditorDataService data, EditorConnection conn) : base(data, conn)
    {
        HookItems();
        // Both loadout tables read the live item/spell lists, so an item's currency-ness or a spell's
        // magnitude flipping under a selected class has to re-raise them.
        _data.EntriesInvalidated += () => SelectedClass?.NotifyLoadoutDerived();
    }

    // ── Starting-loadout pickers ─────────────────────────────────────────────
    // Gate facts come from EditorDataService, which serves the LIVE world's when connected and the
    // offline records only when it isn't. That distinction matters: the editor's offline folder can be a
    // different world from the server's, so answering "can this class wear this?" from local files while
    // connected would validate against gear the server has never heard of.

    /// <summary>Spells THIS class could actually learn at level 1 — the spell picker is restrictive by
    /// construction rather than permissive-with-a-warning, because a spell a class cannot cast is never
    /// a legitimate authoring choice (an off-class potion or key genuinely can be).
    ///
    /// <para>Asks the same three gates character creation will: the class list, the level gate, and
    /// <see cref="CombatFormulas.GetSpellIntRequirement"/> against the class's base INT.</para></summary>
    private NamedEntry[] LearnableSpells(ClassRowViewModel cls)
    {
        var result = new List<NamedEntry>();
        foreach (var entry in _data.LiveSpellEntries)
        {
            if (entry.Id <= 0) continue;
            if (_data.SpellGate(entry.Id) is not { } g) continue;
            if (g.LevelReq > 1) continue;
            if (!ClassGate.Allows(g.AllowedClasses, cls.Index)) continue;
            // Mirrors CombatFormulas.GetSpellIntRequirement for a non-GiveItem spell: the magnitude IS
            // the raw gate, less the class-affinity head-start, floored at 1.
            int need = Math.Max(1, g.VitalAmount - CombatFormulas.ClassAffinityBonus(cls.Int));
            if (need > cls.Int) continue;
            result.Add(entry);
        }
        return [.. result];
    }

    private void AttachLoadoutProviders(ClassRowViewModel? row) =>
        row?.AttachProviders(() => _data.LiveItemEntries, _data.ItemGate, _data.SpellGate, LearnableSpells);

    partial void OnSelectedClassChanged(ClassRowViewModel? oldValue, ClassRowViewModel? newValue)
    {
        NotifyInboundRefsChanged();
        // Wire on selection rather than at construction: rows are built in bulk (and lazily for online
        // placeholders), and only the selected one ever shows its loadout tables.
        AttachLoadoutProviders(newValue);
    }

    protected override string TypeName => EditorStrings.Get(EditorStrings.ClassEditor_TypeName);
    protected override string TypeNamePlural => EditorStrings.Get(EditorStrings.ClassEditor_TypeNamePlural);
    /// <inheritdoc/>
    protected override int GetIndex(ClassRowViewModel vm) => vm.Index;
    /// <inheritdoc/>
    protected override bool GetIsDirty(ClassRowViewModel vm) => vm.IsDirty;
    /// <inheritdoc/>
    protected override void ClearDirtyState(ClassRowViewModel vm) => vm.ClearDirty();

    /// <summary>Pre-fill every placeholder row from one bulk server response, so browsing the list after
    /// connecting is instant instead of fetching per selection. No-op offline; canceled on disconnect.</summary>
    public async Task EagerLoadAllAsync(CancellationToken ct)
    {
        if (!_data.IsOnline) return;
        var bulk = await _conn.RequestAllClassesAsync(ct);
        if (bulk is null) return;
        foreach (var pkt in bulk.Classes)
        {
            var vm = Items.FirstOrDefault(v => v.Index == pkt.ClassNum);
            if (vm is not null) ApplyServerResponse(vm, pkt);
        }
        OnPropertyChanged(nameof(FilteredItems));
    }

    partial void OnSelectedClassChanged(ClassRowViewModel? value)
    {
        NotifyDirtyState();
        if (value is not null && !value.IsLoaded && _data.IsOnline)
            _ = LoadEntityAsync(value);
    }

    /// <summary>Rebuild the list from the on-disk records, fully populated — offline editing has no
    /// server to lazy-load from.</summary>
    public void LoadOffline()
    {
        SelectedClass = null;
        Classes.Clear();
        for (int i = 1; i < _data.OfflineClasses.Length; i++)
            Classes.Add(new ClassRowViewModel(i, _data.OfflineClasses[i]));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOffline,
            ("Count", Classes.Count), ("EntityType", TypeNamePlural));
    }

    /// <summary>Rebuild the list from the server's name index as NAME-ONLY placeholders
    /// (<c>isLoaded: false</c>). Each row's full definition arrives when it is selected, or sooner via
    /// <see cref="EagerLoadAllAsync"/>.</summary>
    public void LoadOnline()
    {
        if (_data.OnlineClasses is null) return;
        SelectedClass = null;
        Classes.Clear();
        foreach (var entry in _data.OnlineClasses)
            Classes.Add(new ClassRowViewModel(entry.Num, new ClassRecord { Name = entry.Name }, isLoaded: false));
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedOnline,
            ("Count", Classes.Count), ("EntityType", TypeNamePlural));
    }

    /// <inheritdoc/>
    protected override async Task<IPacket?> RequestFromServerAsync(ClassRowViewModel vm)
        => await _conn.RequestClassAsync(vm.Index);

    /// <inheritdoc/>
    protected override void ApplyServerResponse(ClassRowViewModel vm, IPacket pkt)
        => vm.ApplyPacket((UpdateClassPacket)pkt);

    /// <inheritdoc/>
    protected override IPacket BuildSavePacket(ClassRowViewModel vm) => vm.BuildSavePacket();

    /// <summary>Patch the cached online name index after a save, so the list caption reflects a renamed
    /// record without re-fetching the whole index.</summary>
    protected override void AfterSave(ClassRowViewModel vm)
    {
        if (_data.IsOnline) _data.PatchOnlineClassName(vm.Index, vm.Name);
    }

    /// <inheritdoc/>
    protected override Task SaveOfflineAsync(ClassRowViewModel vm)
        => _data.SaveOfflineClassAsync(vm.Index, vm.ToRecord());

    /// <inheritdoc/>
    protected override void LoadFromOfflineRecord(ClassRowViewModel vm)
        => vm.LoadFromRecord(_data.OfflineClasses[vm.Index]);
}
