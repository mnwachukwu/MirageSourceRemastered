using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Mirage.Editor.Localization;

namespace Mirage.Editor.Views;

/// <summary>Code-behind for the editor pane that edits NPC conversation trees — nodes and their player choices.
/// Localizes the captions (they are assigned in code rather than bound) and persists the
/// splitter width across sessions.</summary>
public partial class ConversationEditorView : LocalizedUserControl
{
    public ConversationEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ApplyStrings();
    }

    protected override void ApplyStrings()
    {
        _filterTextBox.PlaceholderText = EditorStrings.Get(EditorStrings.Common_Filter);
        _selectPrompt.Text = EditorStrings.Get(EditorStrings.ConversationEditor_SelectPrompt);
        _sectionTitle.Text = EditorStrings.Get(EditorStrings.ConversationEditor_SectionTitle);
        _nameLabel.Text = EditorStrings.Get(EditorStrings.Common_NameLabel);
        _speakerLabel.Text = EditorStrings.Get(EditorStrings.ConversationEditor_SpeakerLabel);
        _rootLabel.Text = EditorStrings.Get(EditorStrings.ConversationEditor_RootLabel);
        _nodesHeader.Text = EditorStrings.Get(EditorStrings.ConversationEditor_NodesHeader);
        _noNodesHint.Text = EditorStrings.Get(EditorStrings.Common_NoRowsHint);
        _addNodeBtn.Content = EditorStrings.Get(EditorStrings.Common_AddRow);
        _discardBtn.Content = EditorStrings.Get(EditorStrings.Common_Discard);
        _discardAllBtn.Content = EditorStrings.Get(EditorStrings.Common_DiscardAll);
        _saveBtn.Content = EditorStrings.Get(EditorStrings.ConversationEditor_SaveButton);
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
        PanelGrid.ColumnDefinitions[0].Width = new GridLength(AppSettings.Current.ConversationEditorLeftWidth);
    }

    /// <summary>Save the splitter width. Guards on a non-zero width so a never-shown view
    /// cannot persist a collapsed layout.</summary>
    internal void SavePanelState()
    {
        if (LeftPanel.Bounds.Width > 0)
            AppSettings.Current.ConversationEditorLeftWidth = LeftPanel.Bounds.Width;
    }
}
