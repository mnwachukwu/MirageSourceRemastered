using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared.Protocol;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// Shared behavior for every record-list editor (items, NPCs, spells, shops, classes, quests, …):
/// text filtering, per-row dirty tracking, and the Save / Save All / Discard / Discard All commands.
/// <para>A subclass supplies only the type-specific pieces — how to index a row, how to talk to the
/// server, and how to read and write the offline record — and inherits the rest. Every save routes
/// through one path that picks the online or offline branch from <see cref="EditorDataService.IsOnline"/>,
/// so the two modes can't drift.</para>
/// <para>Constructors must call <see cref="HookItems"/> after <c>base()</c>, once
/// <see cref="Items"/> exists, or dirty state and the filter will not track row changes.</para>
/// </summary>
/// <typeparam name="TRow">The per-record row view-model type; must raise <c>PropertyChanged</c>
/// for <c>"IsDirty"</c> so the aggregate dirty flags stay live.</typeparam>
public abstract partial class EditorViewModelBase<TRow> : ObservableObject
    where TRow : class, INotifyPropertyChanged
{
    protected readonly EditorDataService _data;
    protected readonly EditorConnection _conn;

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;
    /// <summary>Case-insensitive substring filter over <see cref="GetFilterText"/>; empty shows everything.</summary>
    [ObservableProperty] private string _filterText = "";
    partial void OnFilterTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
        OnPropertyChanged(nameof(IsFilterActive));
    }

    // Rows currently subscribed to PropertyChanged, tracked so they can be detached again — a
    // CollectionChanged Reset reports no OldItems, so without this list those handlers would leak.
    private readonly List<TRow> _subscribedRows = [];

    protected EditorViewModelBase(EditorDataService data, EditorConnection conn)
    {
        _data = data;
        _conn = conn;
        // One subscription here covers every record-list editor and its rows. Each row's
        // DisplayName is a COMPUTED property that falls back to a localized "(empty)" placeholder,
        // so re-raising the list is enough to make the bindings re-read them — no per-row plumbing.
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Re-read everything on this view-model that resolves a localized string: the row
    /// labels (via the list) and the "N of M results" caption. Status messages are left alone —
    /// they are a record of something that already happened, in the language it happened in.</summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilterStatus));
    }

    /// <summary>The row the editor pane is bound to, or null when nothing is selected.</summary>
    public abstract TRow? Selected { get; }

    // ── What points at the selected record ───────────────────────────────────
    // Every reference in this data model runs one way: the child names the parent, and no record carries a
    // list of its dependents. So the answer is a scan of the OTHER collections, which only
    // MainWindowViewModel can reach — it owns every editor. It supplies the scan here.

    /// <summary>Supplies the records that point at the given record number, grouped by relationship. Assigned
    /// by <see cref="MainWindowViewModel"/>; null in a test or before wiring, which reads as "no references".</summary>
    public Func<int, IReadOnlyList<ReferenceGroupViewModel>>? ResolveInboundRefs { get; set; }

    /// <summary>What refers to the selected record. Recomputed on demand rather than cached: it is a scan over
    /// collections this editor does not own, and a cache would need invalidating on every edit anywhere.</summary>
    public IReadOnlyList<ReferenceGroupViewModel> InboundRefs =>
        Selected is { } row && ResolveInboundRefs is { } resolve ? resolve(GetIndex(row)) : [];

    /// <summary>Whether anything refers to the selected record, so the panel can say "nothing" rather than
    /// showing an empty heading.</summary>
    public bool HasInboundRefs => InboundRefs.Count > 0;

    /// <summary>Re-read <see cref="InboundRefs"/>. The referring records live in other editors, so this one
    /// cannot see them change: the eager load that fills them finishes long after the list is built, and an
    /// edit elsewhere can add or remove a reference entirely behind this editor's back.</summary>
    public void NotifyInboundRefsChanged()
    {
        OnPropertyChanged(nameof(InboundRefs));
        OnPropertyChanged(nameof(HasInboundRefs));
    }
    /// <summary>Assign the selection. The backing property is named per editor (SelectedItem, SelectedNpc),
    /// so the base cannot set it directly.</summary>
    protected abstract void SetSelected(TRow? row);

    /// <summary>Select the row with this record number, as an ordinary selection so it runs the editor's
    /// normal lazy-fetch and dirty-tracking path. False when no row has that number, letting a caller leave
    /// the current section alone rather than switching to a pane showing the wrong record.</summary>
    public bool TrySelect(int index)
    {
        var row = Items.FirstOrDefault(r => GetIndex(r) == index);
        if (row is null) return false;
        SetSelected(row);
        return true;
    }

    /// <summary>Singular display name of the record type ("Item", "NPC"), used in status messages.</summary>
    protected abstract string TypeName { get; }
    /// <summary>Plural display name; defaults to <see cref="TypeName"/> + "s". Override for irregular plurals.</summary>
    protected virtual string TypeNamePlural => TypeName + "s";
    /// <summary>The record's 1-based slot number, for status messages and save packets.</summary>
    protected abstract int GetIndex(TRow vm);
    /// <summary>Ask the server for this record's current definition; null if the request yielded nothing.</summary>
    protected abstract Task<IPacket?> RequestFromServerAsync(TRow vm);
    /// <summary>Copy a server response returned by <see cref="RequestFromServerAsync"/> into the row.</summary>
    protected abstract void ApplyServerResponse(TRow vm, IPacket pkt);
    /// <summary>Build the save packet sent when the editor is connected to a server.</summary>
    protected abstract IPacket BuildSavePacket(TRow vm);
    /// <summary>Persist the row straight to disk, for offline editing.</summary>
    protected abstract Task SaveOfflineAsync(TRow vm);
    /// <summary>Re-read the row from the on-disk record, discarding unsaved edits (offline discard).</summary>
    protected abstract void LoadFromOfflineRecord(TRow vm);
    /// <summary>Whether the row holds unsaved edits.</summary>
    protected abstract bool GetIsDirty(TRow vm);
    /// <summary>Mark the row clean after a successful save or discard.</summary>
    protected abstract void ClearDirtyState(TRow vm);
    /// <summary>Every row of this record type, in slot order.</summary>
    public abstract ObservableCollection<TRow> Items { get; }
    /// <summary>The text <see cref="FilterText"/> is matched against for one row.</summary>
    protected abstract string GetFilterText(TRow row);
    /// <summary>Whether a row survives the current filter. Override to match on more than the row's text.</summary>
    protected virtual bool MatchesFilter(TRow row) =>
        string.IsNullOrEmpty(FilterText) || GetFilterText(row).Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    /// <summary>The rows the list should display, after <see cref="MatchesFilter"/>.</summary>
    public IEnumerable<TRow> FilteredItems => Items.Where(MatchesFilter);
    /// <summary>Whether a filter is narrowing the list (drives the clear-filter affordance).</summary>
    public virtual bool IsFilterActive => !string.IsNullOrEmpty(FilterText);
    /// <summary>"showing N of M" caption beneath the list.</summary>
    public string FilterStatus => EditorStrings.Format(EditorStrings.Status_FilterCount,
        ("Filtered", FilteredItems.Count()), ("Total", Items.Count));
    [RelayCommand] private void ClearFilter() => FilterText = "";

    /// <summary>Every row holding unsaved edits — used by the quit-time push/save prompt.</summary>
    public IEnumerable<TRow> GetDirty() => Items.Where(GetIsDirty);

    /// <summary>Write every dirty row straight to disk, bypassing the online path entirely.
    /// Used when saving a whole offline session at once.</summary>
    public async Task SaveAllOfflineAsync()
    {
        foreach (var vm in Items.Where(GetIsDirty).ToList())
        {
            await SaveOfflineAsync(vm);
            ClearDirtyState(vm);
        }
        NotifyDirtyState();
    }

    // ── Dirty-state computed properties ───────────────────────────────────────
    /// <summary>Whether the selected row has unsaved edits (enables Save / Discard).</summary>
    public bool IsSelectedDirty => Selected is not null && GetIsDirty(Selected);
    /// <summary>Whether any row has unsaved edits (enables Save All / Discard All).</summary>
    public bool HasAnyDirty => Items.Any(GetIsDirty);

    /// <summary>Re-raise the aggregate dirty flags. Call after anything that can change a row's
    /// dirty state, since neither flag is a stored value.</summary>
    protected void NotifyDirtyState()
    {
        OnPropertyChanged(nameof(IsSelectedDirty));
        OnPropertyChanged(nameof(HasAnyDirty));
    }

    /// <summary>Subscribe to <see cref="Items"/> so dirty state and the filtered view follow row
    /// additions, removals, and edits. Call from each concrete constructor after <c>base()</c>, once
    /// <see cref="Items"/> is ready — the base constructor can't do it, since the collection is a
    /// subclass member that does not exist yet at that point.</summary>
    protected void HookItems()
    {
        Items.CollectionChanged += (_, e) =>
        {
            // A Reset carries no OldItems, so detach via the tracked list or the handlers leak.
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _subscribedRows)
                    row.PropertyChanged -= OnRowPropertyChanged;
                _subscribedRows.Clear();
                NotifyDirtyState();
                OnPropertyChanged(nameof(FilteredItems));
                OnPropertyChanged(nameof(FilterStatus));
                return;
            }
            if (e.NewItems is not null)
            {
                foreach (TRow row in e.NewItems.Cast<TRow>())
                {
                    row.PropertyChanged += OnRowPropertyChanged;
                    _subscribedRows.Add(row);
                }
            }

            if (e.OldItems is not null)
            {
                foreach (TRow row in e.OldItems.Cast<TRow>())
                {
                    row.PropertyChanged -= OnRowPropertyChanged;
                    _subscribedRows.Remove(row);
                }
            }

            NotifyDirtyState();
            OnPropertyChanged(nameof(FilteredItems));
            OnPropertyChanged(nameof(FilterStatus));
        };
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsDirty")
            NotifyDirtyState();
    }

    // ── Load / Save / Discard ─────────────────────────────────────────────────

    /// <summary>Fetch one record from the server into <paramref name="vm"/>, driving
    /// <see cref="IsLoading"/> and <see cref="StatusMessage"/>. Failures are reported in the status
    /// line rather than thrown, so a dropped connection can't tear down the editor.</summary>
    protected async Task LoadEntityAsync(TRow vm)
    {
        IsLoading = true;
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadingEntity,
            ("EntityType", TypeName), ("Index", GetIndex(vm)));
        try
        {
            var pkt = await RequestFromServerAsync(vm);
            if (pkt is not null)
            {
                ApplyServerResponse(vm, pkt);
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadedEntity,
                    ("EntityType", TypeName), ("Index", GetIndex(vm)));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_LoadFailed,
                ("EntityType", TypeName), ("Index", GetIndex(vm)), ("Error", ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Hook for work a subclass needs after a row saves successfully (refreshing a
    /// dependent list, re-sorting). Does nothing by default.</summary>
    protected virtual void AfterSave(TRow vm) { }

    // The single save path: online sends a packet, offline writes the file. Every command below
    // funnels through here so the two modes can't diverge.
    private async Task SaveOneAsync(TRow vm)
    {
        if (_data.IsOnline)
            await _conn.SendSaveAsync(BuildSavePacket(vm));
        else
            await SaveOfflineAsync(vm);
        ClearDirtyState(vm);
        NotifyDirtyState();
        AfterSave(vm);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Selected is null || !GetIsDirty(Selected)) return;
        var vm = Selected;
        try
        {
            await SaveOneAsync(vm);
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_Saved,
                ("EntityType", TypeName), ("Index", GetIndex(vm)));
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_SaveFailed,
                ("Error", ex.Message));
        }
    }

    // Stops at the first failure rather than pressing on, so a server-side rejection can't leave
    // half the batch saved without the author noticing.
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        int saved = 0;
        foreach (var vm in Items.Where(GetIsDirty))
        {
            try
            {
                await SaveOneAsync(vm);
                saved++;
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_SaveFailed,
                    ("Error", $"{TypeName} {GetIndex(vm)}: {ex.Message}"));
                return;
            }
        }
        StatusMessage = saved > 0
            ? EditorStrings.Format(EditorStrings.EntityEditor_SaveAllSaved,
                ("Count", saved), ("EntityTypePlural", TypeNamePlural))
            : EditorStrings.Format(EditorStrings.EntityEditor_NoDirty,
                ("EntityTypePlural", TypeNamePlural));
    }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        if (Selected is null || !GetIsDirty(Selected)) return;
        var vm = Selected;
        if (_data.IsOnline)
            await LoadEntityAsync(vm);
        else
            LoadFromOfflineRecord(vm);
        ClearDirtyState(vm);
        NotifyDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_Discarded,
            ("EntityType", TypeName), ("Index", GetIndex(vm)));
    }

    [RelayCommand]
    private async Task DiscardAllAsync()
    {
        foreach (var vm in Items.Where(GetIsDirty).ToList())
        {
            try
            {
                if (_data.IsOnline)
                    await LoadEntityAsync(vm);
                else
                    LoadFromOfflineRecord(vm);
                ClearDirtyState(vm);
            }
            catch (Exception ex)
            {
                StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_DiscardFailed,
                    ("EntityType", TypeName), ("Index", GetIndex(vm)), ("Error", ex.Message));
                return;
            }
        }
        NotifyDirtyState();
        StatusMessage = EditorStrings.Format(EditorStrings.EntityEditor_AllDiscarded,
            ("EntityTypePlural", TypeNamePlural));
    }
}
