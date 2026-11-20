namespace Knez.Sim.Tests;

[TestFixture]
public class SimRngTests
{
    [Test]
    public void NextUInt_Seed42Sequence54_MatchesPcgReferenceOutput()
    {
        // First outputs of the PCG reference demo (pcg32-demo, pcg32_srandom_r(&rng, 42u, 54u)).
        var rng = new SimRng(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var actual = expected.Select(_ => rng.NextUInt()).ToArray();
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ForStream_SameSeedAndStream_GivesSameSequence()
    {
        var a = SimRng.ForStream(7, RngStream.Combat);
        var b = SimRng.ForStream(7, RngStream.Combat);
        Assert.That(Enumerable.Range(0, 100).Select(_ => a.NextUInt()), Is.EqualTo(Enumerable.Range(0, 100).Select(_ => b.NextUInt())));
    }

    [Test]
    public void ForStream_DifferentStreamsOrSeeds_GiveDifferentSequences()
    {
        uint First(ulong seed, RngStream stream) => SimRng.ForStream(seed, stream).NextUInt();
        Assert.Multiple(() =>
        {
            Assert.That(First(7, RngStream.Combat), Is.Not.EqualTo(First(7, RngStream.Morale)));
            Assert.That(First(7, RngStream.Combat), Is.Not.EqualTo(First(8, RngStream.Combat)));
        });
    }

    [Test]
    public void NextInt_StaysInRangeAndHitsEveryValue()
    {
        var rng = new SimRng(1, 1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.NextInt(-3, 4);
            Assert.That(value, Is.InRange(-3, 3));
            seen.Add(value);
        }

        Assert.That(seen, Has.Count.EqualTo(7));
    }

    [Test]
    public void NextInt_NonPositiveBound_Throws() =>
        Assert.That(() => new SimRng(1, 1).NextInt(0), Throws.TypeOf<ArgumentOutOfRangeException>());

    [Test]
    public void RollPermille_Bounds_AreNeverAndAlways()
    {
        var rng = new SimRng(3, 3);
        Assert.Multiple(() =>
        {
            Assert.That(Enumerable.Range(0, 1000).Any(_ => rng.RollPermille(0)), Is.False);
            Assert.That(Enumerable.Range(0, 1000).All(_ => rng.RollPermille(1000)), Is.True);
        });
    }

    [Test]
    public void Clone_ContinuesTheSameSequenceIndependently()
    {
        var original = new SimRng(5, 9);
        original.NextUInt();
        var copy = original.Clone();
        var fromOriginal = Enumerable.Range(0, 50).Select(_ => original.NextUInt()).ToArray();
        var fromCopy = Enumerable.Range(0, 50).Select(_ => copy.NextUInt()).ToArray();
        Assert.That(fromCopy, Is.EqualTo(fromOriginal));
    }
}
