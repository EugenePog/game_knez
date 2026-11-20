using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Numerics;

[TestFixture]
public class FixTests
{
    [Test]
    public void FromInt_ScalesBySixteenBits() =>
        Assert.That(Fix.FromInt(3).Raw, Is.EqualTo(3L * 65536));

    [Test]
    public void Ratio_ThreeHalves_IsOnePointFive() =>
        Assert.That(Fix.Ratio(3, 2).Raw, Is.EqualTo(98304L));

    [Test]
    public void Arithmetic_OnExactValues_IsExact()
    {
        var a = Fix.Ratio(3, 2);
        var two = Fix.FromInt(2);
        Assert.Multiple(() =>
        {
            Assert.That(a + two, Is.EqualTo(Fix.Ratio(7, 2)));
            Assert.That(a - two, Is.EqualTo(-Fix.Half));
            Assert.That(a * two, Is.EqualTo(Fix.FromInt(3)));
            Assert.That(-a * two, Is.EqualTo(Fix.FromInt(-3)));
            Assert.That(Fix.FromInt(3) / two, Is.EqualTo(a));
            Assert.That(a * 4, Is.EqualTo(Fix.FromInt(6)));
            Assert.That(Fix.FromInt(3) / 2, Is.EqualTo(a));
        });
    }

    [Test]
    public void Multiply_InexactNegative_TruncatesTowardZero() =>
        Assert.That((Fix.FromRaw(-1) * Fix.Half).Raw, Is.EqualTo(0L));

    [Test]
    public void Multiply_IsSymmetricAroundZero()
    {
        var third = Fix.Ratio(1, 3);
        var seventh = Fix.Ratio(1, 7);
        Assert.That(third * -seventh, Is.EqualTo(-(third * seventh)));
    }

    [Test]
    public void Damping_NegativeValue_DecaysToZeroLikeAPositiveOne()
    {
        var damping = Fix.Ratio(9, 10);
        var up = Fix.FromRaw(100);
        var down = Fix.FromRaw(-100);
        for (int i = 0; i < 200; i++)
        {
            up *= damping;
            down *= damping;
        }

        Assert.Multiple(() =>
        {
            Assert.That(up, Is.EqualTo(Fix.Zero));
            Assert.That(down, Is.EqualTo(Fix.Zero));
        });
    }

    [Test]
    public void DivisionByZero_ThrowsOnEveryPath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Fix.Ratio(1, 0), Throws.TypeOf<DivideByZeroException>());
            Assert.That(() => Fix.One / Fix.Zero, Throws.TypeOf<DivideByZeroException>());
            Assert.That(() => Fix.One / 0, Throws.TypeOf<DivideByZeroException>());
        });
    }

#if DEBUG
    [Test]
    public void Multiply_OutOfRange_ThrowsInDebugBuilds() =>
        Assert.That(() => Fix.FromInt(46_341) * Fix.FromInt(46_341), Throws.TypeOf<OverflowException>());
#endif

    [Test]
    public void Divide_InexactNegative_TruncatesTowardZero() =>
        Assert.That((Fix.FromRaw(-3) / Fix.FromInt(2)).Raw, Is.EqualTo(-1L));

    [Test]
    public void FloorToInt_Negative_RoundsDown() =>
        Assert.That(Fix.Ratio(-3, 2).FloorToInt(), Is.EqualTo(-2));

    [Test]
    public void Comparisons_FollowRawOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fix.Half < Fix.One, Is.True);
            Assert.That(Fix.FromInt(1) >= Fix.One, Is.True);
            Assert.That(Fix.Zero > Fix.Half, Is.False);
            Assert.That(Fix.One.CompareTo(Fix.Half), Is.Positive);
        });
    }

    [TestCase(3, 2, "1.50000")]
    [TestCase(-1, 4, "-0.25000")]
    [TestCase(1, 3, "0.33332")]
    [TestCase(0, 1, "0.00000")]
    public void ToString_PrintsFiveTruncatedDigits(long numerator, long denominator, string expected) =>
        Assert.That(Fix.Ratio(numerator, denominator).ToString(), Is.EqualTo(expected));
}
