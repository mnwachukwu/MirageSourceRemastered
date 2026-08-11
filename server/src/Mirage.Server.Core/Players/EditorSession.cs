using Mirage.Shared;

namespace Mirage.Server.Core.Players;

/// <summary>One editor connection slot. Instances are pre-allocated by
/// <see cref="EditorSessionManager"/> and reused, so a slot is "free" by virtue of its flags rather
/// than by being null.</summary>
public sealed class EditorSession
{
    /// <summary>1-based slot number; fixed for the lifetime of the manager.</summary>
    public int Index { get; set; }
    /// <summary>Account name once authenticated; blank while free or unauthenticated.</summary>
    public string Login { get; set; } = "";
    /// <summary>Access level of the authenticated account, which gates the editor sections.</summary>
    public AdminLevel AdminLevel { get; set; }
    /// <summary>Whether credentials have been accepted. Connected but unauthenticated is a real state —
    /// the socket is open while the login is still in flight.</summary>
    public bool IsAuthenticated { get; set; }
    /// <summary>Whether the slot is in use; this is what <see cref="EditorSessionManager.FindOpenSlot"/> tests.</summary>
    public bool IsConnected { get; set; }
}
