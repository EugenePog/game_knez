using Knez.Sim.Commands;
using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// M1 step 4 rules (battle-sim.md §10): everyone is solid, allies included; overlapping men push apart sharing the push
/// by weight — a member of a squad holding its ground weighs its formation's hold weight (3), anyone moving weighs 1;
/// steering eases out of 10 cm of personal space; bodies stay on the map. Radius 0.3 m everywhere.
/// </summary>
[TestFixture]
public class CrowdingTests
{
    /// <summary>Raw units of slack for expected values written as decimals (Fix.Ratio truncates; 3 raw ≈ 0.05 mm).</summary>
    private const long Tolerance = 3;

    private static Fix M(int hundredths) => Fix.Ratio(hundredths, 100);

    private static (BattleState State, Fighter A, Fighter B) Pair(int ax100, int bx100, int y = 10)
    {
        var state = new BattleState(1);
        var a = state.AddFighter(TestDefs.Warrior, 0, new FixVec2(M(ax100), Fix.FromInt(y)), Angle.Zero);
        var b = state.AddFighter(TestDefs.Warrior, 1, new FixVec2(M(bx100), Fix.FromInt(y)), Angle.Zero);
        return (state, a, b);
    }

    [Test]
    public void Resolve_EqualWeights_EachTakesHalfThePush()
    {
        var (state, a, b) = Pair(1000, 1040); // 0.4 m apart, bodies need 0.6: 0.2 m overlap
        Crowding.Resolve(state, [1, 1]);
        Assert.Multiple(() =>
        {
            Assert.That(a.Position.X.Raw, Is.EqualTo(M(990).Raw).Within(Tolerance));
            Assert.That(b.Position.X.Raw, Is.EqualTo(M(1050).Raw).Within(Tolerance));
        });
    }

    [Test]
    public void Resolve_HoldingAgainstMoving_HolderTakesAQuarter()
    {
        var (state, a, b) = Pair(1000, 1040);
        Crowding.Resolve(state, [3, 1]); // a holds (weight 3), b moves
        Assert.Multiple(() =>
        {
            Assert.That(a.Position.X.Raw, Is.EqualTo(M(995).Raw).Within(Tolerance), "holder moves 0.05 m");
            Assert.That(b.Position.X.Raw, Is.EqualTo(M(1055).Raw).Within(Tolerance), "mover moves 0.15 m");
        });
    }

    [Test]
    public void Resolve_SameSpot_PartAlongXLowerIdWest()
    {
        var (state, a, b) = Pair(1000, 1000);
        Crowding.Resolve(state, [1, 1]);
        Assert.Multiple(() =>
        {
            Assert.That(a.Position.X.Raw, Is.EqualTo(M(970).Raw).Within(Tolerance));
            Assert.That(b.Position.X.Raw, Is.EqualTo(M(1030).Raw).Within(Tolerance));
            Assert.That(a.Position.Y, Is.EqualTo(Fix.FromInt(10)));
            Assert.That(b.Position.Y, Is.EqualTo(Fix.FromInt(10)));
        });
    }

    [Test]
    public void Resolve_TouchingOrApart_NothingMoves()
    {
        var (state, a, b) = Pair(1000, 1060);
        Crowding.Resolve(state, [1, 1]);
        Assert.Multiple(() =>
        {
            Assert.That(a.Position.X, Is.EqualTo(Fix.FromInt(10)));
            Assert.That(b.Position.X, Is.EqualTo(M(1060)));
        });
    }

    [Test]
    public void Resolve_KeepsEveryBodyOnTheMap()
    {
        var (state, a, b) = Pair(10, 9995, y: 0); // map is 100 × 100 (the empty scenario)
        Crowding.Resolve(state, [1, 1]);
        Assert.Multiple(() =>
        {
            Assert.That(a.Position, Is.EqualTo(new FixVec2(TestDefs.Warrior.Radius, TestDefs.Warrior.Radius)), "clamped to one radius from the corner");
            Assert.That(b.Position, Is.EqualTo(new FixVec2(Fix.FromInt(100) - TestDefs.Warrior.Radius, TestDefs.Warrior.Radius)));
        });
    }

    [Test]
    public void Resolve_ACrush_EndsWithNoOverlapLeft()
    {
        var state = new BattleState(1);
        for (int i = 0; i < 12; i++)
        {
            state.AddFighter(TestDefs.Warrior, i % 2, new FixVec2(M(1000 + 5 * (i % 4)), M(1000 + 5 * (i / 4))), Angle.Zero);
        }

        var weights = Enumerable.Repeat(1, 12).ToList();
        for (int tick = 0; tick < 10; tick++)
        {
            Crowding.Resolve(state, weights);
        }

        Assert.That(MovementMetrics.OverlappingFighters(state), Is.Zero);
    }

    [Test]
    public void Weight_HoldingSquadUsesItsFormation_MovingOrLooseFightersWeighOne()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.TwoSquads(), 1));
        var state = sim.State;
        var line = state.Squads[0];
        var loose = state.AddFighter(TestDefs.Warrior, 0, FixVec2.FromInts(5, 5), Angle.Zero);
        Assert.That(Crowding.Weight(state, state.FindFighter(line.Members[0])!), Is.EqualTo(TestDefs.Line.HoldWeight));

        line.Order = SquadOrder.Move(FixVec2.FromInts(20, 30), Angle.FromDegrees(90));
        Assert.Multiple(() =>
        {
            Assert.That(Crowding.Weight(state, state.FindFighter(line.Members[0])!), Is.EqualTo(1));
            Assert.That(Crowding.Weight(state, loose), Is.EqualTo(1));
        });
    }

    [Test]
    public void Weight_HoldingSquadButManFarFromHisSlot_IsOnTheMove()
    {
        // A squad whose anchor arrived while a man is still blocked metres short of his slot: he is trying to get
        // somewhere, so he weighs 1, not the hold weight (otherwise he'd shove a holding enemy line at full weight).
        var state = BattleSetup.Create(TestDefs.TwoSquads(), 1);
        var man = state.FindFighter(state.Squads[0].Members[0])!;
        man.Position += new FixVec2(Fix.Zero, Fix.Ratio(14, 10)); // 1.4 m off: still holding
        Assert.That(Crowding.Weight(state, man), Is.EqualTo(TestDefs.Line.HoldWeight));
        man.Position += new FixVec2(Fix.Zero, Fix.Ratio(2, 10));  // 1.6 m off: on the move
        Assert.That(Crowding.Weight(state, man), Is.EqualTo(1));
    }

    [Test]
    public void Separation_InsidePersonalSpace_EasesApartByAFifthOfTheIntrusion()
    {
        var (state, _, _) = Pair(1000, 1065); // 0.65 m apart; personal reach 0.7 → 0.05 m intrusion
        var push = Crowding.Separation(state, [1, 1], 0);
        var none = Pair(1000, 1070).State;
        Assert.Multiple(() =>
        {
            Assert.That(push, Is.EqualTo(new FixVec2(-M(1), Fix.Zero)), "0.05 / 5 = 0.01 m west");
            Assert.That(Crowding.Separation(none, [1, 1], 0), Is.EqualTo(FixVec2.Zero), "exactly at reach: nothing");
        });
    }

    [Test]
    public void Separation_SameSpot_LowerIdEasesWest()
    {
        var (state, _, _) = Pair(1000, 1000);
        Assert.Multiple(() =>
        {
            Assert.That(Crowding.Separation(state, [1, 1], 0).X, Is.LessThan(Fix.Zero));
            Assert.That(Crowding.Separation(state, [1, 1], 1).X, Is.GreaterThan(Fix.Zero));
            Assert.That(Crowding.Separation(state, [1, 1], 0).Y, Is.EqualTo(Fix.Zero));
        });
    }

    [Test]
    public void Weight_DeadFighter_IsOne()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), 1);
        var man = state.FindFighter(state.Squads[0].Members[0])!;
        man.IsAlive = false;
        Assert.That(Crowding.Weight(state, man), Is.EqualTo(1));
    }

    [Test]
    [Category("Scenario")]
    public void PushFourMenDeep_NeverOverlaps()
    {
        // A push travels one link per relaxation pass against id order: 3 passes left 7 % of fighter-ticks overlapping here.
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.DeepPush(), 1));
        int worst = 0;
        for (int i = 0; i < 600; i++)
        {
            sim.Step();
            worst = Math.Max(worst, MovementMetrics.OverlappingFighters(sim.State));
        }

        Assert.That(worst, Is.Zero);
    }

    [Test]
    [Category("Scenario")]
    public void BodiesPressedAgainstTheMapEdge_StayOnTheMap_DontOverlap_AndComeToRest()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.AgainstTheEdge(), 1));
        long overlapping = 0;
        long fighterTicks = 0;
        for (int i = 0; i < 1200; i++)
        {
            sim.Step();
            overlapping += MovementMetrics.OverlappingFighters(sim.State);
            fighterTicks += sim.State.Fighters.Count;
        }

        // At rest: over 100 more ticks nobody moves more than rounding (a 1-raw-unit flicker, 0.015 mm, is left).
        long worstStep = 0;
        for (int i = 0; i < 100; i++)
        {
            var before = sim.State.Fighters.Select(f => f.Position).ToArray();
            sim.Step();
            worstStep = Math.Max(worstStep, sim.State.Fighters.Select((f, k) => (f.Position - before[k]).Length.Raw).Max());
        }

        var map = sim.State.Scenario.Map;
        Assert.Multiple(() =>
        {
            Assert.That((double)overlapping / fighterTicks, Is.LessThan(0.01), "overlap share of fighter-ticks (M1 target)");
            Assert.That(sim.State.Fighters.All(f => f.Position.X >= f.Radius && f.Position.X <= map.Width - f.Radius
                && f.Position.Y >= f.Radius && f.Position.Y <= map.Height - f.Radius), "every body inside the map");
            Assert.That(worstStep, Is.LessThanOrEqualTo(2), "at rest: nobody shimmers against the edge (raw units per tick)");
        });
    }

    [Test]
    public void OverlappingFighters_CountsOnlyPressesDeeperThanFiveCentimetres()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovementMetrics.OverlappingFighters(Pair(1000, 1056).State), Is.Zero, "4 cm into each other");
            Assert.That(MovementMetrics.OverlappingFighters(Pair(1000, 1054).State), Is.EqualTo(2), "6 cm");
        });
    }

    [Test]
    [Category("Scenario")]
    public void FriendlySquadsCrossing_NeverOverlap_AndBothArriveInFormation()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.Crossing(), 1));
        int worst = 0;
        double closest = double.MaxValue;
        for (int i = 0; i < 700; i++)
        {
            sim.Step();
            worst = Math.Max(worst, MovementMetrics.OverlappingFighters(sim.State));
            closest = Math.Min(closest, ClosestBetweenSquads(sim.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(closest, Is.LessThan(0.7), "the lines really met: men inside each other's personal space (m)");
            Assert.That(worst, Is.Zero, "fighters pressed more than 5 cm into someone, worst tick");
            Assert.That(sim.State.Squads.Select(s => s.Order.Kind), Has.All.EqualTo(SquadOrderKind.None), "both arrived");
            Assert.That(WorstSlotError(sim.State), Is.LessThan(0.05), "and settled into formation (m)");
        });
    }

    [Test]
    [Category("Scenario")]
    public void MarchingIntoAHoldingLine_TheLineBendsButHolds_MarchersStayBlocked()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.IntoAHoldingLine(), 1));
        var holders = sim.State.Squads[0];
        var movers = sim.State.Squads[1];
        double holdersWorst = 0;
        double moversWorst = 0;
        int overlapWorst = 0;
        double holdersAt600 = 0;
        for (int i = 0; i < 1200; i++)
        {
            sim.Step();
            holdersWorst = Math.Max(holdersWorst, WorstSlotError(sim.State, holders));
            moversWorst = Math.Max(moversWorst, WorstSlotError(sim.State, movers));
            overlapWorst = Math.Max(overlapWorst, MovementMetrics.OverlappingFighters(sim.State));
            if (sim.State.Tick == 600)
            {
                holdersAt600 = holdersWorst;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(overlapWorst, Is.Zero);
            Assert.That(movers.Order.Kind, Is.EqualTo(SquadOrderKind.None), "march on: the movers' anchor reached its target");
            Assert.That(holdersWorst, Is.LessThan(1.0), "the holding line bends but holds (m)");
            Assert.That(holdersWorst, Is.EqualTo(holdersAt600).Within(0.01), "and stops bending once the push settles, even after the movers' order is done");
            Assert.That(holdersWorst, Is.LessThan(moversWorst), "the holders are pushed out of shape less than the movers");
        });
    }

    private static double ClosestBetweenSquads(BattleState state) =>
        state.Fighters.SelectMany(a => state.Fighters.Where(b => b.SquadId != a.SquadId),
            (a, b) => (a.Position - b.Position).Length.Raw / 65536.0).DefaultIfEmpty(double.MaxValue).Min();

    private static double WorstSlotError(BattleState state, Squad? only = null) =>
        state.Fighters.Where(f => only == null || f.SquadId == only.Id).Max(f =>
        {
            var squad = state.FindSquad(f.SquadId)!;
            var slot = FormationLayout.SlotPosition(squad.Formation, squad.Members.Count, f.SlotIndex, squad.Anchor, squad.Facing);
            return (slot - f.Position).Length.Raw / 65536.0;
        });
}
