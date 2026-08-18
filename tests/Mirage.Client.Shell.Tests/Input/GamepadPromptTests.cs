using Mirage.Client.Shell.Input;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests;

/// <summary>
/// Which device the hotkey badges name. Two properties look similar and answer different questions:
/// <see cref="InputState.IsGamepadActive"/> arbitrates which device may ACT and lets both through before
/// either has claimed a frame, so a badge reading it prints controller faces to a keyboard player.
/// <see cref="InputState.ShowGamepadPrompts"/> has to pick one, and the keyboard is the answer until a
/// pad is both enabled and in use.
/// </summary>
[TestFixture]
public class GamepadPromptTests
{
    [Test]
    public void WithTheGamepadTurnedOffThePromptsAreNeverGamepadPrompts()
    {
        var input = new InputState { UseGamepad = false };

        Assert.That(input.ShowGamepadPrompts, Is.False);
    }

    [Test]
    public void BeforeAnythingIsPressedThePromptsAreKeyboardPrompts()
    {
        var input = new InputState { UseGamepad = true };

        Assert.That(input.ActiveDevice, Is.EqualTo(ActiveInputDevice.None), "nothing has claimed the frame");
        Assert.That(input.ShowGamepadPrompts, Is.False, "so the badges read 1-4, not X/Y/B/A");
    }

    [Test]
    public void TheArbiterStillLetsBothDevicesActBeforeEitherClaimsAFrame()
    {
        var input = new InputState { UseGamepad = true };

        Assert.That(input.IsKeyboardActive, Is.True);
        Assert.That(input.IsGamepadActive, Is.True,
            "double-fire suppression depends on this staying permissive; the display question is separate");
    }

    [Test]
    public void ResetReturnsToTheKeyboardPrompts()
    {
        var input = new InputState { UseGamepad = true };
        input.Reset();

        Assert.That(input.ShowGamepadPrompts, Is.False);
    }
}
