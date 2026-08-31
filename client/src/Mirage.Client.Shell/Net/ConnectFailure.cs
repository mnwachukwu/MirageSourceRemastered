using Mirage.Client.Shell.Localization;
using Mirage.Shared.Security;

namespace Mirage.Client.Shell.Net;

/// <summary>Turns a connect task that did not succeed into the message to show.</summary>
internal static class ConnectFailure
{
    /// <summary>A timeout is worth its own line: refused and unanswered look identical on screen but mean
    /// different things — a server that is down, versus an address or port that nothing is listening on.</summary>
    public static string Describe(Task connect) =>
        connect.Exception?.InnerException switch
        {
            ServerIdentityChangedException => ClientStrings.Get(ClientStrings.Common_ServerIdentityChanged),
            TimeoutException => ClientStrings.Get(ClientStrings.Common_ConnectionTimedOut),
            _ => ClientStrings.Get(ClientStrings.Common_CannotConnect),
        };
}
