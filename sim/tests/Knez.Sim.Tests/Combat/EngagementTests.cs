using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Combat;

/// <summary>
/// M3 step 2 (battle-sim.md §11): engagement within weapon reach, the melee lock, and stepping back out of it (decided
/// 2026-11-22: an engaged squad only withdraws backward, facing the enemy, at step-back pace).
/// </summary>
[TestFixture]
public class EngagementTests
{
    private static readonly CombatDef C = CombatDef.Standard;

    /// <summary>An experienced man with a spear, round shield, padded coat and cap.</summary>
    private static SquadMenDef Spearman { get; } = TestDefs.Men(1, kit: TestDefs.KitOf("spear-shield-padded"))[0];

    /// <summary>Lone men (squads of one) at the given points; side 0 faces north, side 1 south.</summary>
    private static BattleSimulation Men(params (int side, int xCm, int yCm, SquadMenDef? def)[] men)
    {
        var squads = men.Select((m, i) => new SquadSpawnDef($"m{i}", m.side, TestDefs.Man, [m.def ?? TestDefs.Men(1)[0]], TestDefs.Line,
            new FixVec2(Fix.Ratio(m.xCm, 100), Fix.Ratio(m.yCm, 100)), Angle.FromDegrees(m.side == 0 ? 90 : 270))).ToArray();
        return new BattleSimulation(BattleSetup.Create(new ScenarioDef("test-men", "test", TestDefs.Map, squads), seed: 1));
    }

    private static Fighter Man(BattleSimulation sim, int index) => sim.State.FighterList[index];

    [Test]
    public void Targeting_EnemyWithinSwordReach_TargetsEachOther()
    {
        // Centres 1.0 m apart, bodies 0.3 m: a gap of 0.4 m, inside the sword's 0.5 m.
        var sim = Men((0, 2000, 1000, null), (1, 2000, 1100, null));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Man(sim, 0).Target, Is.EqualTo(Man(sim, 1).Id));
            Assert.That(Man(sim, 1).Target, Is.EqualTo(Man(sim, 0).Id));
            Assert.That(Man(sim, 0).IsEngaged, Is.True);
        });
    }

    [Test]
    public void Targeting_EnemyJustBeyondReach_NoTarget()
    {
        var sim = Men((0, 2000, 1000, null), (1, 2000, 1115, null)); // gap 0.55 m
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Man(sim, 0).Target.IsNone, Is.True);
            Assert.That(Man(sim, 1).IsEngaged, Is.False);
        });
    }

    [Test]
    public void Targeting_AllyInReach_IsNoTarget()
    {
        var sim = Men((0, 2000, 1000, null), (0, 2100, 1000, null));
        sim.Step();
        Assert.That(Man(sim, 0).Target.IsNone, Is.True);
    }

    [Test]
    public void Targeting_TwoEnemiesInReach_NearestWins_TiesByLowerId()
    {
        // Man 0 at (20, 10); men 1 and 2 equally near (1 m), man 3 nearer (0.9 m) but listed last.
        var sim = Men((0, 2000, 1000, null), (1, 1900, 1000, null), (1, 2100, 1000, null), (1, 2000, 1090, null));
        sim.Step();
        Assert.That(Man(sim, 0).Target, Is.EqualTo(Man(sim, 3).Id), "nearest");

        var tie = Men((0, 2000, 1000, null), (1, 1900, 1000, null), (1, 2100, 1000, null));
        tie.Step();
        Assert.That(Man(tie, 0).Target, Is.EqualTo(Man(tie, 1).Id), "equal distance: lower id");
    }

    [Test]
    public void Targeting_Spear_ReachesAcrossTheGapOfARank()
    {
        // Gap 1.4 m: out of a sword's reach, inside the spear's 1.5 m. The swordsman is not engaged; the spearman is.
        var sim = Men((0, 2000, 1000, Spearman), (1, 2000, 1200, null));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Man(sim, 0).Target, Is.EqualTo(Man(sim, 1).Id));
            Assert.That(Man(sim, 1).Target.IsNone, Is.True);
        });
    }

    /// <summary>Two lines of 4 (two ranks of two), 6 m apart, each ordered to the other's ground.</summary>
    private static BattleSimulation HeadOn()
    {
        var scenario = new ScenarioDef("test-head-on", "test", TestDefs.Map,
        [
            TestDefs.Squad("Blue", 0, 4, FixVec2.FromInts(20, 10), 90),
            TestDefs.Squad("Red", 1, 4, FixVec2.FromInts(20, 16), 270),
        ], combat: TestDefs.Harmless); // the lock, not the fight: nobody gets hurt, so nobody breaks
        var sim = new BattleSimulation(BattleSetup.Create(scenario, seed: 1));
        sim.Enqueue(new MoveSquadCommand(0, sim.State.Squads[0].Id, FixVec2.FromInts(20, 20), Angle.FromDegrees(90)));
        sim.Enqueue(new MoveSquadCommand(1, sim.State.Squads[1].Id, FixVec2.FromInts(20, 6), Angle.FromDegrees(270)));
        return sim;
    }

    private static void Run(BattleSimulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
        }
    }

    private static IEnumerable<Fighter> Members(BattleSimulation sim, int squad) =>
        sim.State.Fighters.Where(f => f.SquadId == sim.State.Squads[squad].Id);

    [Test]
    public void Lock_SquadsMarchingHeadOn_StopInContact_NobodySlipsThrough()
    {
        var sim = HeadOn();
        Run(sim, 15 * BattleState.TicksPerSecond);
        var blue = Members(sim, 0).ToArray();
        var red = Members(sim, 1).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(blue.Max(f => f.Position.Y), Is.LessThan(red.Min(f => f.Position.Y)), "the lines stay apart: nobody passed through");
            Assert.That(blue.Count(f => f.IsEngaged), Is.GreaterThanOrEqualTo(2), "Blue's front rank is engaged");
            Assert.That(red.Count(f => f.IsEngaged), Is.GreaterThanOrEqualTo(2), "Red's front rank is engaged");
            Assert.That(sim.State.Squads[0].Order.Kind, Is.EqualTo(SquadOrderKind.Move), "the order waits, it isn't dropped");
            Assert.That(sim.State.Squads[0].Velocity, Is.EqualTo(FixVec2.Zero), "an engaged squad doesn't march");
        });

        // Locked: another second changes nothing but shuffling.
        var before = sim.State.Fighters.Select(f => f.Position).ToArray();
        Run(sim, BattleState.TicksPerSecond);
        var moved = sim.State.Fighters.Select((f, i) => (f.Position - before[i]).Length).Max();
        Assert.That(moved, Is.LessThan(Fix.Ratio(5, 100)), "men in a lock stand (they only shuffle)");
    }

    [Test]
    public void Lock_EngagedMen_StandWithinReach_NotPressedTogether()
    {
        var sim = HeadOn();
        Run(sim, 15 * BattleState.TicksPerSecond);
        foreach (var man in sim.State.Fighters.Where(f => f.IsEngaged))
        {
            var foe = sim.State.FindFighter(man.Target)!;
            Fix gap = (foe.Position - man.Position).Length - man.Radius - foe.Radius;
            Assert.That(gap, Is.InRange(Fix.Zero, man.Kit.Weapon.Reach), $"fighter {man.Id.Value}");
        }
    }

    [Test]
    public void Withdraw_OrderBackward_StepsBackFacingTheEnemy_AtStepBackPace_UntilClear()
    {
        var sim = HeadOn();
        Run(sim, 15 * BattleState.TicksPerSecond);
        var blue = sim.State.Squads[0];
        Angle facing = blue.Facing;
        sim.Enqueue(new MoveSquadCommand(0, blue.Id, new FixVec2(blue.Anchor.X, blue.Anchor.Y - Fix.FromInt(6)), Angle.FromDegrees(90)));
        Fix pace = TestDefs.Man.WalkPerTick * MovementDef.Standard.StepBackPace;

        bool clear = false;
        for (int t = 0; t < 10 * BattleState.TicksPerSecond && !clear; t++)
        {
            FixVec2 anchor = blue.Anchor;
            sim.Step();
            bool engaged = Members(sim, 0).Any(f => f.IsEngaged);
            if (engaged)
            {
                Assert.That((blue.Anchor - anchor).Length, Is.LessThanOrEqualTo(pace + Fix.FromRaw(2)), $"tick {sim.State.Tick}: step-back pace");
                Assert.That(blue.Facing, Is.EqualTo(facing), "faces the enemy while stepping back");
                Assert.That(blue.Anchor.Y, Is.LessThanOrEqualTo(anchor.Y), "only backward");
            }

            clear = !engaged && Members(sim, 1).All(f => !f.IsEngaged);
        }

        Assert.That(clear, Is.True, "stepping back takes the squad out of reach");
    }

    [Test]
    public void Withdraw_OrderSideways_WaitsWhileEngaged()
    {
        var sim = HeadOn();
        Run(sim, 15 * BattleState.TicksPerSecond);
        var blue = sim.State.Squads[0];
        FixVec2 anchor = blue.Anchor;
        Angle facing = blue.Facing;
        sim.Enqueue(new MoveSquadCommand(0, blue.Id, new FixVec2(anchor.X + Fix.FromInt(10), anchor.Y), Angle.FromDegrees(0)));
        Run(sim, 3 * BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(blue.Anchor, Is.EqualTo(anchor), "no march along the enemy's front");
            Assert.That(blue.Facing, Is.EqualTo(facing), "no turning in contact");
            Assert.That(blue.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "the order waits for contact to end");
        });
    }

    [Test]
    public void Lock_SnapshotRestore_ContinuesIdentically()
    {
        var sim = HeadOn();
        Run(sim, 12 * BattleState.TicksPerSecond);
        var snapshot = sim.TakeSnapshot();
        Run(sim, 2 * BattleState.TicksPerSecond);
        ulong expected = sim.ComputeHash().Total;

        var restored = BattleSimulation.Restore(snapshot);
        Run(restored, 2 * BattleState.TicksPerSecond);
        Assert.That(restored.ComputeHash().Total, Is.EqualTo(expected));
    }

    [Test]
    public void Targeting_DeadEnemy_IsNoTarget_AndTheDeadHaveNone()
    {
        var sim = Men((0, 2000, 1000, null), (1, 2000, 1100, null));
        sim.Step();
        Man(sim, 1).IsAlive = false;
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Man(sim, 0).Target.IsNone, Is.True, "a dead enemy is no target");
            Assert.That(Man(sim, 1).Target.IsNone, Is.True, "the dead fight no one");
        });
    }

    /// <summary>Smallest edge-to-edge gap between a man of squad <paramref name="a"/> and a man of squad <paramref name="b"/>.</summary>
    private static Fix Gap(BattleSimulation sim, int a, int b) =>
        Members(sim, a).SelectMany(x => Members(sim, b).Select(y => (y.Position - x.Position).Length - x.Radius - y.Radius)).Min();

    private static readonly Fix Clearance = TestDefs.Kit.Weapon.Reach + MovementDef.Standard.WithdrawClearance;

    [Test]
    public void Withdraw_FromAnAdvancingEnemy_KeepsFacingIt_AndEndsAtTheTarget()
    {
        // Red keeps its order through Blue's ground, so it follows Blue back and stays within the clearance: Blue must step
        // back facing it all the way (the reviewer's case: facing about once out of reach left its back to the enemy).
        var sim = HeadOn();
        Run(sim, 15 * BattleState.TicksPerSecond);
        var blue = sim.State.Squads[0];
        Angle facing = blue.Facing;
        var target = new FixVec2(blue.Anchor.X, blue.Anchor.Y - Fix.FromInt(4));
        sim.Enqueue(new MoveSquadCommand(0, blue.Id, target, Angle.FromDegrees(90)));
        for (int t = 0; t < 20 * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            if (Gap(sim, 0, 1) <= Clearance)
            {
                Assert.That(blue.Facing, Is.EqualTo(facing), $"tick {sim.State.Tick}: an enemy within reach + 3 m, and Blue turned");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(blue.Order.Kind, Is.EqualTo(SquadOrderKind.None), "the withdrawal ends at its target");
            Assert.That(blue.Anchor, Is.EqualTo(target));
            Assert.That(blue.Withdrawing, Is.False);
        });
    }

    [Test]
    public void Withdraw_FromAHoldingEnemy_StepsClear_ThenTurnsAndMarches()
    {
        var scenario = new ScenarioDef("test-into-holders", "test", TestDefs.Map,
        [
            TestDefs.Squad("Blue", 0, 4, FixVec2.FromInts(20, 10), 90),
            TestDefs.Squad("Red", 1, 4, FixVec2.FromInts(20, 16), 270),
        ], combat: TestDefs.Harmless);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, seed: 1));
        var blue = sim.State.Squads[0];
        sim.Enqueue(new MoveSquadCommand(0, blue.Id, FixVec2.FromInts(20, 20), Angle.FromDegrees(90)));
        Run(sim, 10 * BattleState.TicksPerSecond);
        Assume.That(Members(sim, 0).Any(f => f.IsEngaged), "the setup: Blue marched into the holding Red line");

        Angle facing = blue.Facing;
        sim.Enqueue(new MoveSquadCommand(0, blue.Id, FixVec2.FromInts(20, 4), Angle.FromDegrees(90)));
        bool turned = false;
        for (int t = 0; t < 40 * BattleState.TicksPerSecond && blue.Order.Kind != SquadOrderKind.None; t++)
        {
            sim.Step();
            if (blue.Facing != facing && !turned)
            {
                turned = true;
                Assert.That(Gap(sim, 0, 1), Is.GreaterThan(Clearance), $"tick {sim.State.Tick}: turned only once clear");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(turned, Is.True, "once clear it marched as usual (faced about)");
            Assert.That(blue.Order.Kind, Is.EqualTo(SquadOrderKind.None), "and the order finished");
            Assert.That(blue.Facing, Is.EqualTo(Angle.FromDegrees(90)));
            Assert.That(sim.State.Fighters.Any(f => f.IsEngaged), Is.False);
        });
    }
}
