using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Determinism;

/// <summary>
/// The determinism contract (battle-sim.md §15, `determinism` skill): scenario + seed + commands ⇒ the same hash at
/// every tick, in every run, process, OS and runtime.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class DeterminismTests
{
    private const int Ticks = 300;

    /// <summary>
    /// Final hash of <c>Run(TestBattle.Create(42), 300)</c>. A change here means outcomes changed: if that was intended
    /// (a rule change), update the constant and say why in the commit message; if not, it is a determinism bug.
    /// </summary>
    private const ulong GoldenFinalHash = 0xb0b9acf1c59bf1d6UL;

    [Test]
    public void TwinRun_SameSeedAndCommands_IdenticalHashEveryTick()
    {
        var first = Run(TestBattle.Create(7), Ticks);
        var second = Run(TestBattle.Create(7), Ticks);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void DifferentSeed_DivergesFromTheFirstTick()
    {
        // Compare the RNG and fighter sections: Core always differs because it hashes the seed itself.
        var a = Run(TestBattle.Create(7), 1);
        var b = Run(TestBattle.Create(8), 1);
        Assert.Multiple(() =>
        {
            Assert.That(b[0].Rng, Is.Not.EqualTo(a[0].Rng));
            Assert.That(b[0].Fighters, Is.Not.EqualTo(a[0].Fighters));
        });
    }

    [Test]
    public void SnapshotRestore_ContinuesExactlyLikeTheUninterruptedRun()
    {
        var uninterrupted = Run(TestBattle.Create(7), Ticks);

        var first = TestBattle.Create(7);
        Run(first, 150);
        var snapshot = first.TakeSnapshot();
        Assert.That(snapshot.Commands.Pending, Is.Not.Empty, "the tick-260 command must cross the snapshot");

        var restored = BattleSimulation.Restore(snapshot, TestBattle.SystemsWithWander());
        var continued = Run(restored, Ticks);
        var originalContinued = Run(first, Ticks); // the original keeps going too: nothing shared with the snapshot

        Assert.Multiple(() =>
        {
            Assert.That(continued, Is.EqualTo(uninterrupted.Skip(150)));
            Assert.That(originalContinued, Is.EqualTo(uninterrupted.Skip(150)));
            Assert.That(restored.Commands.Log.Select(c => c.Tick), Does.Contain(260));
        });
    }

    [Test]
    public void Snapshot_RestoredTwice_GivesTheSameFuture()
    {
        var sim = TestBattle.Create(7);
        Run(sim, 120);
        var snapshot = sim.TakeSnapshot();

        var a = Run(BattleSimulation.Restore(snapshot, TestBattle.SystemsWithWander()), Ticks);
        var b = Run(BattleSimulation.Restore(snapshot, TestBattle.SystemsWithWander()), Ticks);
        Assert.That(b, Is.EqualTo(a));
    }

    [Test]
    public void Golden_Seed42_FinalHashMatchesRecordedValue()
    {
        // Catches nondeterminism across processes (randomised string hashes, static state) and unintended rule changes.
        var hashes = Run(TestBattle.Create(42), Ticks);
        ulong actual = hashes[^1].Total;
        Assert.That(actual, Is.EqualTo(GoldenFinalHash),
            $"Final hash is 0x{actual:x16}. If outcomes changed on purpose, update GoldenFinalHash and say why in the commit.");
    }

    /// <summary>Steps until <paramref name="untilTick"/>, applying the command script; returns the hash after each tick.</summary>
    private static List<StateHash> Run(BattleSimulation sim, int untilTick)
    {
        var hashes = new List<StateHash>();
        while (sim.State.Tick < untilTick)
        {
            Script(sim);
            sim.Step();
            hashes.Add(sim.ComputeHash());
        }

        return hashes;
    }

    /// <summary>Commands issued at fixed state ticks: same-tick ordering, a rejected command, and a far-future one.</summary>
    private static void Script(BattleSimulation sim)
    {
        var fighters = sim.State.Fighters;
        switch (sim.State.Tick)
        {
            case 0:
                sim.Enqueue(new NudgeCommand(0, fighters[0].Id, 3, -1));
                break;
            case 49:
                sim.Enqueue(new NudgeCommand(0, fighters[1].Id, 1, 1));
                sim.Enqueue(new NudgeCommand(1, fighters[2].Id, -2, 0));
                break;
            case 100:
                sim.EnqueueAt(260, new NudgeCommand(1, fighters[3].Id, 0, -4));
                break;
            case 120:
                sim.Enqueue(new NudgeCommand(0, fighters[3].Id, 5, 5)); // wrong side: rejected
                break;
        }
    }
}
