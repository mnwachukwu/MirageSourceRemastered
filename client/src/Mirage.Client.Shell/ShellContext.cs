using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell;

/// <summary>Services shared across all screens and panels.</summary>
public sealed class ShellContext
{
    public required ScreenManager Screens { get; init; }
    public required ClientState State { get; init; }
    public required ClientPacketSender Sender { get; init; }
    public required MenuLogic Menu { get; init; }
    public required IClientTransport Transport { get; init; }
    public required GraphicsDevice Graphics { get; init; }
    public required Action ExitGame { get; init; }
    public required string ServerHost { get; set; }
    public required int ServerPort { get; set; }
    public required AlertDialog Dialog { get; init; }
    public required OptionsPanel OptionsPanel { get; init; }
    public required Action<bool> OnAspectRatioChanged { get; init; }
    public required Action<bool> OnAlwaysShowBarsChanged { get; init; }
    public required Action<bool> OnShowCombatNumbersChanged { get; init; }
    public required Action<bool> OnUseGamepadChanged { get; init; }
    public required Action OnRestoreDefaults { get; init; }
    public required Action SaveSettings { get; init; }
    public required Action ShowQuitConfirm { get; init; }
    public required Action<bool> OnPlayMusicChanged { get; init; }
    public required Action<int> OnMusicVolumeChanged { get; init; }
    public required Action PlayMenuMusic { get; init; }
    public required Action<string> OnLanguageChanged { get; init; }
    public SpriteFont? MenuFont { get; set; }
    public SpriteFont? TitleFont { get; set; }
    public Texture2D? MenuArt { get; set; }
    public Texture2D? Sprites { get; set; }
    public Texture2D? Sprites64 { get; set; }
    public Texture2D? Sprites96 { get; set; }
    public Texture2D? Items { get; set; }
}
