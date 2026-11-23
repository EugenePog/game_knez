using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Combat;

/// <summary>
/// M3 step 4 (battle-sim.md §11): no hit points — a blow that gets through leaves a wound rolled from the weapon's row;
/// wounds add up (Light + Light = Heavy, anything on Heavy = Down); Light and Heavy cost blows and pace; Down is out of
/// the fight (decided 2026-11-22).
/// </summary>
[TestFixture]
public class WoundTests
{
    private static readonly CombatDef C = CombatDef.Standard;

    [TestCase(WoundLevel.None, WoundLevel.Light, WoundLevel.Light)]
    [TestCase(WoundLevel.None, WoundLevel.Heavy, WoundLevel.Heavy)]
    [TestCase(WoundLevel.None, WoundLevel.Down, WoundLevel.Down)]
    [TestCase(WoundLevel.Light, WoundLevel.Light, WoundLevel.Heavy)]
    [TestCase(WoundLevel.Light, WoundLevel.Heavy, WoundLevel.Heavy)]
    [TestCase(WoundLevel.Light, WoundLevel.Down, WoundLevel.Down)]
    [TestCase(WoundLevel.Heavy, WoundLevel.Light, WoundLevel.Down)]
    [TestCase(WoundLevel.Heavy, WoundLevel.Heavy, WoundLevel.Down)]
    public void Worsen_WoundsAddUp(WoundLevel before, WoundLevel blow, WoundLevel after)
    {
        Assert.That(Wounds.Worsen(before, blow), Is.EqualTo(after));
    }

    [TestCase(0, WoundLevel.Light)]
    [TestCase(649, WoundLevel.Light)]
    [TestCase(650, WoundLevel.Heavy)]
    [TestCase(929, WoundLevel.Heavy)]
    [TestCase(930, WoundLevel.Down)]
    [TestCase(999, WoundLevel.Down)]
    public void Severity_SwordRow_650_280_70(int roll, WoundLevel expected)
    {
        Assert.That(Wounds.Severity(C.FindWeapon("sword")!, roll), Is.EqualTo(expected));
    }

    [Test]
    public void PenaltyAndPace_ByWound()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Wounds.HitPenalty(C, WoundLevel.None), Is.Zero);
            Assert.That(Wounds.HitPenalty(C, WoundLevel.Light), Is.EqualTo(40));
            Assert.That(Wounds.HitPenalty(C, WoundLevel.Heavy), Is.EqualTo(120));
            Assert.That(Wounds.Pace(C, WoundLevel.None), Is.EqualTo(Fix.One));
            Assert.That(Wounds.Pace(C, WoundLevel.Light), Is.EqualTo(C.LightWoundPace));
            Assert.That(Wounds.Pace(C, WoundLevel.Heavy), Is.EqualTo(C.HeavyWoundPace));
        });
    }

    private static BattleSimulation Pair(ulong seed = 1)
    {
        var scenario = new ScenarioDef("test-pair", "test", TestDefs.Map,
        [
            TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90),
            TestDefs.Squad("b", 1, 1, FixVec2.FromInts(20, 11), 270),
        ]);
        return new BattleSimulation(BattleSetup.Create(scenario, seed));
    }

    [Test]
    public void Chance_WoundedAttacker_StrikesWorse()
    {
        var sim = Pair();
        var attacker = sim.State.FighterList[0];
        var defender = sim.State.FighterList[1];
        int whole = Blows.Chance(sim.State, attacker, defender, out _);
        attacker.Wound = WoundLevel.Light;
        int light = Blows.Chance(sim.State, attacker, defender, out _);
        attacker.Wound = WoundLevel.Heavy;
        int heavy = Blows.Chance(sim.State, attacker, defender, out _);
        Assert.Multiple(() =>
        {
            Assert.That(whole, Is.EqualTo(140));
            Assert.That(light, Is.EqualTo(100));
            Assert.That(heavy, Is.EqualTo(C.MinHit), "140 − 120 = 20, the floor");
        });
    }

    [Test]
    public void Pace_WoundedMan_SlowsHisSquad()
    {
        var scenario = new ScenarioDef("test-walker", "test", TestDefs.Map, [TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90)]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var squad = sim.State.Squads[0];
        sim.State.FighterList[0].Wound = WoundLevel.Heavy;
        sim.Enqueue(new MoveSquadCommand(0, squad.Id, FixVec2.FromInts(20, 30), Angle.FromDegrees(90)));
        sim.StepTicks(5); // route planned, marching
        FixVec2 before = squad.Anchor;
        sim.Step();
        Fix expected = TestDefs.Warrior.WalkPerTick * C.HeavyWoundPace;
        Assert.That((double)Math.Abs((squad.Anchor - before).Length.Raw - expected.Raw), Is.LessThanOrEqualTo(2), "raw units");
    }

    private static (List<BlowStruck> blows, List<Wounded> wounds) Fight(BattleSimulation sim, int ticks, Action<BattleSimulation>? eachTick = null)
    {
        var blows = new List<BlowStruck>();
        var wounds = new List<Wounded>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            blows.AddRange(sim.State.Events.OfType<BlowStruck>());
            wounds.AddRange(sim.State.Events.OfType<Wounded>());
            eachTick?.Invoke(sim);
        }

        return (blows, wounds);
    }

    [Test]
    public void Duel_EveryLandedBlowWounds_UntilOneFalls_AndIsOutOfTheFight()
    {
        var sim = Pair(seed: 3);
        var (blows, wounds) = Fight(sim, 300 * BattleState.TicksPerSecond, s =>
        {
            // No living man is ever left targeting the fallen, not even for the rest of the tick.
            foreach (var f in s.State.Fighters.Where(f => f.IsAlive && f.IsEngaged))
            {
                Assert.That(s.State.FindFighter(f.Target)!.IsAlive, Is.True, $"tick {s.State.Tick}");
            }
        });
        var fallen = sim.State.Fighters.Where(f => !f.IsAlive).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(fallen, Has.Length.EqualTo(1), "one of them fell");
            Assert.That(blows.Count(b => b.Landed), Is.EqualTo(wounds.Count), "every blow that got through left a wound");
            Assert.That(wounds.Last().Now, Is.EqualTo(WoundLevel.Down));
            Assert.That(wounds.Last().Fighter, Is.EqualTo(fallen[0].Id));
            Assert.That(wounds.Count(w => w.Now == WoundLevel.Down), Is.EqualTo(1), "a man falls once");
            Assert.That(fallen[0].SquadId.IsNone, Is.True, "the fallen leave their squad");
            Assert.That(fallen[0].Target.IsNone && fallen[0].Action == FighterAction.Idle, Is.True, "the fallen fight no one");
            Assert.That(sim.State.Fighters.Single(f => f.IsAlive).IsEngaged, Is.False, "the survivor has no one left to fight");
        });

        // Each wound follows the rule from the one before.
        var levels = new Dictionary<EntityId, WoundLevel>();
        foreach (var w in wounds)
        {
            var before = levels.GetValueOrDefault(w.Fighter, WoundLevel.None);
            Assert.That(w.Now, Is.EqualTo(Wounds.Worsen(before, w.Blow)), $"tick {w.Tick}");
            levels[w.Fighter] = w.Now;
        }
    }

    [Test]
    public void SameTick_BothDecidingBlowsLand_BothFall()
    {
        // Both men heavily wounded: the first blow to get through puts its man down. They strike on the same ticks, so
        // the first tick on which both blows land must fell both — the lower id must not win by being wounded first.
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var sim = Pair(seed);
            sim.State.FighterList[0].Wound = WoundLevel.Heavy;
            sim.State.FighterList[1].Wound = WoundLevel.Heavy;
            for (int t = 0; t < 120 * BattleState.TicksPerSecond && sim.State.Fighters.All(f => f.IsAlive); t++)
            {
                sim.Step();
                var landed = sim.State.Events.OfType<BlowStruck>().Count(b => b.Landed);
                if (landed == 2)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(sim.State.Fighters.All(f => !f.IsAlive), Is.True, $"seed {seed}: both fell");
                        Assert.That(sim.State.Events.OfType<Wounded>().Count(w => w.Now == WoundLevel.Down), Is.EqualTo(2));
                    });
                    return;
                }
            }
        }

        Assert.Inconclusive("no seed gave a tick with both blows landing");
    }

    [Test]
    public void Duels_EndWithinFiveMinutes_EachSideFallsAFairShare()
    {
        int ended = 0;
        int aFalls = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var sim = Pair(seed);
            Fight(sim, 300 * BattleState.TicksPerSecond);
            var a = sim.State.FighterList[0];
            var b = sim.State.FighterList[1];
            ended += !a.IsAlive || !b.IsAlive ? 1 : 0;
            aFalls += a.IsAlive ? 0 : 1;
        }

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.EqualTo(40), "every duel ends within 5 minutes");
            Assert.That(aFalls, Is.InRange(10, 30), "the lower id has no edge");
        });
    }

    [Test]
    public void Pace_WoundedMan_CatchesUpWithHisSlotSlower()
    {
        // A heavily wounded man 5 m from his slot runs to it at 70 % of his run speed.
        var scenario = new ScenarioDef("test-runner", "test", TestDefs.Map, [TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90)]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var man = sim.State.FighterList[0];
        man.Wound = WoundLevel.Heavy;
        man.Position = FixVec2.FromInts(20, 5);
        sim.Step();
        Fix cap = TestDefs.Warrior.RunPerTick * C.HeavyWoundPace;
        Assert.That((double)Math.Abs(man.Velocity.Length.Raw - cap.Raw), Is.LessThanOrEqualTo(2), "raw units: at the wounded cap");
    }

    [Test]
    public void TargetFalls_HisEnemyTurnsToTheNextInReach_SameTick()
    {
        // Man 0 faces two enemies in reach; when the nearer one falls, man 0 targets the other at once, so his squad's
        // lock never opens for a tick.
        var scenario = new ScenarioDef("test-two-on-one", "test", TestDefs.Map,
        [
            TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90),
            // a at (20, 10); b straight ahead, gap 0.35 m; c ahead-right, gap ~0.36 m: both in sword reach, b nearer.
            new SquadSpawnDef("b", 1, TestDefs.Warrior, 1, TestDefs.Line, new FixVec2(Fix.FromInt(20), Fix.Ratio(1095, 100)), Angle.FromDegrees(270)),
            new SquadSpawnDef("c", 1, TestDefs.Warrior, 1, TestDefs.Line, new FixVec2(Fix.Ratio(2085, 100), Fix.Ratio(1045, 100)), Angle.FromDegrees(208)),
        ]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var a = sim.State.FighterList[0];
        var b = sim.State.FighterList[1];
        var c = sim.State.FighterList[2];
        b.Wound = WoundLevel.Heavy;
        sim.Step();
        Assume.That(a.Target, Is.EqualTo(b.Id), "the setup: a fights b, the nearer");
        for (int t = 0; t < 120 * BattleState.TicksPerSecond && b.IsAlive && a.IsAlive; t++)
        {
            sim.Step();
        }

        Assume.That(!b.IsAlive && a.IsAlive, "the setup: b fell first");
        Assert.That(a.Target, Is.EqualTo(c.Id), "the tick b fell, a already faces c");
    }

    [Test]
    [Category("Determinism")]
    public void Wounds_SnapshotRestore_ContinuesIdentically()
    {
        var sim = Pair(seed: 3);
        Fight(sim, 20 * BattleState.TicksPerSecond);
        var restored = BattleSimulation.Restore(sim.TakeSnapshot());
        Fight(sim, 60 * BattleState.TicksPerSecond);
        Fight(restored, 60 * BattleState.TicksPerSecond);
        Assert.That(restored.ComputeHash(), Is.EqualTo(sim.ComputeHash()));
    }
}
