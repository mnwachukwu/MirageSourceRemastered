using Mirage.Shared.Localization;

namespace Mirage.Server.Core.Localization;

/// <summary>Marketplace listings, direct player-to-player trade, and the friends/ignore lists.</summary>
public static partial class ServerStrings
{
    // ── Marketplace ─────────────────────────────────────────────────────────────
    public const string Market_Sender = nameof(Market_Sender);
    public const string Market_NotAtInn = nameof(Market_NotAtInn);
    public const string Market_BadPrice = nameof(Market_BadPrice);
    public const string Market_TooManyListings = nameof(Market_TooManyListings);
    public const string Market_CannotList = nameof(Market_CannotList);
    public const string Market_CannotListItem = nameof(Market_CannotListItem);
    public const string Market_Listed = nameof(Market_Listed);
    public const string Market_ListingGone = nameof(Market_ListingGone);
    public const string Market_CannotBuyOwn = nameof(Market_CannotBuyOwn);
    public const string Market_NotEnoughGold = nameof(Market_NotEnoughGold);
    public const string Market_Bought = nameof(Market_Bought);
    public const string Market_Canceled = nameof(Market_Canceled);
    public const string Market_BoughtSubject = nameof(Market_BoughtSubject);
    public const string Market_BoughtBody = nameof(Market_BoughtBody);
    public const string Market_SoldSubject = nameof(Market_SoldSubject);
    public const string Market_SoldBody = nameof(Market_SoldBody);
    public const string Market_ReturnSubject = nameof(Market_ReturnSubject);
    public const string Market_ReturnBody = nameof(Market_ReturnBody);
    public const string Market_ExpiredSubject = nameof(Market_ExpiredSubject);
    public const string Market_ExpiredBody = nameof(Market_ExpiredBody);

    // ── Direct trade ────────────────────────────────────────────────────────────
    public const string Trade_Sender = nameof(Trade_Sender);
    public const string Trade_TargetNotOnline = nameof(Trade_TargetNotOnline);
    public const string Trade_CannotTradeSelf = nameof(Trade_CannotTradeSelf);
    public const string Trade_AlreadyTrading = nameof(Trade_AlreadyTrading);
    public const string Trade_OutOfRange = nameof(Trade_OutOfRange);
    public const string Trade_RequestSent = nameof(Trade_RequestSent);
    public const string Trade_RequestReceived = nameof(Trade_RequestReceived);
    public const string Trade_NoRequest = nameof(Trade_NoRequest);
    public const string Trade_Declined = nameof(Trade_Declined);
    public const string Trade_OfferFull = nameof(Trade_OfferFull);
    public const string Trade_CannotOfferItem = nameof(Trade_CannotOfferItem);
    public const string Trade_NoSpace = nameof(Trade_NoSpace);
    public const string Trade_Failed = nameof(Trade_Failed);
    public const string Trade_Complete = nameof(Trade_Complete);
    public const string Trade_Canceled = nameof(Trade_Canceled);
    public const string Trade_ReturnedSubject = nameof(Trade_ReturnedSubject);
    public const string Trade_ReturnedBody = nameof(Trade_ReturnedBody);
    public const string Quest_Sender = nameof(Quest_Sender);
    public const string Quest_Accepted = nameof(Quest_Accepted);
    public const string Quest_ReadyToTurnIn = nameof(Quest_ReadyToTurnIn);
    public const string Quest_Complete = nameof(Quest_Complete);
    public const string Quest_Abandoned = nameof(Quest_Abandoned);
    public const string Quest_NotEligible = nameof(Quest_NotEligible);
    public const string Quest_NotComplete = nameof(Quest_NotComplete);
    public const string Quest_TooMany = nameof(Quest_TooMany);
    public const string Quest_RewardMailSubject = nameof(Quest_RewardMailSubject);
    public const string Quest_RewardMailBody = nameof(Quest_RewardMailBody);

    // ── Social (friends / ignore) ─────────────────────────────────────────────
    public const string Social_FriendAdded = nameof(Social_FriendAdded);
    public const string Social_FriendRemoved = nameof(Social_FriendRemoved);
    public const string Social_AlreadyFriend = nameof(Social_AlreadyFriend);
    public const string Social_IgnoreAdded = nameof(Social_IgnoreAdded);
    public const string Social_IgnoreRemoved = nameof(Social_IgnoreRemoved);
    public const string Social_AlreadyIgnored = nameof(Social_AlreadyIgnored);
    public const string Social_TargetOffline = nameof(Social_TargetOffline);
    public const string Social_CantAddSelf = nameof(Social_CantAddSelf);
}
