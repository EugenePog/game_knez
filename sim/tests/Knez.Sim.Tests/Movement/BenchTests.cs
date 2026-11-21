using Knez.Sim.Cli;
using Knez.Sim.Defs;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// M1 step 8: the movement meter, and the M1 exit criteria on the bench scenarios S1–S4 (battle-sim.md §10): no fighter
/// stuck longer than 2 s, overlap under 1 % of fighter-ticks. Tick time (p99 under 2 ms) is the CLI's <c>bench</c>
/// command's job: a timing test would be flaky.
/// </summary>
[TestFixture]
public class BenchTests
{
    private static IEnumerable<string> ExitScenarios() => ["s1-clash", "s2-gate", "s3-crossing", "s4-wheel"];

    [TestCaseSource(nameof(ExitScenarios))]
    [Category("Scenario")]
    [Category("Slow")]
    public void Bench_MeetsTheM1MovementCriteria(string name)
    {
        var scenario = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, "scenarios", "bench", name + ".json"));
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var meter = new MovementMeter();
        for (int i = 0; i < 900; i++)
        {
            sim.Step();
            meter.Observe(sim.State);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.FightersStuck, Is.Zero, $"longest stuck spell {meter.LongestStuck} ticks");
            Assert.That(meter.OverlappingTicks * 100, Is.LessThan(meter.FighterTicks), "overlap under 1 %");
            Assert.That(meter.InWallTicks, Is.Zero);
            Assert.That(OpenOrdersNotEngaged(sim.State), Is.Empty, "every order not held by an enemy press done in 45 s");
        });
    }

    /// <summary>Squads still on a move order without a man within fighting distance of an enemy: a jam.</summary>
    private static string[] OpenOrdersNotEngaged(BattleState state) => state.Squads
        .Where(s => s.Order.Kind == Orders.SquadOrderKind.Move)
        .Where(s => !s.Members.Select(id => state.FindFighter(id)!).Any(f => state.Fighters.Any(e => e.IsAlive && e.SideId != f.SideId
            && (e.Position - f.Position).Length < f.Radius + e.Radius + MovementMeter.EngagedGap)))
        .Select(s => s.Name).ToArray();

    [Test]
    [Category("Scenario")]
    public void S1_HeadOnLines_PressAndHold_EngagedNotStuck()
    {
        // Decided 2026-11-21: until combat (M3) a head-on press stands; the whole squad in contact is engaged.
        var scenario = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, "scenarios", "bench", "s1-clash.json"));
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var meter = new MovementMeter();
        sim.StepTicks(600);
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
            meter.Observe(sim.State);
        }

        var open = sim.State.Squads.Where(s => s.Order.Kind == Orders.SquadOrderKind.Move).Select(s => s.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(open, Is.EquivalentTo(new[] { "Hird line", "Raider line" }), "the head-on pair presses on");
            Assert.That(meter.EngagedTicks, Is.EqualTo(300 * 20), "both squads engaged every tick");
            Assert.That(meter.FightersStuck, Is.Zero);
        });
    }

    [Test]
    public void Meter_AManHeldFarFromHisSlot_IsStuckAfterTwoSeconds()
    {
        // One man of a line of 4 is held still 25 m from his slot (the test moves him back every tick).
        var state = BattleSetup.Create(new ScenarioDef("s", "", TestDefs.Map, [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(45, 35), 90)]), 1);
        var man = state.FighterList[0];
        var meter = new MovementMeter();
        for (int i = 0; i < 60; i++)
        {
            man.Position = FixVec2.FromInts(20, 10);
            meter.Observe(state);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.FightersStuck, Is.EqualTo(1));
            Assert.That(meter.LongestStuck, Is.EqualTo(60));
            Assert.That(meter.StuckFighterTicks, Is.EqualTo(60 - MovementMeter.StuckTicks));
            Assert.That(meter.LongestFarFromSlot, Is.EqualTo(60));
            Assert.That(meter.WorstSlotError, Is.GreaterThan(Fix.FromInt(20)));
        });
    }

    [Test]
    public void Meter_FarButMoving_DeadOrSquadless_IsNotStuck_FarStillCounts()
    {
        var state = BattleSetup.Create(new ScenarioDef("s", "", TestDefs.Map, [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(45, 35), 90)]), 1);
        var moving = state.FighterList[0];
        var dead = state.FighterList[1];
        var meter = new MovementMeter();
        dead.IsAlive = false;
        dead.Position = FixVec2.FromInts(5, 5);
        for (int i = 0; i < 60; i++)
        {
            moving.Position = new FixVec2(Fix.FromInt(10) + (Fix.Ratio(5, 100) * i), Fix.FromInt(10)); // 5 cm a tick, far away
            meter.Observe(state);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.FightersStuck, Is.Zero);
            Assert.That(meter.LongestFarFromSlot, Is.EqualTo(60), "orbiting or wandering far from the slot still shows");
            Assert.That(meter.FighterTicks, Is.EqualTo(60 * 3), "the dead don't count");
        });
    }

    [Test]
    public void Meter_AWholeSquadWithOneManNearAnEnemy_IsEngaged_NotStuck()
    {
        // Decided 2026-11-21: fighting distance is the body + 0.5 m, and the whole squad counts as engaged.
        var state = BattleSetup.Create(new ScenarioDef("s", "", TestDefs.Map,
        [
            TestDefs.Squad("A", 0, 4, FixVec2.FromInts(45, 35), 90),
            TestDefs.Squad("B", 1, 1, FixVec2.FromInts(10, 10), 90),
        ]), 1);
        var meter = new MovementMeter();
        for (int i = 0; i < 60; i++)
        {
            state.FighterList[0].Position = new FixVec2(Fix.FromInt(10) + Fix.Ratio(105, 100), Fix.FromInt(10)); // 0.45 m gap
            state.FighterList[1].Position = FixVec2.FromInts(15, 10); // far from his slot, not near the enemy
            meter.Observe(state);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.FightersStuck, Is.Zero);
            Assert.That(meter.EngagedTicks, Is.EqualTo(60 * 5), "both squads: the enemy is within fighting distance too");
        });
    }

    [Test]
    public void Meter_FedAnotherBattle_Throws()
    {
        var meter = new MovementMeter();
        meter.Observe(BattleSetup.Create(TestDefs.OneLine(), 1));
        Assert.Throws<InvalidOperationException>(() => meter.Observe(BattleSetup.Create(TestDefs.SmallBattle(), 1)));
    }
}
