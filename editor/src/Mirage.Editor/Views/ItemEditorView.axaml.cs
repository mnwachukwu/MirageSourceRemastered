using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits item definitions, including the type-dependent data slots and the restriction flags.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class ItemEditorView : LocalizedUserControl
{
    public ItemEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _typeFilterCombo.PlaceholderText = EditorStrings.Get(EditorStrings.ItemEditor_AllTypesFilter);
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_FilterByName);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.ItemEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.ItemEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _picLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_PicLabel);
        _typeLabel.Text = EditorStrings.Get(EditorStrings.Common_TypeLabel);
        _restrictionsLabel.Text = EditorStrings.Get(EditorStrings.ItemEditor_RestrictionsLabel);
        _nonTradeableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonTradeable);
        _nonListableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonListable);
        _nonMailableCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_NonMailable);
        _destroyOnDropCheck.Content = EditorStrings.Get(EditorStrings.ItemEditor_DestroyOnDrop);
        _fieldNotesHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_FieldNotesHeader);

        _spellPicker.PlaceholderText = EditorStrings.Get(EditorStrings.ItemEditor_SpellSearchPlaceholder);
        _classPicker.PlaceholderText = EditorStrings.Get(EditorStrings.ItemEditor_ClassSearchPlaceholder);

        _notesEquipmentHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentHeader);
        _notesEquipmentDurability.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentDurability);
        _notesEquipmentDamageDef.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentDamageDef);
        _notesEquipmentWeapon.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentWeapon);
        _notesEquipmentArmor.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentArmor);
        _notesEquipmentHelmet.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentHelmet);
        _notesEquipmentShield.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentShield);
        _notesEquipmentClassReq.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentClassReq);
        _notesEquipmentShieldSide.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_EquipmentShieldSide);

        _notesPotionsHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_PotionsHeader);
        _notesPotionsData1.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_PotionsData1);

        _notesSpellScrollHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_SpellScrollHeader);
        _notesSpellScrollData1.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_SpellScrollData1);

        _notesKeyHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_KeyHeader);
        _notesKeyData1.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_KeyData1);

        _notesCurrencyHeader.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_CurrencyHeader);
        _notesCurrencyDesc.Text = EditorStrings.Get(EditorStrings.ItemEditor_Notes_CurrencyDesc);

        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.ItemEditor_SaveItemButton);
        _saveAllBtn.Content = EditorStrings.Get(EditorStrings.Common_SaveAll);
    }

    /// <summary>Persist the panel layout as the view leaves the tree, so switching sections
    /// keeps the splitter position.</summary>
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        SavePanelState();
        AppSettings.Current.Save();
    }

    /// <summary>Restore the saved splitter width once the visual tree exists.</summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.ItemEditorLeftWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.ItemEditorLeftWidth = LeftPanel.Bounds.Width;
    }
}
