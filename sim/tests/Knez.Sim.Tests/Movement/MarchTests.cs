using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// M1 step 3 rules (battle-sim.md §10): a squad turns in place until the target is within 30° of its front, marches at
/// its slowest member's walk speed, turns to the order's facing on arrival; members follow their slots and settle
/// exactly on them. Test numbers: walk 1.4 m/s = 4587 raw/tick, line turns 45°/s = 410 units/tick.
/// </summary>
[TestFixture]
public class MarchTests
{
    private const long WalkRawPerTick = 4587;

    private static BattleSimulation OneLine() => new(BattleSetup.Create(TestDefs.OneLine(), seed: 1));

    private static Squad Line(BattleSimulation sim) => sim.State.Squads[0];

    private static void Order(BattleSimulation sim, int x, int y, int? facingDegrees = null) =>
        sim.Enqueue(new MoveSquadCommand(0, Line(sim).Id, FixVec2.FromInts(x, y),
            facingDegrees.HasValue ? Angle.FromDegrees(facingDegrees.Value) : null));

    private static FixVec2 SlotOf(BattleState state, Fighter fighter)
    {
        var squad = state.FindSquad(fighter.SquadId)!;
        return FormationLayout.SlotPosition(squad.Formation, squad.Members.Count, fighter.SlotIndex, squad.Anchor, squad.Facing);
    }

    private static double WorstSlotError(BattleState state) =>
        state.Fighters.Max(f => (SlotOf(state, f) - f.Position).Length.Raw) / 65536.0;

    [Test]
    public void March_StraightAhead_ArrivesOnTheTickWalkSpeedPredicts()
    {
        var sim = OneLine();
        Order(sim, 20, 20); // 10 m north, already facing north: no turn
        int expectedTicks = (int)Math.Ceiling(10 * 65536.0 / WalkRawPerTick); // 143

        sim.StepTicks(expectedTicks - 1);
        Assert.That(Line(sim).Anchor, Is.Not.EqualTo(FixVec2.FromInts(20, 20)));
        Assert.That(Line(sim).Velocity.Length.Raw, Is.EqualTo(WalkRawPerTick).Within(1));

        sim.Step();
        Assert.That(Line(sim).Anchor, Is.EqualTo(FixVec2.FromInts(20, 20)));
    }

    [Test]
    public void March_MembersMoveWithTheirSlots_AndFaceTheFront()
    {
        var sim = OneLine();
        Order(sim, 20, 30);
        double worst = 0;
        for (int i = 0; i < 300; i++)
        {
            sim.Step();
            worst = Math.Max(worst, WorstSlotError(sim.State));
            Assert.That(sim.State.Fighters.Select(f => f.Facing), Has.All.EqualTo(Angle.FromDegrees(90)), $"tick {sim.State.Tick}");
        }

        Assert.That(worst, Is.LessThan(0.01), "metres a member falls out of its slot while marching");
    }

    [Test]
    public void Order_TargetBehind_TurnsInPlaceFirst_ThenMarches()
    {
        var sim = OneLine();
        Order(sim, 20, 0); // straight behind: 180° to turn, march once within 30°
        sim.StepTicks(60);
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Anchor, Is.EqualTo(FixVec2.FromInts(20, 10)), "still turning in place");
            Assert.That(Line(sim).Facing, Is.Not.EqualTo(Angle.FromDegrees(90)));
            Assert.That(Line(sim).Velocity, Is.EqualTo(FixVec2.Zero));
            Assert.That(Line(sim).Spin, Is.EqualTo(-410), "half a turn goes clockwise at 45°/s");
        });

        sim.StepTicks(20); // (180° − 30°) at 45°/s = 3.33 s = 67 ticks
        Assert.That(Line(sim).Anchor.Y, Is.LessThan(Fix.FromInt(10)), "marching south");
    }

    [Test]
    public void Order_WithFacing_TurnsOnArrival_ThenHolds_AndMembersSettleExactlyOnTheirSlots()
    {
        var sim = OneLine();
        Order(sim, 20, 20, facingDegrees: 0);
        sim.StepTicks(143 + 40 + 1); // march, turn 90° at 45°/s (40 ticks), one spare
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.None));
            Assert.That(Line(sim).Facing, Is.EqualTo(Angle.FromDegrees(0)));
        });

        sim.StepTicks(40); // members finish closing in
        foreach (var fighter in sim.State.Fighters)
        {
            Assert.Multiple(() =>
            {
                Assert.That(fighter.Position, Is.EqualTo(SlotOf(sim.State, fighter)), $"fighter {fighter.Id} on its slot");
                Assert.That(fighter.Velocity, Is.EqualTo(FixVec2.Zero));
                Assert.That(fighter.Facing, Is.EqualTo(Angle.FromDegrees(0)));
            });
        }
    }

    [Test]
    public void TurnRate_ComesFromTheFormation_ColumnTurnsTwiceAsFastAsLine()
    {
        static int TicksToFaceEast(FormationDef formation)
        {
            var scenario = new ScenarioDef("t", "", TestDefs.Map, [TestDefs.Squad("S", 0, 6, FixVec2.FromInts(20, 10), 90, formation)]);
            var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
            var squad = sim.State.Squads[0];
            sim.Enqueue(new MoveSquadCommand(0, squad.Id, squad.Anchor, Angle.FromDegrees(0))); // turn in place
            int ticks = 0;
            do
            {
                sim.Step(); // the order arrives in the first tick and turning starts in that same tick
                ticks++;
            }
            while (squad.Facing != Angle.FromDegrees(0) && ticks < 1000); // the front's turn; the order ends once formed up

            return ticks;
        }

        Assert.Multiple(() =>
        {
            Assert.That(TicksToFaceEast(TestDefs.Line), Is.EqualTo(40), "90° at 45°/s = 2 s");
            Assert.That(TicksToFaceEast(TestDefs.Column), Is.EqualTo(21), "90° at 90°/s = 1 s (819 units/tick rounds down)");
        });
    }

    [Test]
    public void SquadSpeed_IsTheSlowestLivingMembersWalk()
    {
        var slow = TestDefs.Fighter("slow", walkTenths: 7); // 0.7 m/s
        var scenario = new ScenarioDef("t", "", TestDefs.Map, [new SquadSpawnDef("S", 0, slow, 4, TestDefs.Line, FixVec2.FromInts(20, 10), Angle.FromDegrees(90))]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.Enqueue(new MoveSquadCommand(0, sim.State.Squads[0].Id, FixVec2.FromInts(20, 20)));
        sim.Step();
        Assert.That(sim.State.Squads[0].Velocity.Length, Is.EqualTo(slow.WalkPerTick));
    }

    [Test]
    public void FighterFarFromItsSlot_RunsThere_LookingWhereItRuns_NeverFasterThanRunSpeed()
    {
        var sim = OneLine();
        var fighter = sim.State.FighterList[0];
        fighter.Position += FixVec2.FromInts(-6, 0); // 6 m west of its slot
        sim.StepTicks(10);
        Assert.Multiple(() =>
        {
            Assert.That(fighter.Velocity.Length.Raw, Is.EqualTo(fighter.Def.RunPerTick.Raw).Within(2), "running");
            Assert.That(Math.Abs(fighter.Facing.DeltaTo(Angle.FromDegrees(0))), Is.LessThanOrEqualTo(2), "looks east, where it runs");
        });

        sim.StepTicks(60);
        Assert.Multiple(() =>
        {
            Assert.That(fighter.Position, Is.EqualTo(SlotOf(sim.State, fighter)), "back in its slot");
            Assert.That(fighter.Facing, Is.EqualTo(Angle.FromDegrees(90)), "facing the squad's front again");
        });
    }

    [Test]
    public void NoMemberEverMovesFasterThanItsRunSpeed()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.March(), seed: 1));
        for (int i = 0; i < 600; i++)
        {
            sim.Step();
            Assert.That(sim.State.Fighters.Max(f => f.Velocity.Length.Raw), Is.LessThanOrEqualTo(TestDefs.Warrior.RunPerTick.Raw));
        }
    }

    [Test]
    public void Script_OrdersArriveOnTheirTick_WithoutEnteringTheCommandLog()
    {
        var scenario = TestDefs.OneLine(new ScriptedOrderDef(5, "Line", FixVec2.FromInts(20, 20), null));
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.StepTicks(4);
        Assert.That(Line(sim).Order.Kind, Is.EqualTo(SquadOrderKind.None));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.Move(FixVec2.FromInts(20, 20), Angle.FromDegrees(90))));
            Assert.That(sim.Commands.Log, Is.Empty);
        });
    }

    [Test]
    public void Script_InvalidWhenItsTickComes_IsRejectedLikeACommand()
    {
        var scenario = TestDefs.OneLine(new ScriptedOrderDef(3, "Line", FixVec2.FromInts(20, 20), null));
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        foreach (var fighter in sim.State.FighterList)
        {
            fighter.IsAlive = false;
        }

        sim.StepTicks(3);
        var rejected = sim.State.Events.OfType<CommandRejected>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Reason, Is.EqualTo(RejectReason.NotAlive));
            Assert.That(Line(sim).Order.Kind, Is.EqualTo(SquadOrderKind.None));
        });
    }

    [Test]
    public void TurnInPlace_EveryMemberKeepsFacingTheFront_EvenTheOuterCorners()
    {
        // Rear corners of a line sit ~2.1 m from the anchor: turning at 45°/s moves their slots at ~1.67 m/s, faster
        // than walking. They are keeping up with their slots, not running, so they must keep facing the front.
        var sim = OneLine();
        sim.Enqueue(new MoveSquadCommand(0, Line(sim).Id, Line(sim).Anchor, Angle.FromDegrees(0)));
        for (int i = 0; i < 45; i++)
        {
            sim.Step();
            var front = Line(sim).Facing;
            Assert.That(sim.State.Fighters.Select(f => f.Facing), Has.All.EqualTo(front), $"tick {sim.State.Tick}");
        }
    }

    [Test]
    public void Order_TargetWithinArrivalDistance_KeepsTheFacing_NoTurnAround()
    {
        var sim = OneLine();
        var behind = Line(sim).Anchor - new FixVec2(Fix.Zero, Fix.FromRaw(1)); // 0.015 mm behind
        sim.Enqueue(new MoveSquadCommand(0, Line(sim).Id, behind));
        sim.StepTicks(10);
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Facing, Is.EqualTo(Angle.FromDegrees(90)));
            Assert.That(Line(sim).Anchor, Is.EqualTo(behind));
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.None));
        });
    }

    [Test]
    public void NewOrderMidTurn_ReplacesTheOld_AndTurnsOnFromTheCurrentFacing()
    {
        var sim = OneLine();
        Order(sim, 20, 0); // about-turn: clockwise
        sim.StepTicks(20);
        var midTurn = Line(sim).Facing;
        Assert.That(midTurn, Is.Not.EqualTo(Angle.FromDegrees(90)));

        Order(sim, 35, 10); // now east
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Order.Target, Is.EqualTo(FixVec2.FromInts(35, 10)));
            Assert.That(Math.Abs(midTurn.DeltaTo(Line(sim).Facing)), Is.LessThanOrEqualTo(410), "no jump: one tick of turning");
        });

        sim.StepTicks(300); // ~15° more turning, then 15 m at 1.4 m/s (215 ticks)
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Anchor, Is.EqualTo(FixVec2.FromInts(35, 10)));
            Assert.That(Line(sim).Facing, Is.EqualTo(Angle.FromDegrees(0)));
        });
    }

    [Test]
    public void EveryMemberDiesMidMarch_TheOrderIsDropped_AndTheAnchorStops()
    {
        var sim = OneLine();
        Order(sim, 20, 30);
        sim.StepTicks(20);
        var anchor = Line(sim).Anchor;
        foreach (var fighter in sim.State.FighterList)
        {
            fighter.IsAlive = false;
        }

        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.None));
            Assert.That(Line(sim).Anchor, Is.EqualTo(anchor));
            Assert.That(Line(sim).Velocity, Is.EqualTo(FixVec2.Zero));
        });
    }

    [Test]
    public void Order_EndsOnlyWhenFormedUp_EveryManWithin30CmOfHisSlot()
    {
        var sim = OneLine();
        sim.State.FighterList[0].Position += FixVec2.FromInts(-3, 0); // 3 m out of place
        Order(sim, 20, 10, facingDegrees: 90); // stay where you are, same facing: only the straggler is missing
        sim.Step();
        Assert.That(Line(sim).Order.Kind, Is.EqualTo(SquadOrderKind.Move), "anchor and facing are there, but a man isn't");
        sim.StepTicks(80);
        Assert.Multiple(() =>
        {
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.None), "formed up: the order is done");
            Assert.That(WorstSlotError(sim.State), Is.LessThan(0.3));
        });
    }
}
