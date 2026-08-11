using Microsoft.Xna.Framework;
using Mirage.Shared;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Maps a 0-based item pic number to a source rectangle in items.bmp.
/// Layout: a vertical strip, each item 32 pixels tall. Pic=0 is the first graphic.
/// </summary>
public static class ItemAtlas
{
    public static Rectangle GetSourceRect(short pic)
    {
        if (pic < 0) return Rectangle.Empty;
        return new Rectangle(0, pic * Constants.PicY, Constants.PicX, Constants.PicY);
    }
}
