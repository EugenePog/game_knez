using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// The soft wheel (battle-sim.md §10, asked for 2026-11-23): a turning squad's slots swing round, but its men are not
/// carried with them — each walks after his slot on his own, lagging the more the farther out he stands, and the lag
/// closes over <c>turnLagTime</c> (4 s) after the turn, on the march and when turning in place on arrival.
/// Test line: 8 men, 4 files × 2 ranks, anchor at the front centre, turning 45°/s (a quarter turn in 40 ticks).
/// </summary>
[TestFixture]
public class TurnLagTests
{
    private static BattleSimulation OneLine(MovementDef? movement = null)
    {
        ScenarioDef line = TestDefs.OneLine();
        var scenario = new ScenarioDef(line.Id, line.Description, line.Map, line.Squads, line.Script, movement ?? MovementDef.Standard,
            [TestDefs.Line, TestDefs.Column]);
        return new BattleSimulation(BattleSetup.Create(scenario, seed: 1));
    }

    private static Squad Line(BattleSimulation sim) => sim.State.Squads[0];

    private static void TurnInPlaceToEast(BattleSimulation sim) =>
        sim.Enqueue(new MoveSquadCommand(0, Line(sim).Id, Line(sim).Anchor, Angle.FromDegrees(0)));

    private static double FromSlot(BattleState state, Fighter fighter)
    {
        Squad squad = state.FindSquad(fighter.SquadId)!;
        FixVec2 slot = FormationLayout.SlotPosition(squad.Formation, squad.Members.Count, fighter.SlotIndex, squad.Anchor, squad.Facing);
        return (slot - fighter.Position).Length.Raw / 65536.0;
    }

    /// <summary>The man whose slot is farthest from the anchor (a rear corner) and the one nearest it (front, inner).</summary>
    private static (Fighter Outer, Fighter Inner) OuterAndInner(BattleSimulation sim)
    {
        Squad squad = Line(sim);
        double Reach(Fighter f) => FormationLayout.SlotOffset(squad.Formation, squad.Members.Count, f.SlotIndex).Length.Raw;
        var men = sim.State.Fighters.OrderBy(Reach).ThenBy(f => f.Id.Value).ToList();
        return (men[^1], men[0]);
    }

    [Test]
    public void TurnInPlace_OuterManLagsBehindHisSlot_MoreThanTheInner()
    {
        var sim = OneLine();
        var (outer, inner) = OuterAndInner(sim);
        TurnInPlaceToEast(sim);
        sim.StepTicks(20); // half way round

        double outerLag = FromSlot(sim.State, outer);
        double innerLag = FromSlot(sim.State, inner);
        Assert.Multiple(() =>
        {
            Assert.That(outerLag, Is.GreaterThan(0.3), "the outer man is not carried round with his slot");
            Assert.That(outerLag, Is.GreaterThan(innerLag * 2), "the farther out, the more he lags");
            Assert.That(outerLag, Is.LessThan(2.0), "but he follows: the line stays a line");
        });
    }

    [Test]
    public void TurnInPlace_LagClosesGradually_OverTheTurnLagTime()
    {
        var sim = OneLine();
        TurnInPlaceToEast(sim);
        sim.StepTicks(40);
        Assert.That(Line(sim).Facing, Is.EqualTo(Angle.FromDegrees(0)), "the front has turned");

        sim.StepTicks(20); // one second after the turn
        double worst = sim.State.Fighters.Max(f => FromSlot(sim.State, f));
        Assert.That(worst, Is.GreaterThan(0.3), "a second later the ranks are still dressing");
        Assert.That(Line(sim).Order.Kind, Is.EqualTo(SquadOrderKind.Move), "not formed up yet");

        sim.StepTicks(80); // 4 s (turnLagTime) after the turn, plus a spare second
        worst = sim.State.Fighters.Max(f => FromSlot(sim.State, f));
        Assert.Multiple(() =>
        {
            Assert.That(worst, Is.LessThan(0.02), "dressed again");
            Assert.That(Line(sim).Order, Is.EqualTo(SquadOrder.None), "formed up");
            Assert.That(sim.State.Fighters.Select(f => f.TurnLag), Has.All.EqualTo(FixVec2.Zero));
        });
    }

    [Test]
    public void Wheel_OnTheMarch_OuterFlankLags_ThenCatchesUp()
    {
        var sim = OneLine();
        Fighter SlotMan(int slot) => sim.State.Fighters.First(f => f.SlotIndex == slot);
        Fighter pivot = SlotMan(0); // front left: the inner end of a wheel to the left
        Fighter outer = SlotMan(3); // front right
        sim.Enqueue(new MoveSquadCommand(0, Line(sim).Id, FixVec2.FromInts(8, 22))); // 45° left: a wheel round the left flank
        double worst = 0;
        double pivotWorst = 0;
        for (int i = 0; i < 60; i++)
        {
            sim.Step();
            worst = Math.Max(worst, FromSlot(sim.State, outer));
            if (Knez.Sim.Movement.SlotTracking.Wheeled(Line(sim)))
            {
                // Only while wheeling: the last 30° (pivotTolerance) are turned about the anchor on the march, where the
                // inner flank leads as much as the outer trails.
                pivotWorst = Math.Max(pivotWorst, FromSlot(sim.State, pivot));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(worst, Is.GreaterThan(0.2), "the outer flank trails its slot through the wheel");
            Assert.That(pivotWorst, Is.LessThan(0.05), "the pivot man marks time on his slot");
        });
        sim.StepTicks(400);
        Assert.That(sim.State.Fighters.Max(f => FromSlot(sim.State, f)), Is.LessThan(0.02), "and is back in line after it");
    }

    [Test]
    public void NewSlots_AfterATurn_DropTheOldLag()
    {
        var sim = OneLine();
        TurnInPlaceToEast(sim);
        sim.StepTicks(40);
        Assert.That(sim.State.Fighters.Any(f => f.TurnLag != FixVec2.Zero), Is.True, "lagging after the turn");

        sim.Enqueue(new SetFormationCommand(0, Line(sim).Id, TestDefs.Column.Id));
        sim.Step();
        Assert.That(sim.State.Fighters.Select(f => f.TurnLag), Has.All.EqualTo(FixVec2.Zero),
            "new places are picked from where the men stand: an old lag would send them away from them");
    }

    [Test]
    public void TurnLagTime_Zero_MenSwingRigidlyWithTheirSlots()
    {
        var sim = OneLine(TestDefs.WithTurnLag(MovementDef.Standard, Fix.Zero));
        TurnInPlaceToEast(sim);
        double worst = 0;
        for (int i = 0; i < 40; i++)
        {
            sim.Step();
            worst = Math.Max(worst, sim.State.Fighters.Max(f => FromSlot(sim.State, f)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(worst, Is.LessThan(0.05), "the old brick: everyone stays on his slot");
            Assert.That(sim.State.Fighters.Select(f => f.TurnLag), Has.All.EqualTo(FixVec2.Zero));
        });
    }

    [Test]
    public void TurnLag_IsPartOfTheFighterHash()
    {
        var a = OneLine();
        var b = OneLine();
        TurnInPlaceToEast(a);
        TurnInPlaceToEast(b);
        a.StepTicks(10);
        b.StepTicks(10);
        Assert.That(a.ComputeHash(), Is.EqualTo(b.ComputeHash()));

        b.State.Fighters[0].TurnLag += new FixVec2(Fix.FromRaw(1), Fix.Zero);
        Assert.That(b.ComputeHash().Fighters, Is.Not.EqualTo(a.ComputeHash().Fighters));
    }
}
