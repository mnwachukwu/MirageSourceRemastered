using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Ui;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests;

/// <summary>
/// The frame readout stacks above the action bar, which sits in the bottom-right corner — its left edge is at
/// 583 of the 800-wide reference. Anchoring the text there leaves about two hundred pixels before the screen
/// ends, and the longest line needs half as much again, so it ran off the edge.
///
/// <para>The block therefore grows LEFTWARD from the bar's right edge whenever it is wider than the bar. This
/// is arithmetic, not rendering, so it is testable without a device — what a font measures is the input.</para>
/// </summary>
[TestFixture]
public class FrameReadoutLayoutTests
{
    private static readonly float BarLeft = HotkeyBarPanel.Bounds.Left;
    private static readonly float BarRight = HotkeyBarPanel.Bounds.Right;

    [Test]
    public void ContentNarrowerThanTheBar_StaysAlignedToIt()
    {
        float widest = HotkeyBarPanel.Bounds.Width - 10;
        Assert.That(GameplayScreen.ReadoutLeft(BarLeft, BarRight, widest), Is.EqualTo(BarLeft),
            "a short line has no reason to move");
    }

    [TestCase(200f)]
    [TestCase(320f)]
    [TestCase(400f)]
    public void ContentWiderThanTheBar_EndsFlushWithItAndFitsOnScreen(float widest)
    {
        float x = GameplayScreen.ReadoutLeft(BarLeft, BarRight, widest);
        Assert.Multiple(() =>
        {
            Assert.That(x + widest, Is.EqualTo(BarRight).Within(0.01f), "the block's right edge lands on the bar's");
            Assert.That(x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(x + widest, Is.LessThanOrEqualTo((float)UiHelper.RefW), "and nothing runs off the screen");
        });
    }

    [Test]
    public void ContentWiderThanTheScreen_StartsAtTheLeftEdge()
    {
        // Nothing can save a line this long, but it must clip at the far edge rather than start off-screen.
        Assert.That(GameplayScreen.ReadoutLeft(BarLeft, BarRight, UiHelper.RefW * 2f), Is.EqualTo(0f));
    }

    [Test]
    public void TheBarIsFarEnoughRightThatThisMatters()
    {
        // The premise, pinned: if the bar ever moves left, this whole guard is still correct but the failure
        // it prevents is gone, and the test above stops meaning anything.
        Assert.That(UiHelper.RefW - BarLeft, Is.LessThan(320f),
            "the readout's longest line does not fit to the right of the action bar");
    }
}
