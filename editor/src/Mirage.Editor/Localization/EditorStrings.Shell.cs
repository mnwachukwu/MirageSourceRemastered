using Mirage.Shared.Localization;

namespace Mirage.Editor.Localization;

/// <summary>The application shell: main window, connect/disconnect and push-changes dialogs,
/// shared data-field labels, the tile palette, and the help dialog.</summary>
public static partial class EditorStrings
{
    public const string LanguageName = nameof(LanguageName);

    // ── Common ────────────────────────────────────────────────────────────────
    // Shared across editors and dialogs — single source of truth for these values.
    public const string Common_NameLabel = nameof(Common_NameLabel);          // "Name:"
    public const string Common_DisplayNameLabel = nameof(Common_DisplayNameLabel);   // "Display Name:"
    public const string Common_TypeLabel = nameof(Common_TypeLabel);          // "Type:"
    public const string Common_Cancel = nameof(Common_Cancel);             // "Cancel"
    public const string Common_Close = nameof(Common_Close);              // "Close"
    public const string Common_Confirm = nameof(Common_Confirm);            // "Confirm"
    public const string Common_Connect = nameof(Common_Connect);            // "Connect"
    public const string Common_Discard = nameof(Common_Discard);            // "Discard"
    public const string Common_DiscardAll = nameof(Common_DiscardAll);         // "Discard All"
    public const string Common_SaveAll = nameof(Common_SaveAll);            // "Save All"
    public const string Common_Copy = nameof(Common_Copy);                  // "Copy"
    public const string Common_CopyTooltip = nameof(Common_CopyTooltip);
    public const string Common_CopyNeedsSelection = nameof(Common_CopyNeedsSelection);
    public const string Common_CopyNeedsRecord = nameof(Common_CopyNeedsRecord);

    public const string Common_Notes = nameof(Common_Notes);              // "Notes"
    public const string Common_RetainOnAltClick = nameof(Common_RetainOnAltClick);   // "Retain values for Alt+Click"
    public const string Common_FilterByName = nameof(Common_FilterByName);       // "Filter by name…"
    public const string Common_Filter = nameof(Common_Filter);             // "Filter…"
    public const string Common_PhysDmgAbbrev = nameof(Common_PhysDmgAbbrev);      // "P-DMG"
    public const string Common_MagDmgAbbrev = nameof(Common_MagDmgAbbrev);       // "M-DMG"
    public const string Common_MitAbbrev = nameof(Common_MitAbbrev);          // "MIT" (one universal axis)
    public const string Common_EmptyName = nameof(Common_EmptyName);          // "(empty)"
    public const string Common_Inherit = nameof(Common_Inherit);            // "(Inherit)"
    public const string Common_AddRow = nameof(Common_AddRow);             // "+ Add row" (dynamic table add button)
    public const string Common_NoRowsHint = nameof(Common_NoRowsHint);         // "No rows yet - click + to add." (empty-table hint)

    // ── Auto-save ─────────────────────────────────────────────────────────────
    public const string AutoSave_Menu = nameof(AutoSave_Menu);
    public const string AutoSave_ConfigureItem = nameof(AutoSave_ConfigureItem);
    public const string AutoSave_DisabledOnline = nameof(AutoSave_DisabledOnline);
    public const string AutoSave_DialogTitle = nameof(AutoSave_DialogTitle);
    public const string AutoSave_DialogIntro = nameof(AutoSave_DialogIntro);
    public const string AutoSave_OfflineOnlyNotice = nameof(AutoSave_OfflineOnlyNotice);
    public const string AutoSave_ColumnEditor = nameof(AutoSave_ColumnEditor);
    public const string AutoSave_ColumnEnabled = nameof(AutoSave_ColumnEnabled);
    public const string AutoSave_ColumnInterval = nameof(AutoSave_ColumnInterval);
    public const string AutoSave_ColumnReach = nameof(AutoSave_ColumnReach);
    public const string AutoSave_ReachOpenRecord = nameof(AutoSave_ReachOpenRecord);
    public const string AutoSave_ReachAllDirty = nameof(AutoSave_ReachAllDirty);
    public const string AutoSave_IntervalMinutes = nameof(AutoSave_IntervalMinutes);
    public const string AutoSave_Saved = nameof(AutoSave_Saved);
    public const string AutoSave_RecordCount = nameof(AutoSave_RecordCount);
    public const string AutoSave_Failed = nameof(AutoSave_Failed);

    // ── MainWindow ────────────────────────────────────────────────────────────
    public const string MainWindow_Title = nameof(MainWindow_Title);
    public const string MainWindow_LanguageMenu = nameof(MainWindow_LanguageMenu);
    public const string MainWindow_HelpMenu = nameof(MainWindow_HelpMenu);
    public const string MainWindow_HelpMapEditor = nameof(MainWindow_HelpMapEditor);
    public const string MainWindow_HelpAbout = nameof(MainWindow_HelpAbout);
    public const string About_Title = nameof(About_Title);
    public const string About_Version = nameof(About_Version);
    public const string About_CreatorDeveloper = nameof(About_CreatorDeveloper);
    public const string MainWindow_ExportMenu = nameof(MainWindow_ExportMenu);
    public const string MainWindow_DisconnectButton = nameof(MainWindow_DisconnectButton);
    public const string MainWindow_Section_Maps = nameof(MainWindow_Section_Maps);
    public const string MainWindow_Section_MapGroups = nameof(MainWindow_Section_MapGroups);
    public const string MainWindow_Section_Items = nameof(MainWindow_Section_Items);
    public const string MainWindow_Section_Npcs = nameof(MainWindow_Section_Npcs);
    public const string MainWindow_Section_Shops = nameof(MainWindow_Section_Shops);
    public const string MainWindow_Section_Spells = nameof(MainWindow_Section_Spells);
    public const string MainWindow_Section_Classes = nameof(MainWindow_Section_Classes);
    public const string MainWindow_Section_Quests = nameof(MainWindow_Section_Quests);
    public const string MainWindow_Section_Conversations = nameof(MainWindow_Section_Conversations);
    public const string MainWindow_Section_Accounts = nameof(MainWindow_Section_Accounts);

    // ── AccountEditor (Creator only, online only) ─────────────────────────────
    public const string AccountEditor_SearchPlaceholder = nameof(AccountEditor_SearchPlaceholder);
    public const string AccountEditor_OfflineNotice = nameof(AccountEditor_OfflineNotice);
    public const string AccountEditor_SelectPrompt = nameof(AccountEditor_SelectPrompt);
    public const string AccountEditor_AccessLabel = nameof(AccountEditor_AccessLabel);
    public const string AccountEditor_GuildLabel = nameof(AccountEditor_GuildLabel);
    public const string AccountEditor_GuildFormat = nameof(AccountEditor_GuildFormat);
    public const string AccountEditor_NoGuild = nameof(AccountEditor_NoGuild);
    public const string AccountEditor_CharactersHeader = nameof(AccountEditor_CharactersHeader);
    public const string AccountEditor_NoCharacters = nameof(AccountEditor_NoCharacters);
    public const string AccountEditor_PageOf = nameof(AccountEditor_PageOf);
    public const string AccountEditor_PrevPage = nameof(AccountEditor_PrevPage);
    public const string AccountEditor_NextPage = nameof(AccountEditor_NextPage);
    public const string AccountEditor_Reload = nameof(AccountEditor_Reload);
    public const string AccountEditor_Save = nameof(AccountEditor_Save);
    public const string AccountEditor_Saved = nameof(AccountEditor_Saved);
    public const string AccountEditor_SelfAccessHint = nameof(AccountEditor_SelfAccessHint);
    public const string AccountEditor_AnyAccess = nameof(AccountEditor_AnyAccess);
    public const string AccountEditor_StatBudget = nameof(AccountEditor_StatBudget);
    public const string AccountEditor_StatBudgetOver = nameof(AccountEditor_StatBudgetOver);
    public const string AccountEditor_SaveBlockedBudget = nameof(AccountEditor_SaveBlockedBudget);
    public const string AccountEditor_Rename = nameof(AccountEditor_Rename);
    public const string AccountEditor_RenamePlaceholder = nameof(AccountEditor_RenamePlaceholder);
    public const string AccountEditor_BagHeader = nameof(AccountEditor_BagHeader);
    public const string AccountEditor_BagEmpty = nameof(AccountEditor_BagEmpty);
    public const string AccountEditor_Give = nameof(AccountEditor_Give);
    public const string AccountEditor_Take = nameof(AccountEditor_Take);
    public const string AccountEditor_ItemPlaceholder = nameof(AccountEditor_ItemPlaceholder);
    public const string AccountEditor_Worn = nameof(AccountEditor_Worn);
    public const string AccountEditor_BookHeader = nameof(AccountEditor_BookHeader);
    public const string AccountEditor_BookEmpty = nameof(AccountEditor_BookEmpty);
    public const string AccountEditor_Teach = nameof(AccountEditor_Teach);
    public const string AccountEditor_SpellPlaceholder = nameof(AccountEditor_SpellPlaceholder);
    public const string AccountEditor_VaultHeader = nameof(AccountEditor_VaultHeader);
    public const string AccountEditor_VaultEmpty = nameof(AccountEditor_VaultEmpty);
    public const string AccountEditor_LogHeader = nameof(AccountEditor_LogHeader);
    public const string AccountEditor_LogEmpty = nameof(AccountEditor_LogEmpty);
    public const string AccountEditor_SetQuest = nameof(AccountEditor_SetQuest);
    public const string AccountEditor_QuestPlaceholder = nameof(AccountEditor_QuestPlaceholder);
    public const string AccountEditor_Ineligible = nameof(AccountEditor_Ineligible);
    public const string MainWindow_StatusOffline = nameof(MainWindow_StatusOffline);
    public const string MainWindow_StatusOnline = nameof(MainWindow_StatusOnline);
    public const string MainWindow_RailCollapse = nameof(MainWindow_RailCollapse);
    public const string MainWindow_RailExpand = nameof(MainWindow_RailExpand);
    public const string MainWindow_LoadingSection = nameof(MainWindow_LoadingSection);
    public const string MainWindow_LoadingData = nameof(MainWindow_LoadingData);
    public const string MainWindow_LoadingAssets = nameof(MainWindow_LoadingAssets);
    public const string MainWindow_LoadingSectionProgress = nameof(MainWindow_LoadingSectionProgress);
    public const string MainWindow_DisconnectedAlert = nameof(MainWindow_DisconnectedAlert);

    // ── ConnectDialog (in-dialog header + error; ConnectDialog_Title is the window title, exists) ──
    public const string ConnectDialog_Header = nameof(ConnectDialog_Header);
    public const string ConnectDialog_ConnectionError = nameof(ConnectDialog_ConnectionError);
    public const string ConnectDialog_IdentityChanged = nameof(ConnectDialog_IdentityChanged); // "{Host}" "{Port}"
    public const string ConnectDialog_KnownServers = nameof(ConnectDialog_KnownServers);
    public const string ConnectDialog_Forget = nameof(ConnectDialog_Forget);
    public const string ConnectDialog_Add = nameof(ConnectDialog_Add);
    public const string ConnectDialog_ServerName = nameof(ConnectDialog_ServerName);
    // The dialog's three groups: who you are, where the server is, which saved entry.
    public const string ConnectDialog_SignInHeader = nameof(ConnectDialog_SignInHeader);
    public const string ConnectDialog_ServerHeader = nameof(ConnectDialog_ServerHeader);
    public const string ConnectDialog_SavedServersHeader = nameof(ConnectDialog_SavedServersHeader);

    // ── DisconnectDialog (connection-lost body + reconnect outcomes) ──
    public const string DisconnectDialog_ConnectionLostBody = nameof(DisconnectDialog_ConnectionLostBody);
    public const string DisconnectDialog_ReconnectFailed = nameof(DisconnectDialog_ReconnectFailed);
    public const string DisconnectDialog_ReconnectCanceled = nameof(DisconnectDialog_ReconnectCanceled);

    // ── PushChangesDialog dirty-entry labels (Unsaved*/SaveAnd*/Saving/Pushing keys already exist) ──
    public const string PushChangesDialog_DirtyItem = nameof(PushChangesDialog_DirtyItem);
    public const string PushChangesDialog_DirtyNpc = nameof(PushChangesDialog_DirtyNpc);
    public const string PushChangesDialog_DirtyShop = nameof(PushChangesDialog_DirtyShop);
    public const string PushChangesDialog_DirtyQuest = nameof(PushChangesDialog_DirtyQuest);
    public const string PushChangesDialog_DirtyConversation = nameof(PushChangesDialog_DirtyConversation);
    public const string PushChangesDialog_DirtySpell = nameof(PushChangesDialog_DirtySpell);
    public const string PushChangesDialog_DirtyMap = nameof(PushChangesDialog_DirtyMap);
    public const string PushChangesDialog_DirtyMapGroup = nameof(PushChangesDialog_DirtyMapGroup);
    public const string PushChangesDialog_DirtyClass = nameof(PushChangesDialog_DirtyClass);
    public const string PushChangesDialog_DirtyUnknown = nameof(PushChangesDialog_DirtyUnknown);

    // ── Data field labels (shared by Item/Spell row editors) ──────────────────
    public const string DataLabel_Durability = nameof(DataLabel_Durability);
    public const string DataLabel_HpAmount = nameof(DataLabel_HpAmount);
    public const string DataLabel_MpAmount = nameof(DataLabel_MpAmount);
    public const string DataLabel_SpAmount = nameof(DataLabel_SpAmount);
    public const string DataLabel_SpellNumber = nameof(DataLabel_SpellNumber);
    public const string DataLabel_ItemNumber = nameof(DataLabel_ItemNumber);
    public const string DataLabel_Damage = nameof(DataLabel_Damage);
    public const string DataLabel_Defense = nameof(DataLabel_Defense);
    public const string DataLabel_MpDrain = nameof(DataLabel_MpDrain);
    public const string DataLabel_SpDrain = nameof(DataLabel_SpDrain);
    public const string DataLabel_Quantity = nameof(DataLabel_Quantity);
    public const string DataLabel_IntReq = nameof(DataLabel_IntReq);
    public const string DataLabel_AllowedClasses = nameof(DataLabel_AllowedClasses);
    // The class multi-select shared by the item, spell and quest editors.
    public const string ClassSelector_AnyClass = nameof(ClassSelector_AnyClass);
    public const string ClassSelector_Hint = nameof(ClassSelector_Hint);
    // Fallback captions for the two fields whose caption varies by type, shown if a type ever falls
    // outside the switch. Not "Data 1/2/3" any more — there is no numbered slot left to name.
    public const string DataLabel_VitalAmount = nameof(DataLabel_VitalAmount);
    public const string DataLabel_Power = nameof(DataLabel_Power);
    public const string DataLabel_LevelReq = nameof(DataLabel_LevelReq);

    // ── EditorConnection (service-layer errors shown to the user) ─────────────
    public const string EditorConnection_ClosedUnexpectedly = nameof(EditorConnection_ClosedUnexpectedly);
    public const string EditorConnection_UnexpectedResponse = nameof(EditorConnection_UnexpectedResponse);
    public const string EditorConnection_ClosedBeforeData = nameof(EditorConnection_ClosedBeforeData);
    public const string EditorConnection_ExpectedDataPacket = nameof(EditorConnection_ExpectedDataPacket);

    // ── TilePaletteControl ────────────────────────────────────────────────────
    public const string TilePalette_NoTileset = nameof(TilePalette_NoTileset);

    // ── HelpDialog ────────────────────────────────────────────────────────────
    public const string HelpDialog_Title = nameof(HelpDialog_Title);
    public const string HelpDialog_Header = nameof(HelpDialog_Header);
    public const string HelpDialog_ControlsHeader = nameof(HelpDialog_ControlsHeader);
    public const string HelpDialog_SelectionHeader = nameof(HelpDialog_SelectionHeader);
    public const string HelpDialog_LayersHeader = nameof(HelpDialog_LayersHeader);
    public const string HelpDialog_AttributeHeader = nameof(HelpDialog_AttributeHeader);
    public const string HelpDialog_LayerGround = nameof(HelpDialog_LayerGround);
    public const string HelpDialog_LayerGroundDesc = nameof(HelpDialog_LayerGroundDesc);
    public const string HelpDialog_LayerAnimFlag = nameof(HelpDialog_LayerAnimFlag);
    public const string HelpDialog_LayerAnimFlagDesc = nameof(HelpDialog_LayerAnimFlagDesc);
    public const string HelpDialog_LayerTileset = nameof(HelpDialog_LayerTileset);
    public const string HelpDialog_LayerTilesetDesc = nameof(HelpDialog_LayerTilesetDesc);
    public const string HelpDialog_LayerFringe = nameof(HelpDialog_LayerFringe);
    public const string HelpDialog_LayerFringeDesc = nameof(HelpDialog_LayerFringeDesc);
    public const string HelpDialog_LayerCanopy = nameof(HelpDialog_LayerCanopy);
    public const string HelpDialog_LayerCanopyDesc = nameof(HelpDialog_LayerCanopyDesc);
    public const string HelpDialog_LayersIntro = nameof(HelpDialog_LayersIntro);
    public const string HelpDialog_SelectionIntro = nameof(HelpDialog_SelectionIntro);
    public const string HelpDialog_AttributesIntro = nameof(HelpDialog_AttributesIntro);
    // Controls section
    public const string HelpDialog_Ctrl_LeftClick = nameof(HelpDialog_Ctrl_LeftClick);
    public const string HelpDialog_Ctrl_LeftClickDesc = nameof(HelpDialog_Ctrl_LeftClickDesc);
    public const string HelpDialog_Ctrl_AltClick = nameof(HelpDialog_Ctrl_AltClick);
    public const string HelpDialog_Ctrl_AltClickDesc = nameof(HelpDialog_Ctrl_AltClickDesc);
    public const string HelpDialog_Ctrl_CtrlDrag = nameof(HelpDialog_Ctrl_CtrlDrag);
    public const string HelpDialog_Ctrl_CtrlDragDesc = nameof(HelpDialog_Ctrl_CtrlDragDesc);
    public const string HelpDialog_Ctrl_CtrlAltShiftClick = nameof(HelpDialog_Ctrl_CtrlAltShiftClick);
    public const string HelpDialog_Ctrl_CtrlAltShiftClickDesc = nameof(HelpDialog_Ctrl_CtrlAltShiftClickDesc);
    public const string HelpDialog_Ctrl_RightClick = nameof(HelpDialog_Ctrl_RightClick);
    public const string HelpDialog_Ctrl_RightClickDesc = nameof(HelpDialog_Ctrl_RightClickDesc);
    public const string HelpDialog_Ctrl_UndoRedo = nameof(HelpDialog_Ctrl_UndoRedo);
    public const string HelpDialog_Ctrl_UndoRedoDesc = nameof(HelpDialog_Ctrl_UndoRedoDesc);
    public const string HelpDialog_Ctrl_BackForward = nameof(HelpDialog_Ctrl_BackForward);
    public const string HelpDialog_Ctrl_BackForwardDesc = nameof(HelpDialog_Ctrl_BackForwardDesc);
    public const string HelpDialog_Ctrl_MouseBackForward = nameof(HelpDialog_Ctrl_MouseBackForward);
    public const string HelpDialog_Ctrl_MouseBackForwardDesc = nameof(HelpDialog_Ctrl_MouseBackForwardDesc);
    public const string HelpDialog_Ctrl_ShiftHold = nameof(HelpDialog_Ctrl_ShiftHold);
    public const string HelpDialog_Ctrl_ShiftHoldDesc = nameof(HelpDialog_Ctrl_ShiftHoldDesc);
    public const string HelpDialog_Ctrl_ScrollWheel = nameof(HelpDialog_Ctrl_ScrollWheel);
    public const string HelpDialog_Ctrl_ScrollWheelDesc = nameof(HelpDialog_Ctrl_ScrollWheelDesc);
    public const string HelpDialog_Ctrl_CtrlScrollWheel = nameof(HelpDialog_Ctrl_CtrlScrollWheel);
    public const string HelpDialog_Ctrl_CtrlScrollWheelDesc = nameof(HelpDialog_Ctrl_CtrlScrollWheelDesc);
    public const string HelpDialog_Ctrl_AltScrollWheel = nameof(HelpDialog_Ctrl_AltScrollWheel);
    public const string HelpDialog_Ctrl_AltScrollWheelDesc = nameof(HelpDialog_Ctrl_AltScrollWheelDesc);
    public const string HelpDialog_Ctrl_CtrlAltScrollWheel = nameof(HelpDialog_Ctrl_CtrlAltScrollWheel);
    public const string HelpDialog_Ctrl_CtrlAltScrollWheelDesc = nameof(HelpDialog_Ctrl_CtrlAltScrollWheelDesc);
    public const string HelpDialog_Ctrl_ZoomButtons = nameof(HelpDialog_Ctrl_ZoomButtons);
    public const string HelpDialog_Ctrl_ZoomButtonsDesc = nameof(HelpDialog_Ctrl_ZoomButtonsDesc);
    public const string HelpDialog_Ctrl_AnimPreview = nameof(HelpDialog_Ctrl_AnimPreview);
    public const string HelpDialog_Ctrl_AnimPreviewDesc = nameof(HelpDialog_Ctrl_AnimPreviewDesc);
    public const string HelpDialog_Ctrl_SaveMap = nameof(HelpDialog_Ctrl_SaveMap);
    public const string HelpDialog_Ctrl_SaveMapDesc = nameof(HelpDialog_Ctrl_SaveMapDesc);
    public const string HelpDialog_Ctrl_DiscardMap = nameof(HelpDialog_Ctrl_DiscardMap);
    public const string HelpDialog_Ctrl_DiscardMapDesc = nameof(HelpDialog_Ctrl_DiscardMapDesc);
    public const string HelpDialog_Ctrl_PaletteClick = nameof(HelpDialog_Ctrl_PaletteClick);
    public const string HelpDialog_Ctrl_PaletteClickDesc = nameof(HelpDialog_Ctrl_PaletteClickDesc);
    // Selection section
    public const string HelpDialog_Sel_ActionPlace = nameof(HelpDialog_Sel_ActionPlace);
    public const string HelpDialog_Sel_ActionPlaceDesc = nameof(HelpDialog_Sel_ActionPlaceDesc);
    public const string HelpDialog_Sel_ActionSelection = nameof(HelpDialog_Sel_ActionSelection);
    public const string HelpDialog_Sel_ActionSelectionDesc = nameof(HelpDialog_Sel_ActionSelectionDesc);
    public const string HelpDialog_Sel_CtrlCSelection = nameof(HelpDialog_Sel_CtrlCSelection);
    public const string HelpDialog_Sel_CtrlCSelectionDesc = nameof(HelpDialog_Sel_CtrlCSelectionDesc);
    public const string HelpDialog_Sel_CtrlCPlace = nameof(HelpDialog_Sel_CtrlCPlace);
    public const string HelpDialog_Sel_CtrlCPlaceDesc = nameof(HelpDialog_Sel_CtrlCPlaceDesc);
    public const string HelpDialog_Sel_CtrlXSelection = nameof(HelpDialog_Sel_CtrlXSelection);
    public const string HelpDialog_Sel_CtrlXSelectionDesc = nameof(HelpDialog_Sel_CtrlXSelectionDesc);
    public const string HelpDialog_Sel_CtrlVSelection = nameof(HelpDialog_Sel_CtrlVSelection);
    public const string HelpDialog_Sel_CtrlVSelectionDesc = nameof(HelpDialog_Sel_CtrlVSelectionDesc);
    public const string HelpDialog_Sel_ClickPlace = nameof(HelpDialog_Sel_ClickPlace);
    public const string HelpDialog_Sel_ClickPlaceDesc = nameof(HelpDialog_Sel_ClickPlaceDesc);
    public const string HelpDialog_Sel_CtrlShiftClick = nameof(HelpDialog_Sel_CtrlShiftClick);
    public const string HelpDialog_Sel_CtrlShiftClickDesc = nameof(HelpDialog_Sel_CtrlShiftClickDesc);
    public const string HelpDialog_Sel_Esc = nameof(HelpDialog_Sel_Esc);
    public const string HelpDialog_Sel_EscDesc = nameof(HelpDialog_Sel_EscDesc);
    // Attribute reference
    public const string HelpDialog_Attr_BlockedDesc = nameof(HelpDialog_Attr_BlockedDesc);
    public const string HelpDialog_Attr_WarpDesc = nameof(HelpDialog_Attr_WarpDesc);
    public const string HelpDialog_Attr_WarpData = nameof(HelpDialog_Attr_WarpData);
    public const string HelpDialog_Attr_ItemDesc = nameof(HelpDialog_Attr_ItemDesc);
    public const string HelpDialog_Attr_ItemData = nameof(HelpDialog_Attr_ItemData);
    public const string HelpDialog_Attr_NpcAvoidDesc = nameof(HelpDialog_Attr_NpcAvoidDesc);
    public const string HelpDialog_Attr_Key = nameof(HelpDialog_Attr_Key);
    public const string HelpDialog_Attr_KeyDesc = nameof(HelpDialog_Attr_KeyDesc);
    public const string HelpDialog_Attr_KeyDoorDesc = nameof(HelpDialog_Attr_KeyDoorDesc);
    public const string HelpDialog_Attr_KeyData = nameof(HelpDialog_Attr_KeyData);
    public const string HelpDialog_Attr_KeyOpen = nameof(HelpDialog_Attr_KeyOpen);
    public const string HelpDialog_Attr_KeyOpenDesc = nameof(HelpDialog_Attr_KeyOpenDesc);
    public const string HelpDialog_Attr_KeyOpenData = nameof(HelpDialog_Attr_KeyOpenData);
    public const string HelpDialog_Attr_NpcSpawnDesc = nameof(HelpDialog_Attr_NpcSpawnDesc);
    public const string HelpDialog_Attr_LayerRamp = nameof(HelpDialog_Attr_LayerRamp);
    public const string HelpDialog_Attr_LayerRampDesc = nameof(HelpDialog_Attr_LayerRampDesc);
    public const string HelpDialog_Attr_LayerRampData = nameof(HelpDialog_Attr_LayerRampData);

    // ── ConnectDialog ─────────────────────────────────────────────────────────
    public const string ConnectDialog_Title = nameof(ConnectDialog_Title);
    public const string ConnectDialog_UsernameLabel = nameof(ConnectDialog_UsernameLabel);
    public const string ConnectDialog_PasswordLabel = nameof(ConnectDialog_PasswordLabel);
    public const string ConnectDialog_HostLabel = nameof(ConnectDialog_HostLabel);
    public const string ConnectDialog_PortLabel = nameof(ConnectDialog_PortLabel);

    // ── DisconnectDialog ──────────────────────────────────────────────────────
    public const string DisconnectDialog_Title = nameof(DisconnectDialog_Title);
    public const string DisconnectDialog_Header = nameof(DisconnectDialog_Header);
    public const string DisconnectDialog_AbandonButton = nameof(DisconnectDialog_AbandonButton);
    public const string DisconnectDialog_ReconnectButton = nameof(DisconnectDialog_ReconnectButton);

    // ── ConfirmDialog ─────────────────────────────────────────────────────────
    public const string ConfirmDialog_OkButton = nameof(ConfirmDialog_OkButton);
    public const string ConfirmDialog_Title = nameof(ConfirmDialog_Title);         // "Confirm"
    public const string ConfirmDialog_AlertTitle = nameof(ConfirmDialog_AlertTitle); // "Notice"

    // ── PushChangesDialog ─────────────────────────────────────────────────────
    public const string PushChangesDialog_Title = nameof(PushChangesDialog_Title);
    public const string PushChangesDialog_UnsavedOnClose = nameof(PushChangesDialog_UnsavedOnClose);
    public const string PushChangesDialog_UnsavedPush = nameof(PushChangesDialog_UnsavedPush);
    public const string PushChangesDialog_UnsavedConnect = nameof(PushChangesDialog_UnsavedConnect);
    public const string PushChangesDialog_UnsavedOnline = nameof(PushChangesDialog_UnsavedOnline);
    public const string PushChangesDialog_SaveAndClose = nameof(PushChangesDialog_SaveAndClose);
    public const string PushChangesDialog_PushAndContinue = nameof(PushChangesDialog_PushAndContinue);
    public const string PushChangesDialog_SaveAndConnect = nameof(PushChangesDialog_SaveAndConnect);
    public const string PushChangesDialog_PushAndDisconnect = nameof(PushChangesDialog_PushAndDisconnect);
    public const string PushChangesDialog_DiscardAndClose = nameof(PushChangesDialog_DiscardAndClose);
    public const string PushChangesDialog_DiscardAndContinue = nameof(PushChangesDialog_DiscardAndContinue);
    public const string PushChangesDialog_DiscardAndConnect = nameof(PushChangesDialog_DiscardAndConnect);
    public const string PushChangesDialog_DiscardAndDisconnect = nameof(PushChangesDialog_DiscardAndDisconnect);
    public const string PushChangesDialog_Saving = nameof(PushChangesDialog_Saving);
    public const string PushChangesDialog_Pushing = nameof(PushChangesDialog_Pushing);
    public const string PushChangesDialog_Error = nameof(PushChangesDialog_Error);

    // ── Status / Connection ───────────────────────────────────────────────────
    public const string Status_Offline = nameof(Status_Offline);
    public const string Status_Online = nameof(Status_Online);
    public const string Status_LoadingMaps = nameof(Status_LoadingMaps);
    public const string Status_LoadingMapsProgress = nameof(Status_LoadingMapsProgress);
    public const string Status_LoadingSection = nameof(Status_LoadingSection);
    public const string Status_FilterCount = nameof(Status_FilterCount);
}
