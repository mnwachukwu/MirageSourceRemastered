using Mirage.Client.Shell.Localization;
using Mirage.Shared.Security;

namespace Mirage.Client.Shell.Net;

/// <summary>Turns a faulted connect task into the message to show.</summary>
internal static class ConnectFailure
{
    public static string Describe(Task connect) =>
        connect.Exception?.InnerException is ServerIdentityChangedException
            ? ClientStrings.Get(ClientStrings.Common_ServerIdentityChanged)
            : ClientStrings.Get(ClientStrings.Common_CannotConnect);
}
