using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Numerics;

[TestFixture]
public class FixMathTests
{
    [Test]
    public void Sqrt_PerfectSquare_IsExact() =>
        Assert.That(FixMath.Sqrt(Fix.FromInt(4)), Is.EqualTo(Fix.FromInt(2)));

    [Test]
    public void Sqrt_Two_RoundsDown() =>
        // sqrt(2) * 65536 = 92681.9…
        Assert.That(FixMath.Sqrt(Fix.FromInt(2)).Raw, Is.EqualTo(92681L));

    [Test]
    public void Sqrt_Zero_IsZero() =>
        Assert.That(FixMath.Sqrt(Fix.Zero), Is.EqualTo(Fix.Zero));

    [Test]
    public void Sqrt_Negative_Throws() =>
        Assert.That(() => FixMath.Sqrt(-Fix.One), Throws.TypeOf<ArgumentOutOfRangeException>());

    [Test]
    public void Sqrt_AtOrAboveTwoToThe32_ThrowsInsteadOfWrapping() =>
        Assert.That(() => FixMath.Sqrt(Fix.FromRaw(1L << 48)), Throws.TypeOf<ArgumentOutOfRangeException>());

    [TestCase(0UL, 0UL)]
    [TestCase(1UL, 1UL)]
    [TestCase(15UL, 3UL)]
    [TestCase(16UL, 4UL)]
    [TestCase(ulong.MaxValue, 4294967295UL)]
    public void IntegerSqrt_RoundsDown(ulong n, ulong expected) =>
        Assert.That(FixMath.IntegerSqrt(n), Is.EqualTo(expected));

    [Test]
    public void AbsMinMaxClamp_Work()
    {
        var a = Fix.Ratio(-3, 2);
        Assert.Multiple(() =>
        {
            Assert.That(FixMath.Abs(a), Is.EqualTo(Fix.Ratio(3, 2)));
            Assert.That(FixMath.Min(a, Fix.One), Is.EqualTo(a));
            Assert.That(FixMath.Max(a, Fix.One), Is.EqualTo(Fix.One));
            Assert.That(FixMath.Clamp(Fix.FromInt(5), Fix.Zero, Fix.One), Is.EqualTo(Fix.One));
            Assert.That(FixMath.Clamp(a, Fix.Zero, Fix.One), Is.EqualTo(Fix.Zero));
        });
    }
}
