using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Account and character auth, editor auth, and the join/leave announcements.</summary>
public static partial class ServerStrings
{
    // ── Auth / Account ────────────────────────────────────────────────────────
    public const string Auth_ShortNameAndPass = nameof(Auth_ShortNameAndPass);
    public const string Auth_ShortDeleteNameAndPass = nameof(Auth_ShortDeleteNameAndPass);
    public const string Auth_ShortNameOrPass = nameof(Auth_ShortNameOrPass);
    public const string Auth_NameTooLong = nameof(Auth_NameTooLong);
    public const string Auth_InvalidName = nameof(Auth_InvalidName);
    public const string Auth_AccountTaken = nameof(Auth_AccountTaken);
    public const string Auth_AccountCreated = nameof(Auth_AccountCreated);
    public const string Auth_AccountCreateFailed = nameof(Auth_AccountCreateFailed);
    public const string Auth_AccountNotFound = nameof(Auth_AccountNotFound);
    public const string Auth_IncorrectPassword = nameof(Auth_IncorrectPassword);
    public const string Auth_AccountLoggedInDelete = nameof(Auth_AccountLoggedInDelete);
    public const string Auth_AccountLoggedIn = nameof(Auth_AccountLoggedIn);
    public const string Auth_AccountDeleted = nameof(Auth_AccountDeleted);
    public const string Auth_PasswordChanged = nameof(Auth_PasswordChanged);
    public const string Auth_ClientOutdated = nameof(Auth_ClientOutdated);
    public const string Auth_LoadFailed = nameof(Auth_LoadFailed);
    public const string Auth_MultiAccount = nameof(Auth_MultiAccount);
    public const string Auth_Banned = nameof(Auth_Banned);
    public const string Auth_BannedCannotDelete = nameof(Auth_BannedCannotDelete);
    public const string Auth_KickedTryAgain = nameof(Auth_KickedTryAgain);
    public const string Auth_CharNameTooShort = nameof(Auth_CharNameTooShort);
    public const string Auth_CharNameTooLong = nameof(Auth_CharNameTooLong);
    public const string Auth_CharSlotsFull = nameof(Auth_CharSlotsFull);
    public const string Auth_CharAlreadyExists = nameof(Auth_CharAlreadyExists);
    public const string Auth_CharNameInUse = nameof(Auth_CharNameInUse);
    public const string Auth_CharNotFound = nameof(Auth_CharNotFound);
    public const string Auth_CombatGhostWarning = nameof(Auth_CombatGhostWarning);

    // ── Editor Auth ───────────────────────────────────────────────────────────
    public const string EditorAuth_InvalidCredentials = nameof(EditorAuth_InvalidCredentials);
    public const string EditorAuth_InsufficientAccess = nameof(EditorAuth_InsufficientAccess);
    public const string EditorAuth_Authenticated = nameof(EditorAuth_Authenticated);

    // ── JoinLeaveSystem ───────────────────────────────────────────────────────
    public const string JoinLeave_JoinBroadcast = nameof(JoinLeave_JoinBroadcast);
    public const string JoinLeave_LeaveBroadcast = nameof(JoinLeave_LeaveBroadcast);
    public const string JoinLeave_Welcome = nameof(JoinLeave_Welcome);
    public const string JoinLeave_HelpHint = nameof(JoinLeave_HelpHint);
    public const string JoinLeave_Motd = nameof(JoinLeave_Motd);
    public const string JoinLeave_NoOtherPlayers = nameof(JoinLeave_NoOtherPlayers);
    public const string JoinLeave_OtherPlayers = nameof(JoinLeave_OtherPlayers);
}
