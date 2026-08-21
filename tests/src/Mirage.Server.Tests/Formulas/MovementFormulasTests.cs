using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Server.Tests.Formulas;

/// <summary>SPD to movement speed, and the display figure derived from it.
///
/// <para>The display half is pinned because it is the half that can be quietly wrong: the number is a
/// COMPARISON, and a comparison against the wrong baseline still looks plausible. Measured against
/// walking, sprint is +100% at 0 SPD and +200% at the cap; measured against the 0-SPD RUN it would be
/// 100% and 150%. Those agree at 0 SPD and nowhere else, so a fresh character cannot tell the two
/// apart and an invested one is understated by a whole walk's worth of speed.</para></summary>
[TestFixture]
public class MovementFormulasTests
{
    [Test]
    public void RunIsExactlyTwiceWalkPaceBeforeAnySpd()
    {
        // The premise the Sprint figure rests on. If these two constants ever drift apart, +100% stops
        // being the right thing to show a brand-new character and this is where that shows up.
        Assert.That(MovementFormulas.BaseWalkMsPerTile, Is.EqualTo(MovementFormulas.BaseRunMsPerTile * 2f));
    }

    [TestCase(0, 100, TestName = "0 SPD sprints at twice walk pace (+100%)")]
    [TestCase(75, 150, TestName = "Half the cap's SPD sprints at 2.5x walk (+150%)")]
    [TestCase(150, 200, TestName = "Cap SPD sprints at three times walk (+200%)")]
    [TestCase(300, 200, TestName = "Past the cap adds nothing further")]
    public void SprintBonusPercent_IsMeasuredAgainstWalking(int spd, int expected)
    {
        Assert.That(MovementFormulas.SprintBonusPercent(spd), Is.EqualTo(expected));
    }

    [Test]
    public void SprintBonusPercent_NeverDropsBelowTheFloor()
    {
        // Base run is a hard floor: SPD is a pure additive bonus and a negative or zero SPD must not
        // make a character slower than the baseline everyone starts at.
        Assert.That(MovementFormulas.SprintBonusPercent(-50), Is.EqualTo(100));
    }

    [Test]
    public void RunMsPerTile_MatchesTheDocumentedCadences()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementFormulas.RunMsPerTile(0), Is.EqualTo(200f).Within(0.5f));
            Assert.That(MovementFormulas.RunMsPerTile(75), Is.EqualTo(160f).Within(0.5f));
            Assert.That(MovementFormulas.RunMsPerTile(150), Is.EqualTo(133.3f).Within(0.5f));
        });
    }
}
