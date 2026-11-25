using Knez.Sim.Cli;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Cli;

/// <summary>M3 step 7: the D-026 measures of one run (<see cref="OutcomeMeter"/>), read up to the decision.</summary>
[TestFixture]
public class OutcomeMeterTests
{
    private static readonly string S6 = Path.Combine(RepoPaths.Content, "scenarios", "bench", "s6-lines.json");

    [Test]
    public void Observe_S6_AgreesWithTheBattleLog_AndStopsAtTheDecision()
    {
        var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(S6), 1));
        var meter = new OutcomeMeter(sim.State);
        var log = new BattleLog();
        for (int i = 0; i < 1200 && !meter.Decided; i++)
        {
            sim.Step();
            meter.Observe(sim.State);
            log.Observe(sim.State);
        }

        Assert.That(meter.Decided, Is.True, "S6 seed 1 is decided within a minute");
        int blows = meter.Blows;
        int fallen0 = meter.FallenOf(0);
        int endTick = meter.EndTick;
        for (int i = 0; i < 200; i++)
        {
            sim.Step();
            meter.Observe(sim.State);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.EndTick, Is.EqualTo(log.EndTick));
            Assert.That(meter.Winner, Is.EqualTo(log.Winner));
            Assert.That(blows, Is.EqualTo(log.Blows));
            Assert.That(meter.Landed, Is.EqualTo(log.Landed));
            Assert.That(meter.End, Is.EqualTo(BattleEnd.Rout), "the losers ran");
            Assert.That(meter.FirstBlowTick, Is.InRange(1, endTick));
            Assert.That(meter.StartOf(0) + meter.StartOf(1), Is.EqualTo(16));
            Assert.That((meter.Blows, meter.FallenOf(0), meter.EndTick, meter.LastTick), Is.EqualTo((blows, fallen0, endTick, endTick)),
                "nothing after the decision counts");
        });
    }

    [Test]
    public void Observe_LossAtBreak_IsTheShareOfTheBrokenSquadDownWhenItBroke()
    {
        // S6 has one squad a side, so the broken squad's men down are its side's men down at that tick.
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(S6), seed));
            var meter = new OutcomeMeter(sim.State);
            var expected = new List<double>();
            while (!meter.Decided && sim.State.Tick < 1200)
            {
                sim.Step();
                meter.Observe(sim.State);
                foreach (var broke in sim.State.Events.OfType<Events.SquadBroke>().Where(b => !b.Reformed))
                {
                    int side = sim.State.FindSquad(broke.Squad)!.SideId;
                    expected.Add(sim.State.Fighters.Count(f => f.SideId == side && f.Wound == WoundLevel.Down) / 8.0);
                }
            }

            Assert.That(meter.LossAtBreak, Is.EqualTo(expected), $"seed {seed}");
        }
    }

    [Test]
    public void Observe_TwoSquadsASide_EachBreakIsCountedAgainstItsOwnStartingMen()
    {
        // S7: side 0 has two squads. A fallen man leaves his squad (cleanup), so the meter must remember who started where.
        string s7 = Path.Combine(RepoPaths.Content, "scenarios", "bench", "s7-flank.json");
        int breaks = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(s7), seed));
            var startedIn = sim.State.Fighters.ToDictionary(f => f.Id, f => f.SquadId);
            var meter = new OutcomeMeter(sim.State);
            var expected = new List<double>();
            var seen = new HashSet<EntityId>();
            while (!meter.Decided && sim.State.Tick < 1200)
            {
                sim.Step();
                meter.Observe(sim.State);
                foreach (var broke in sim.State.Events.OfType<Events.SquadBroke>().Where(b => !b.Reformed && seen.Add(b.Squad)))
                {
                    var men = startedIn.Where(p => p.Value == broke.Squad).Select(p => p.Key).ToArray();
                    expected.Add(men.Count(id => sim.State.FindFighter(id)!.Wound == WoundLevel.Down) / (double)men.Length);
                }
            }

            breaks += expected.Count;
            Assert.That(meter.LossAtBreak, Is.EqualTo(expected), $"seed {seed}");
        }

        Assert.That(breaks, Is.GreaterThanOrEqualTo(30), "every seed broke a squad");
    }

    [Test]
    public void Observe_ASquadBreaksAgainAfterReforming_OnlyItsFirstBreakCounts()
    {
        // Built by hand: three squads of four; squad A breaks with one man down, re-forms, breaks again with two down; B
        // breaks with none down. Events and wounds are set directly (no step), as the sim's stages would leave them.
        var state = BattleSetup.Create(new ScenarioDef("test-breaks", "test", TestDefs.Map,
        [
            TestDefs.Squad("A", 0, 4, FixVec2.FromInts(15, 10), 90),
            TestDefs.Squad("B", 0, 4, FixVec2.FromInts(30, 10), 90),
            TestDefs.Squad("C", 1, 4, FixVec2.FromInts(30, 30), 270),
        ]), 1);
        var meter = new OutcomeMeter(state);
        var a = state.Squads[0];
        var b = state.Squads[1];
        var aMen = state.Fighters.Where(f => f.SquadId == a.Id).ToArray();

        void Down(Units.Fighter man)
        {
            man.Wound = WoundLevel.Down;
            man.IsAlive = false;
        }

        state.BeginTick();
        Down(aMen[0]);
        state.AddEvent(new Events.SquadBroke(state.Tick, a.Id, reformed: false, inFight: 4, running: 3));
        meter.Observe(state);
        state.BeginTick();
        state.AddEvent(new Events.SquadBroke(state.Tick, a.Id, reformed: true, inFight: 3, running: 1));
        meter.Observe(state);
        state.BeginTick();
        Down(aMen[1]);
        state.AddEvent(new Events.SquadBroke(state.Tick, a.Id, reformed: false, inFight: 2, running: 2));
        state.AddEvent(new Events.SquadBroke(state.Tick, b.Id, reformed: false, inFight: 4, running: 3));
        meter.Observe(state);

        Assert.Multiple(() =>
        {
            Assert.That(meter.LossAtBreak, Is.EqualTo(new[] { 0.25, 0.0 }), "A's first break (1 of 4), then B's (0 of 4)");
            Assert.That(meter.FallenOf(0), Is.EqualTo(2));
            Assert.That(meter.Decided, Is.False);
        });
    }

    [Test]
    public void Observe_Duels_AnnihilationExactlyWhenTheLoserIsDown()
    {
        // One man a side: most duels end with a man down, some with a man running before he falls.
        var ends = new List<BattleEnd>();
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var scenario = new ScenarioDef("test-pair", "test", TestDefs.Map,
            [
                TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90),
                TestDefs.Squad("b", 1, 1, FixVec2.FromInts(20, 11), 270),
            ]);
            var sim = new BattleSimulation(BattleSetup.Create(scenario, seed));
            var meter = new OutcomeMeter(sim.State);
            while (!meter.Decided && sim.State.Tick < 300 * BattleState.TicksPerSecond)
            {
                sim.Step();
                meter.Observe(sim.State);
            }

            Assert.That(meter.Decided, Is.True, $"seed {seed} decided in 5 minutes");
            ends.Add(meter.End);
            if (meter.Winner >= 0)
            {
                bool loserDown = sim.State.Fighters.Where(f => f.SideId != meter.Winner).All(f => f.Wound == WoundLevel.Down);
                Assert.That(meter.End, Is.EqualTo(loserDown ? BattleEnd.Annihilation : BattleEnd.Rout), $"seed {seed}");
            }
        }

        Assert.That(ends, Does.Contain(BattleEnd.Annihilation));
    }

    [Test]
    public void Observe_NoFight_StaysUndecided()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.March(), 1));
        var meter = new OutcomeMeter(sim.State);
        for (int i = 0; i < 100; i++)
        {
            sim.Step();
            meter.Observe(sim.State);
        }

        Assert.Multiple(() =>
        {
            Assert.That(meter.Decided, Is.False);
            Assert.That(meter.End, Is.EqualTo(BattleEnd.Time));
            Assert.That(meter.Winner, Is.EqualTo(BattleState.Undecided));
            Assert.That(meter.FirstBlowTick, Is.EqualTo(-1));
            Assert.That(meter.LastTick, Is.EqualTo(100));
            Assert.That(meter.LossAtBreak, Is.Empty);
        });
    }
}
