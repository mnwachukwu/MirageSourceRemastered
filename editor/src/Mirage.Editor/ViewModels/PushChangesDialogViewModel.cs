using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The unsaved-changes prompt shown before any transition that would discard edits — connecting,
/// reconnecting, disconnecting, or closing the window. Lists every dirty row across all editors and
/// offers to commit them first.
/// <para>Which of the four situations applies is carried by the constructor flags and only changes
/// the wording (<see cref="MessageText"/>, <see cref="SaveButtonText"/>,
/// <see cref="ProceedButtonText"/>) plus whether committing writes to disk or to the server.</para>
/// <para>The caller drives the outcome through <see cref="ProceedConfirmed"/> and
/// <see cref="Canceled"/>; this view-model never closes the dialog itself.</para>
/// </summary>
public sealed partial class PushChangesDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _dirtyItems;
    private readonly EditorConnection _conn;
    private readonly EditorDataService _data;
    private readonly bool _isConnecting;
    private readonly bool _isClosing;
    private readonly bool _isReconnecting;
    /// <summary>Whether the prompt was raised by a dropped connection rather than a user action.</summary>
    public bool IsReconnecting => _isReconnecting;

    [ObservableProperty] private string _statusMessage = "";
    /// <summary>True while a commit is in flight, so the buttons can disable.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Human-readable "type index: name" line for each dirty row, for the dialog's list.</summary>
    public ObservableCollection<string> DirtyNames { get; } = [];

    /// <summary>Prompt body, worded for the situation that raised the dialog.</summary>
    public string MessageText => _isClosing
        ? EditorStrings.Get(EditorStrings.PushChangesDialog_UnsavedOnClose)
        : _isReconnecting
            ? EditorStrings.Get(EditorStrings.PushChangesDialog_UnsavedPush)
            : _isConnecting
                ? EditorStrings.Get(EditorStrings.PushChangesDialog_UnsavedConnect)
                : EditorStrings.Get(EditorStrings.PushChangesDialog_UnsavedOnline);

    /// <summary>Caption for the commit-then-continue button.</summary>
    public string SaveButtonText => _isClosing ? EditorStrings.Get(EditorStrings.PushChangesDialog_SaveAndClose) : _isReconnecting ? EditorStrings.Get(EditorStrings.PushChangesDialog_PushAndContinue) : _isConnecting ? EditorStrings.Get(EditorStrings.PushChangesDialog_SaveAndConnect) : EditorStrings.Get(EditorStrings.PushChangesDialog_PushAndDisconnect);
    /// <summary>Caption for the discard-then-continue button.</summary>
    public string ProceedButtonText => _isClosing ? EditorStrings.Get(EditorStrings.PushChangesDialog_DiscardAndClose) : _isReconnecting ? EditorStrings.Get(EditorStrings.PushChangesDialog_DiscardAndContinue) : _isConnecting ? EditorStrings.Get(EditorStrings.PushChangesDialog_DiscardAndConnect) : EditorStrings.Get(EditorStrings.PushChangesDialog_DiscardAndDisconnect);

    /// <summary>Raised when the author chose to go ahead, whether they committed or discarded.</summary>
    public event Action? ProceedConfirmed;
    /// <summary>Raised when the author backed out; the caller should abandon the transition.</summary>
    public event Action? Canceled;

    /// <summary>Alias of <see cref="ProceedConfirmed"/> for the disconnect call sites.</summary>
    public event Action? DisconnectConfirmed
    {
        add => ProceedConfirmed += value;
        remove => ProceedConfirmed -= value;
    }

    public PushChangesDialogViewModel(
        IReadOnlyList<object> dirtyItems,
        EditorConnection conn,
        EditorDataService data,
        bool isConnecting = false,
        bool isClosing = false,
        bool isReconnecting = false)
    {
        _dirtyItems = dirtyItems;
        _conn = conn;
        _data = data;
        _isClosing = isClosing;
        _isReconnecting = isReconnecting;
        // Closing while offline commits to disk, which is exactly the connecting path's behavior.
        _isConnecting = isConnecting || (isClosing && !data.IsOnline);

        // The dirty set arrives as a flat object list (it spans every editor), so each row type is
        // matched back to its own caption format here.
        foreach (var item in dirtyItems)
        {
            DirtyNames.Add(item switch
            {
                ItemRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyItem, ("Index", vm.Index), ("Name", vm.Name)),
                NpcRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyNpc, ("Index", vm.Index), ("Name", vm.Name)),
                ShopRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyShop, ("Index", vm.Index), ("Name", vm.Name)),
                QuestRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyQuest, ("Index", vm.Index), ("Name", vm.Name)),
                ConversationRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyConversation, ("Index", vm.Index), ("Name", vm.Name)),
                SpellRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtySpell, ("Index", vm.Index), ("Name", vm.Name)),
                MapRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyMap, ("Index", vm.Index), ("Name", vm.Record.Name)),
                MapGroupRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyMapGroup, ("Index", vm.Index), ("Name", vm.Name)),
                ClassRowViewModel vm => EditorStrings.Format(EditorStrings.PushChangesDialog_DirtyClass, ("Index", vm.Index), ("Name", vm.Name)),
                _ => item.ToString() ?? EditorStrings.Get(EditorStrings.PushChangesDialog_DirtyUnknown),
            });
        }
    }

    /// <summary>Commit every dirty row, then raise <see cref="ProceedConfirmed"/>. Offline rows go
    /// straight to disk; online rows are sent as editor-save packets. A failure leaves the status
    /// line set and does NOT raise the event, so the transition is abandoned.</summary>
    // ONE case per row type, each carrying both modes, so a type cannot be handled offline and forgotten
    // online — a missing arm skips the row silently AND leaves it dirty (ClearDirty is never reached).
    // Neither arm restates a field mapping: offline goes through the row's ToRecord, online through its
    // BuildSavePacket — the very projections the per-type editors save with, so this path can't drift
    // from them and drop fields the normal save writes.
    [RelayCommand]
    private async Task SaveAndProceedAsync()
    {
        IsBusy = true;
        StatusMessage = _isConnecting ? EditorStrings.Get(EditorStrings.PushChangesDialog_Saving) : EditorStrings.Get(EditorStrings.PushChangesDialog_Pushing);
        try
        {
            foreach (var item in _dirtyItems)
            {
                switch (item)
                {
                    case ItemRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineItemAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case NpcRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineNpcAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ShopRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineShopAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case QuestRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineQuestAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ConversationRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineConversationAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case SpellRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineSpellAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case MapRowViewModel vm:
                        // Bump before either save — the server ignores the packet's Revision and does its
                        // own bump; the local bump is a UI mirror (see MapRowViewModel.BumpRevision).
                        vm.BumpRevision();
                        if (_isConnecting)
                        {
                            await _data.SaveOfflineMapAsync(vm.Index, vm.Record);
                        }
                        else
                        {
                            await _conn.SendSaveAsync(
                            EditorDataService.BuildSaveMapPacket(vm.Index, vm.Record));
                        }

                        vm.ClearDirty();
                        break;
                    case MapGroupRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineMapGroupAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                    case ClassRowViewModel vm:
                        if (_isConnecting) await _data.SaveOfflineClassAsync(vm.Index, vm.ToRecord());
                        else await _conn.SendSaveAsync(vm.BuildSavePacket());
                        vm.ClearDirty();
                        break;
                }
            }
            ProceedConfirmed?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = EditorStrings.Format(EditorStrings.PushChangesDialog_Error, ("Error", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DiscardAndProceed() => ProceedConfirmed?.Invoke();

    [RelayCommand]
    private void Cancel() => Canceled?.Invoke();
}
