using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Numerics;

/// <summary>Sin/Cos from the committed table (tools/gen_sine_table.py). Doubles are fine here: this is the test side.</summary>
[TestFixture]
public class FixTrigTests
{
    [TestCase(0, 0L)]
    [TestCase(90, 65536L)]
    [TestCase(180, 0L)]
    [TestCase(270, -65536L)]
    [TestCase(30, 32766L)]   // 30° rounds to 5461 units = 29.998°, whose sine is 0.49997
    [TestCase(-30, -32766L)]
    public void Sin_KnownAngles(int degrees, long expectedRaw)
    {
        Assert.That(FixMath.Sin(Angle.FromDegrees(degrees)).Raw, Is.EqualTo(expectedRaw).Within(1));
    }

    /// <summary>
    /// Exact values between table entries: these pin the interpolation itself (truncating, not rounding — raw 1000 is
    /// 6273.56 in truth and must stay 6273). A change here changes every hash that involves an angle.
    /// </summary>
    [TestCase(1000, 6273L, 65234L)]
    [TestCase(5461, 32766L, 56756L)]
    [TestCase(12345, 60683L, 24748L)]
    [TestCase(18204, 64540L, -11377L)] // 100°
    [TestCase(40000, -41886L, -50404L)]
    public void SinCos_BetweenTableEntries_ExactKnownAnswers(int raw, long sinRaw, long cosRaw)
    {
        var angle = Angle.FromRaw(raw);
        Assert.Multiple(() =>
        {
            Assert.That(FixMath.Sin(angle).Raw, Is.EqualTo(sinRaw));
            Assert.That(FixMath.Cos(angle).Raw, Is.EqualTo(cosRaw));
        });
    }

    [Test]
    public void Cos_IsSinShiftedByAQuarterTurn()
    {
        for (int raw = 0; raw < 65536; raw += 97)
        {
            var a = Angle.FromRaw(raw);
            Assert.That(FixMath.Cos(a), Is.EqualTo(FixMath.Sin(a + Angle.FromDegrees(90))));
        }
    }

    [Test]
    public void Sin_EveryAngle_WithinTwoUnitsOfTheTrueValue()
    {
        long worst = 0;
        for (int raw = 0; raw < 65536; raw++)
        {
            long actual = FixMath.Sin(Angle.FromRaw(raw)).Raw;
            long expected = (long)Math.Round(Math.Sin(raw * Math.PI * 2 / 65536) * 65536);
            worst = Math.Max(worst, Math.Abs(actual - expected));
        }

        Assert.That(worst, Is.LessThanOrEqualTo(2));
    }

    [Test]
    public void Sin_IsOddAndSymmetricAroundTheQuarterTurn()
    {
        for (int raw = 0; raw < 65536; raw += 31)
        {
            var a = Angle.FromRaw(raw);
            Assert.That(FixMath.Sin(-a), Is.EqualTo(-FixMath.Sin(a)), $"odd at raw {raw}");
            Assert.That(FixMath.Sin(Angle.FromDegrees(180) - a), Is.EqualTo(FixMath.Sin(a)), $"mirror at raw {raw}");
        }
    }

    [Test]
    public void Table_MatchesTheGeneratorFormula()
    {
        // Guards against a hand edit of SineTable.cs: entry k = round(sin(k·90°/256)·65536).
        for (int k = 0; k <= 256; k++)
        {
            long expected = (long)Math.Round(Math.Sin(k * (Math.PI / 2) / 256) * 65536, MidpointRounding.ToEven);
            Assert.That(SineTable.QuarterSin(k << 6), Is.EqualTo(expected), $"entry {k}"); // k·64 units = entry k exactly
        }
    }
}
