using Knez.Sim.Content;

namespace Knez.Sim.Tests.Content;

[TestFixture]
public class ContentNumbersTests
{
    [TestCase("0", 0L)]
    [TestCase("3", 196608L)]
    [TestCase("-3", -196608L)]
    [TestCase("1.25", 81920L)]
    [TestCase("0.5", 32768L)]
    [TestCase("-0.5", -32768L)]
    [TestCase("0.1", 6554L)]     // 6553.6 → nearest
    [TestCase("-0.1", -6554L)]   // mirrors the positive value
    [TestCase("0.3", 19661L)]    // 19660.8
    [TestCase("0.75", 49152L)]
    [TestCase("-0", 0L)]
    [TestCase("0.000007629", 0L)]  // just below half a unit (0.0000076294 = 0.5/65536)
    [TestCase("0.000007630", 1L)]  // just above
    [TestCase("1000000000", 65536000000000L)]
    public void TryParseFix_ValidDecimal_ExactRaw(string text, long raw)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContentNumbers.TryParseFix(text, out var value, out string error), Is.True, error);
            Assert.That(value.Raw, Is.EqualTo(raw));
        });
    }

    [TestCase("", "expected a number")]
    [TestCase("-", "is not a number")]
    [TestCase(".5", "is not a number")]
    [TestCase("1.", "expected digits after the point")]
    [TestCase("+1", "is not a number")]
    [TestCase("01", "leading zeros")]
    [TestCase("1e3", "exponents are not allowed")]
    [TestCase("1.5E-2", "exponents are not allowed")]
    [TestCase("1.0000000001", "more than 9 digits")]
    [TestCase("1000000001", "too large")]
    [TestCase("99999999999999999999999", "too large")]
    [TestCase("1,5", "is not a number")]
    [TestCase("١", "is not a number")] // Arabic-Indic digit one: only ASCII digits count
    public void TryParseFix_Invalid_FalseWithReason(string text, string reason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContentNumbers.TryParseFix(text, out _, out string error), Is.False);
            Assert.That(error, Does.Contain(reason));
        });
    }

    [TestCase("8", 8)]
    [TestCase("-2", -2)]
    [TestCase("0", 0)]
    public void TryParseInt_WholeNumber_Parses(string text, int expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContentNumbers.TryParseInt(text, out int value, out string error), Is.True, error);
            Assert.That(value, Is.EqualTo(expected));
        });
    }

    [TestCase("8.0", "expected a whole number")]
    [TestCase("8.5", "expected a whole number")]
    [TestCase("1e2", "exponents are not allowed")]
    public void TryParseInt_NotAWholeNumber_FalseWithReason(string text, string reason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ContentNumbers.TryParseInt(text, out _, out string error), Is.False);
            Assert.That(error, Does.Contain(reason));
        });
    }
}
