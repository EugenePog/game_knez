using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Numerics;

[TestFixture]
public class FixVec2Tests
{
    [Test]
    public void Length_ThreeFour_IsFive()
    {
        var v = FixVec2.FromInts(3, 4);
        Assert.Multiple(() =>
        {
            Assert.That(v.LengthSquared, Is.EqualTo(Fix.FromInt(25)));
            Assert.That(v.Length, Is.EqualTo(Fix.FromInt(5)));
        });
    }

    [TestCase(3, 1000, 0, 1000, 196L)]   // 3 mm: non-zero, safe to normalise
    [TestCase(1, 100, 0, 100, 655L)]     // 1 cm: exact, not biased short
    [TestCase(3, 100, 4, 100, 3276L)]    // (3 cm, 4 cm) → 5 cm, floor of 3276.39 raw
    public void Length_ShortVectors_KeepsFullPrecision(int xNum, int xDen, int yNum, int yDen, long expectedRaw) =>
        Assert.That(new FixVec2(Fix.Ratio(xNum, xDen), Fix.Ratio(yNum, yDen)).Length.Raw, Is.EqualTo(expectedRaw));

    [Test]
    public void Operators_AreComponentWise()
    {
        var a = FixVec2.FromInts(1, 2);
        var b = FixVec2.FromInts(3, -4);
        Assert.Multiple(() =>
        {
            Assert.That(a + b, Is.EqualTo(FixVec2.FromInts(4, -2)));
            Assert.That(a - b, Is.EqualTo(FixVec2.FromInts(-2, 6)));
            Assert.That(-a, Is.EqualTo(FixVec2.FromInts(-1, -2)));
            Assert.That(a * Fix.FromInt(3), Is.EqualTo(FixVec2.FromInts(3, 6)));
            Assert.That(FixVec2.Dot(a, b), Is.EqualTo(Fix.FromInt(-5)));
        });
    }
}
