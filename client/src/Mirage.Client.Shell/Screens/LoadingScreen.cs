using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;

namespace Mirage.Client.Shell.Screens;

/// <summary>
/// Shown while waiting for a server response. Replaced automatically by MenuLogic on success,
/// or via the AlertDialog / disconnect detection in MirageGame on failure.
/// </summary>
public sealed class LoadingScreen : IGameScreen
{
    private readonly ShellContext _ctx;

    public LoadingScreen(ShellContext ctx) => _ctx = ctx;

    public void OnEnter() { }
    public void OnExit() { }
    public void Update(GameTime gameTime, InputState input) { }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        var vp = _ctx.Graphics.Viewport.Bounds;
        UiHelper.DrawFilledRect(sb, vp, Color.Black);

        string msg = _ctx.State.LoadingMessage.Length > 0 ? _ctx.State.LoadingMessage : ClientStrings.Get(ClientStrings.LoadingScreen_DefaultMessage);
        var pos = UiHelper.CenterText(font, msg, vp);
        sb.DrawString(font, msg, pos, Color.White);
    }
}
