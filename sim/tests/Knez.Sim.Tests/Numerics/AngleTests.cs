using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Numerics;

[TestFixture]
public class AngleTests
{
    [TestCase(0, 0)]
    [TestCase(90, 16384)]
    [TestCase(180, 32768)]
    [TestCase(270, 49152)]
    [TestCase(360, 0)]
    [TestCase(-90, 49152)]
    [TestCase(450, 16384)]
    [TestCase(1, 182)]   // 182.04 → nearest
    [TestCase(-1, 65354)] // mirror of +1: -182 wrapped
    [TestCase(int.MaxValue, 23120)] // ≡ 127° → 23119.64 units; no overflow even in checked Debug builds
    [TestCase(int.MinValue, 42234)] // ≡ −128° → −23301.69 → −23302, wrapped
    public void FromDegrees_Int_RoundsToNearestUnitAndWraps(int degrees, int expectedRaw)
    {
        Assert.That(Angle.FromDegrees(degrees).Raw, Is.EqualTo(expectedRaw));
    }

    [Test]
    public void FromDegrees_Fix_IsExactForWholeDegreesAndRoundsFractions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Angle.FromDegrees(Fix.FromInt(90)), Is.EqualTo(Angle.FromDegrees(90)));
            Assert.That(Angle.FromDegrees(Fix.FromInt(-45)), Is.EqualTo(Angle.FromDegrees(-45)));
            Assert.That(Angle.FromDegrees(Fix.Ratio(45, 2)).Raw, Is.EqualTo(4096)); // 22.5° = 1/16 turn
        });
    }

    [Test]
    public void Arithmetic_WrapsAroundTheTurn()
    {
        var a = Angle.FromDegrees(270);
        var b = Angle.FromDegrees(180);
        Assert.Multiple(() =>
        {
            Assert.That((a + b).Raw, Is.EqualTo(Angle.FromDegrees(90).Raw));
            Assert.That((b - a).Raw, Is.EqualTo(Angle.FromDegrees(-90).Raw));
            Assert.That((-Angle.FromDegrees(90)).Raw, Is.EqualTo(49152));
            Assert.That(Angle.FromRaw(65536 + 5).Raw, Is.EqualTo(5));
        });
    }

    [TestCase(0, 90, 16384)]
    [TestCase(90, 0, -16384)]
    [TestCase(350, 10, 3640)]       // across zero, the short way: +20° (each end rounds to whole units)
    [TestCase(10, 350, -3640)]
    [TestCase(0, 180, -32768)]      // exactly half a turn: always clockwise
    [TestCase(180, 0, -32768)]
    public void DeltaTo_ShortestSignedTurn(int fromDegrees, int toDegrees, int expected)
    {
        Assert.That(Angle.FromDegrees(fromDegrees).DeltaTo(Angle.FromDegrees(toDegrees)), Is.EqualTo(expected));
    }

    [Test]
    public void TurnToward_StepsAtMostMaxAndStopsOnTheTarget()
    {
        var target = Angle.FromDegrees(90);
        Assert.Multiple(() =>
        {
            Assert.That(Angle.Zero.TurnToward(target, 1000).Raw, Is.EqualTo(1000));
            Assert.That(Angle.FromDegrees(80).TurnToward(target, 5000), Is.EqualTo(target));
            Assert.That(Angle.FromDegrees(10).TurnToward(Angle.FromDegrees(350), 100).Raw, Is.EqualTo(Angle.FromDegrees(10).Raw - 100));
            Assert.That(target.TurnToward(target, 0), Is.EqualTo(target));
        });
    }

    [Test]
    public void ToString_ShowsDegreesWithTwoDecimals()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Angle.FromDegrees(90).ToString(), Is.EqualTo("90.00°"));
            Assert.That(Angle.FromRaw(4096).ToString(), Is.EqualTo("22.50°"));
        });
    }

    [Test]
    public void Forward_PointsAlongTheFacing()
    {
        // Convention (battle-sim.md §5): 0° = +X (east), counter-clockwise, 90° = +Y (north).
        Assert.Multiple(() =>
        {
            Assert.That(Angle.FromDegrees(0).Forward, Is.EqualTo(new FixVec2(Fix.One, Fix.Zero)));
            Assert.That(Angle.FromDegrees(90).Forward, Is.EqualTo(new FixVec2(Fix.Zero, Fix.One)));
            Assert.That(Angle.FromDegrees(180).Forward, Is.EqualTo(new FixVec2(-Fix.One, Fix.Zero)));
            Assert.That(Angle.FromDegrees(270).Forward, Is.EqualTo(new FixVec2(Fix.Zero, -Fix.One)));
        });
    }
}
