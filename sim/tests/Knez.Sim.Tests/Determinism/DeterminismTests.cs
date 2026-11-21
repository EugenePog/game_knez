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
    private const ulong GoldenFinalHash = 0x06296fb330d5703dUL;

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

    /// <summary>
    /// Final hash of <see cref="TestDefs.March"/> after 400 ticks (seed 42): marching, turning in place, arrival facing
    /// and slot following. Same rule as <see cref="GoldenFinalHash"/>: a change means movement outcomes changed.
    /// </summary>
    private const ulong GoldenMarchHash = 0x10c2cd4d2d5a1413UL;

    [Test]
    public void Golden_March_FinalHashMatchesRecordedValue()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.March(), 42));
        sim.StepTicks(400);
        ulong actual = sim.ComputeHash().Total;
        Assert.That(actual, Is.EqualTo(GoldenMarchHash),
            $"March hash is 0x{actual:x16}. If movement changed on purpose, update GoldenMarchHash and say why in the commit.");
    }

    /// <summary>
    /// Every tick's hash of <see cref="TestDefs.Crossing"/> (seed 42, 500 ticks) folded into one value: two friendly lines
    /// jostling through each other. A trace, not the final hash — the crossing settles exactly on the slots afterwards, so
    /// the end state alone didn't notice when separation or sliding changed. A change means crowding outcomes changed.
    /// </summary>
    private const ulong GoldenCrossingTrace = 0xf089e8ed66a45432UL;

    [Test]
    public void Golden_Crossing_EveryTickMatchesTheRecordedTrace()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.Crossing(), 42));
        ulong trace = 14695981039346656037UL; // FNV-1a over the per-tick totals
        for (int i = 0; i < 500; i++)
        {
            sim.Step();
            trace = unchecked((trace ^ sim.ComputeHash().Total) * 1099511628211UL);
        }

        Assert.That(trace, Is.EqualTo(GoldenCrossingTrace),
            $"Crossing trace is 0x{trace:x16}. If crowding changed on purpose, update GoldenCrossingTrace and say why in the commit.");
    }

    /// <summary>
    /// Every tick's hash of <see cref="TestDefs.AlliesThroughAHoldingLine"/> (seed 42, 700 ticks) folded into one value:
    /// a line squeezing through the ranks of an allied line that holds its ground (decided 2026-11-21).
    /// </summary>
    private const ulong GoldenAlliesThroughTrace = 0xf27fe6c6d262556aUL;

    [Test]
    public void Golden_AlliesThrough_EveryTickMatchesTheRecordedTrace()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.AlliesThroughAHoldingLine(), 42));
        ulong trace = 14695981039346656037UL;
        for (int i = 0; i < 700; i++)
        {
            sim.Step();
            trace = unchecked((trace ^ sim.ComputeHash().Total) * 1099511628211UL);
        }

        Assert.That(trace, Is.EqualTo(GoldenAlliesThroughTrace),
            $"Allies-through trace is 0x{trace:x16}. If crowding changed on purpose, update GoldenAlliesThroughTrace and say why in the commit.");
    }

    /// <summary>
    /// Every tick's hash of a line of 8 squeezing through a 3 m gate (<see cref="TestDefs.ThroughTheGate"/>, seed 42, 700
    /// ticks) folded into one value: walls, sliding along them and round their corners, and (since step 6) the route the
    /// line plans through a gap narrower than its frontage: it stops to regroup into 3 files, passes, and widens again.
    /// </summary>
    private const ulong GoldenGateTrace = 0x9cfa519d1f436e23UL;

    [Test]
    public void Golden_Gate_EveryTickMatchesTheRecordedTrace()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.ThroughTheGate(TestDefs.Line), 42));
        ulong trace = 14695981039346656037UL;
        for (int i = 0; i < 700; i++)
        {
            sim.Step();
            trace = unchecked((trace ^ sim.ComputeHash().Total) * 1099511628211UL);
        }

        Assert.That(trace, Is.EqualTo(GoldenGateTrace),
            $"Gate trace is 0x{trace:x16}. If wall movement changed on purpose, update GoldenGateTrace and say why in the commit.");
    }

    [Test]
    public void SnapshotRestore_MidCrossing_ContinuesExactlyLikeTheUninterruptedRun()
    {
        var whole = new BattleSimulation(BattleSetup.Create(TestDefs.Crossing(), 9));
        var expected = new List<StateHash>();
        for (int i = 0; i < 500; i++)
        {
            whole.Step();
            expected.Add(whole.ComputeHash());
        }

        var first = new BattleSimulation(BattleSetup.Create(TestDefs.Crossing(), 9));
        first.StepTicks(230); // the two lines are pushing through each other
        var restored = BattleSimulation.Restore(first.TakeSnapshot());
        var continued = new List<StateHash>();
        for (int i = 230; i < 500; i++)
        {
            restored.Step();
            continued.Add(restored.ComputeHash());
        }

        Assert.That(continued, Is.EqualTo(expected.Skip(230)));
    }

    [Test]
    public void SnapshotRestore_MidMarch_ContinuesExactlyLikeTheUninterruptedRun()
    {
        var whole = new BattleSimulation(BattleSetup.Create(TestDefs.March(), 7));
        var expected = new List<StateHash>();
        for (int i = 0; i < 400; i++)
        {
            whole.Step();
            expected.Add(whole.ComputeHash());
        }

        var first = new BattleSimulation(BattleSetup.Create(TestDefs.March(), 7));
        first.StepTicks(150); // both squads mid-move: the column is turning or marching, the line marching
        Assert.That(first.State.Squads.Select(s => s.Order.Kind), Has.All.EqualTo(Knez.Sim.Orders.SquadOrderKind.Move));
        var restored = BattleSimulation.Restore(first.TakeSnapshot());
        var continued = new List<StateHash>();
        for (int i = 150; i < 400; i++)
        {
            restored.Step();
            continued.Add(restored.ComputeHash());
        }

        Assert.That(continued, Is.EqualTo(expected.Skip(150)));
    }

    [Test]
    public void MoveSquadCommands_ThroughTheQueue_TwinRunAndSnapshotWithAPendingOrder_SameHashes()
    {
        // The script covers Validate/Apply; this covers MoveSquad as player input: queued, logged, carried by a snapshot.
        static List<StateHash> Play(BattleSimulation sim, int from, int until)
        {
            var hashes = new List<StateHash>();
            for (int tick = from; tick < until; tick++)
            {
                if (tick == 0)
                {
                    var line = sim.State.Squads[0];
                    sim.Enqueue(new Knez.Sim.Commands.MoveSquadCommand(0, line.Id, Knez.Sim.Numerics.FixVec2.FromInts(30, 25)));
                    sim.EnqueueAt(180, new Knez.Sim.Commands.MoveSquadCommand(0, line.Id, Knez.Sim.Numerics.FixVec2.FromInts(10, 20),
                        Knez.Sim.Numerics.Angle.FromDegrees(200)));
                }

                sim.Step();
                hashes.Add(sim.ComputeHash());
            }

            return hashes;
        }

        var a = Play(new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), 5)), 0, 400);
        var b = Play(new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), 5)), 0, 400);
        Assert.That(b, Is.EqualTo(a));

        var first = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), 5));
        Play(first, 0, 100);
        var snapshot = first.TakeSnapshot();
        Assert.That(snapshot.Commands.Pending, Is.Not.Empty, "the tick-180 order crosses the snapshot");
        var continued = Play(BattleSimulation.Restore(snapshot), 100, 400);
        Assert.That(continued, Is.EqualTo(a.Skip(100)));
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
