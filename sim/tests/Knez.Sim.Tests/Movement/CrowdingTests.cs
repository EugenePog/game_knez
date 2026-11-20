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

    private static (BattleState State, Fighter A, Fighter B) Pair(int ax100, int bx100, int y = 10, bool allies = false)
    {
        var state = new BattleState(1);
        var a = state.AddFighter(TestDefs.Warrior, 0, new FixVec2(M(ax100), Fix.FromInt(y)), Angle.Zero);
        var b = state.AddFighter(TestDefs.Warrior, allies ? 0 : 1, new FixVec2(M(bx100), Fix.FromInt(y)), Angle.Zero);
        return (state, a, b);
    }

    /// <summary>Two allied squads: Front holds, Rear gets a move order; Rear's first man stands 0.4 m from Front's.</summary>
    private static (BattleState State, Fighter Holder, Fighter Ordered, List<int> Weights, List<CrowdRole> Roles) AlliedPair()
    {
        var state = BattleSetup.Create(TestDefs.TwoAlliedLines(), 1);
        var holder = state.FindFighter(state.Squads[0].Members[0])!;
        var ordered = state.FindFighter(state.Squads[1].Members[0])!;
        state.Squads[1].Order = SquadOrder.Move(FixVec2.FromInts(40, 30), Angle.FromDegrees(90));
        ordered.Position = holder.Position + new FixVec2(M(40), Fix.Zero);
        ordered.PassingThrough = true; // as if he had walked into Front's ranks on his way (UpdatePassing has its own tests)
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        return (state, holder, ordered, weights, roles);
    }

    [Test]
    public void Resolve_AllyHoldingAndAllyOnOrders_PassThroughEachOther()
    {
        var (state, holder, ordered, weights, roles) = AlliedPair();
        var holderAt = holder.Position;
        var orderedAt = ordered.Position;
        Crowding.Resolve(state, weights, roles);
        Assert.Multiple(() =>
        {
            Assert.That(roles[state.FighterList.IndexOf(holder)], Is.EqualTo(CrowdRole.Holding));
            Assert.That(roles[state.FighterList.IndexOf(ordered)], Is.EqualTo(CrowdRole.Ordered));
            Assert.That(holder.Position, Is.EqualTo(holderAt), "the holder stays");
            Assert.That(ordered.Position, Is.EqualTo(orderedAt), "the ordered man isn't pushed out: he is squeezing through");
        });
    }

    [Test]
    public void Resolve_ManShovedOffHisSlotWithoutOrders_StaysSolidAgainstAnAlliedReserve()
    {
        // Decided 2026-11-21: only men on orders pass. A man shoved out of a holding line is Loose and must not sink
        // into an allied squad standing behind it.
        var state = BattleSetup.Create(TestDefs.TwoAlliedLines(), 1);
        var reserve = state.FindFighter(state.Squads[1].Members[0])!;
        var shoved = state.FindFighter(state.Squads[0].Members[0])!;
        shoved.Position = reserve.Position + new FixVec2(Fix.Zero, M(40)); // just in front of him, ~20 m off his own slot
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        Crowding.Resolve(state, weights, roles);
        Assert.Multiple(() =>
        {
            Assert.That(roles[state.FighterList.IndexOf(shoved)], Is.EqualTo(CrowdRole.Loose));
            Assert.That((shoved.Position - reserve.Position).Length.Raw, Is.GreaterThanOrEqualTo(M(60).Raw - Tolerance), "pushed apart");
        });
    }

    [Test]
    public void Resolve_SameSquad_StaysSolid()
    {
        // A holder shoved off his slot (Loose) must not sink into his own rank.
        var state = BattleSetup.Create(TestDefs.TwoSquads(), 1);
        var squad = state.Squads[0];
        var a = state.FindFighter(squad.Members[0])!;
        var b = state.FindFighter(squad.Members[1])!;
        b.Position = a.Position + new FixVec2(M(40), Fix.Zero);
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        Crowding.Resolve(state, weights, roles);
        Assert.That((b.Position - a.Position).Length.Raw, Is.GreaterThanOrEqualTo(M(60).Raw - Tolerance));
    }

    // Roles as ints: CrowdRole is internal, and NUnit test methods must be public. 1 = Holding, 2 = Ordered.
    [TestCase(1, 2, false, Description = "enemies: holder and ordered man stay solid")]
    [TestCase(2, 2, true, Description = "allies both on orders: solid, they jostle")]
    [TestCase(1, 1, true, Description = "allies both holding: solid")]
    public void Resolve_OtherPairs_StaySolid(int a1, int b1, bool allies)
    {
        var roleA = (CrowdRole)a1;
        var roleB = (CrowdRole)b1;
        var (state, a, b) = Pair(1000, 1056, allies: allies); // 4 cm overlap: within one pass even for allies
        Crowding.Resolve(state, [roleA == CrowdRole.Holding ? 3 : 1, roleB == CrowdRole.Holding ? 3 : 1], [roleA, roleB]);
        Assert.That((b.Position - a.Position).Length.Raw, Is.EqualTo(M(60).Raw).Within(Tolerance), "pushed apart to touching");
    }

    [Test]
    public void Resolve_DeepOverlapBetweenAlliedSquads_PartsGraduallyNotInOneJump()
    {
        // Men of two allied squads who stop passing through each other mid-overlap (an order given or ended mid-squeeze)
        // drift apart over a few ticks: at most personal space (10 cm) per tick, not the whole 0.5 m at once.
        var (state, holder, ordered, weights, roles) = AlliedPair();
        ordered.Position = holder.Position + new FixVec2(M(10), Fix.Zero);
        ordered.PassingThrough = false; // no longer passing: solid again
        Crowding.Resolve(state, weights, roles);
        double parted = (ordered.Position - holder.Position).Length.Raw / 65536.0 - 0.1;
        Assert.That(parted, Is.LessThanOrEqualTo(0.1 + 1e-4).And.GreaterThan(0.05));
    }

    [Test]
    public void Resolve_DeepOverlapWithinOneSquad_IsClearedAtOnce()
    {
        // Men of one squad never pass through each other, so a deep overlap between them is a push, cleared fully.
        var state = BattleSetup.Create(TestDefs.TwoAlliedLines(), 1);
        var a = state.FindFighter(state.Squads[0].Members[0])!;
        var b = state.FindFighter(state.Squads[0].Members[1])!;
        b.Position = a.Position + new FixVec2(M(10), Fix.Zero);
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        Crowding.Resolve(state, weights, roles);
        Assert.That((b.Position - a.Position).Length.Raw, Is.GreaterThanOrEqualTo(M(60).Raw - Tolerance));
    }

    [Test]
    public void UpdatePassing_StartsWhenWalkingIntoAnAlliedHolder_NotWhenShovedIntoOne_EndsOnceClear()
    {
        var state = BattleSetup.Create(TestDefs.TwoAlliedLines(), 1);
        var holder = state.FindFighter(state.Squads[0].Members[0])!; // Front holds
        var man = state.FindFighter(state.Squads[1].Members[0])!;
        state.Squads[1].Order = SquadOrder.Move(FixVec2.FromInts(40, 30), Angle.FromDegrees(90)); // Rear is on orders
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        int i = state.FighterList.IndexOf(man);
        var north = new FixVec2(Fix.Zero, TestDefs.Warrior.WalkPerTick);
        List<FixVec2> Intent(FixVec2 v) => state.FighterList.Select((f, k) => k == i ? v : FixVec2.Zero).ToList();

        man.Position = holder.Position - new FixVec2(Fix.Zero, M(65)); // just south of the holder
        Crowding.UpdatePassing(state, roles, Intent(north));
        Assert.That(man.PassingThrough, Is.True, "he means to walk north, into the holder: he passes");

        man.Position = holder.Position + new FixVec2(Fix.Zero, M(20)); // half through, the holder now behind him
        Crowding.UpdatePassing(state, roles, Intent(north));
        Assert.That(man.PassingThrough, Is.True, "kept while still inside");

        man.Position = holder.Position + new FixVec2(Fix.Zero, M(70)); // through and clear
        Crowding.UpdatePassing(state, roles, Intent(north));
        Assert.That(man.PassingThrough, Is.False, "ends once clear");

        man.Position = holder.Position + new FixVec2(Fix.Zero, M(50)); // shoved south into the holder while meaning north
        Crowding.UpdatePassing(state, roles, Intent(north));
        Assert.That(man.PassingThrough, Is.False, "he wants to walk away from the holder: shoved, not passing");

        state.Squads[1].Order = SquadOrder.None; // Rear holds too: nobody on orders, nobody passes
        Crowding.Fill(state, weights, roles);
        man.Position = holder.Position - new FixVec2(Fix.Zero, M(65));
        Crowding.UpdatePassing(state, roles, Intent(north));
        Assert.That(man.PassingThrough, Is.False);
    }

    [Test]
    public void Squeeze_OrderedManInsideAHoldingAllysRanks_MovesAtAThirdOfHisWalk_OthersUnaffected()
    {
        var (state, holder, ordered, _, roles) = AlliedPair(); // bodies overlap by 0.2 m
        var enemy = state.AddFighter(TestDefs.Warrior, 1, holder.Position - new FixVec2(Fix.Zero, M(50)), Angle.Zero);
        roles.Add(CrowdRole.Loose);
        int orderedIndex = state.FighterList.IndexOf(ordered);
        int holderIndex = state.FighterList.IndexOf(holder);
        int enemyIndex = state.FighterList.IndexOf(enemy);
        var fast = new FixVec2(Fix.Zero, TestDefs.Warrior.RunPerTick);
        Assert.Multiple(() =>
        {
            Assert.That(Crowding.Squeeze(state, roles, orderedIndex, fast).Length.Raw,
                Is.EqualTo((TestDefs.Warrior.WalkPerTick * Fix.Ratio(1, 3)).Raw).Within(1), "a third of 1.4 m/s per tick");
            Assert.That(Crowding.Squeeze(state, roles, enemyIndex, fast), Is.EqualTo(fast), "enemies don't squeeze, they collide");
            Assert.That(Crowding.Squeeze(state, roles, holderIndex, fast), Is.EqualTo(fast), "the holder isn't slowed");
        });
    }

    [Test]
    public void Squeeze_WalkingAlongsideAnAlliedLineWithoutTouching_CostsNothing()
    {
        var (state, holder, ordered, _, roles) = AlliedPair();
        ordered.Position = holder.Position + new FixVec2(Fix.Zero, M(65)); // in front of the line, 5 cm clear
        var fast = new FixVec2(Fix.Zero, TestDefs.Warrior.RunPerTick);
        Assert.That(Crowding.Squeeze(state, roles, state.FighterList.IndexOf(ordered), fast), Is.EqualTo(fast));
    }

    [Test]
    public void Resolve_EqualWeights_EachTakesHalfThePush()
    {
        var (state, a, b) = Pair(1000, 1040); // 0.4 m apart, bodies need 0.6: 0.2 m overlap
        Crowding.Resolve(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose]);
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
        Crowding.Resolve(state, [3, 1], [CrowdRole.Holding, CrowdRole.Ordered]); // a holds (weight 3), b moves; enemies, so solid
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
        Crowding.Resolve(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose]);
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
        Crowding.Resolve(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose]);
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
        Crowding.Resolve(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose]);
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
            Crowding.Resolve(state, weights, Enumerable.Repeat(CrowdRole.Loose, 12).ToList());
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
        Assert.That(WeightOf(state, state.FindFighter(line.Members[0])!), Is.EqualTo(TestDefs.Line.HoldWeight));

        line.Order = SquadOrder.Move(FixVec2.FromInts(20, 30), Angle.FromDegrees(90));
        Assert.Multiple(() =>
        {
            Assert.That(WeightOf(state, state.FindFighter(line.Members[0])!), Is.EqualTo(1));
            Assert.That(WeightOf(state, loose), Is.EqualTo(1));
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
        Assert.That(WeightOf(state, man), Is.EqualTo(TestDefs.Line.HoldWeight));
        man.Position += new FixVec2(Fix.Zero, Fix.Ratio(2, 10));  // 1.6 m off: on the move
        Assert.That(WeightOf(state, man), Is.EqualTo(1));
    }

    [Test]
    public void Separation_InsidePersonalSpace_EasesApartByAFifthOfTheIntrusion()
    {
        var (state, _, _) = Pair(1000, 1065); // 0.65 m apart; personal reach 0.7 → 0.05 m intrusion
        var push = Crowding.Separation(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose], 0);
        var none = Pair(1000, 1070).State;
        Assert.Multiple(() =>
        {
            Assert.That(push, Is.EqualTo(new FixVec2(-M(1), Fix.Zero)), "0.05 / 5 = 0.01 m west");
            Assert.That(Crowding.Separation(none, [1, 1], [CrowdRole.Loose, CrowdRole.Loose], 0), Is.EqualTo(FixVec2.Zero), "exactly at reach: nothing");
        });
    }

    [Test]
    public void Separation_SameSpot_LowerIdEasesWest()
    {
        var (state, _, _) = Pair(1000, 1000);
        Assert.Multiple(() =>
        {
            Assert.That(Crowding.Separation(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose], 0).X, Is.LessThan(Fix.Zero));
            Assert.That(Crowding.Separation(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose], 1).X, Is.GreaterThan(Fix.Zero));
            Assert.That(Crowding.Separation(state, [1, 1], [CrowdRole.Loose, CrowdRole.Loose], 0).Y, Is.EqualTo(Fix.Zero));
        });
    }

    [Test]
    public void Weight_DeadFighter_IsOne()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), 1);
        var man = state.FindFighter(state.Squads[0].Members[0])!;
        man.IsAlive = false;
        Assert.That(WeightOf(state, man), Is.EqualTo(1));
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
    public void AlliesThroughAHoldingLine_SqueezeThroughSlowly_TheLineStandsStill_AllArrive()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.AlliesThroughAHoldingLine(), 1));
        var holders = sim.State.Squads[0];
        var movers = sim.State.Squads[1];
        var holderStart = holders.Members.Select(id => sim.State.FindFighter(id)!.Position).ToArray();
        long squeezeCap = (TestDefs.Warrior.WalkPerTick * Fix.Ratio(1, 3)).Raw;
        bool squeezed = false;
        int overlapWorst = 0;
        for (int i = 0; i < 1500; i++)
        {
            // Men passing through and already inside the holders' ranks when the tick starts take a short stride.
            var inside = movers.Members.Where(id => sim.State.FindFighter(id)!.PassingThrough && holders.Members.Any(h =>
                (sim.State.FindFighter(h)!.Position - sim.State.FindFighter(id)!.Position).Length < Fix.Ratio(55, 100))).ToList();
            sim.Step();
            overlapWorst = Math.Max(overlapWorst, MovementMetrics.OverlappingFighters(sim.State));
            foreach (var id in inside)
            {
                squeezed = true;
                Assert.That(sim.State.FindFighter(id)!.Velocity.Length.Raw, Is.LessThanOrEqualTo(squeezeCap + 1), $"tick {sim.State.Tick}: squeezing is slow");
            }
        }

        double holderDrift = holders.Members.Select((id, k) => (sim.State.FindFighter(id)!.Position - holderStart[k]).Length.Raw / 65536.0).Max();
        Assert.Multiple(() =>
        {
            Assert.That(squeezed, Is.True, "the movers really passed through the line");
            Assert.That(holderDrift, Is.Zero, "the holding line stands still (m)");
            Assert.That(overlapWorst, Is.Zero, "no unintended overlap");
            Assert.That(movers.Order.Kind, Is.EqualTo(SquadOrderKind.None));
            Assert.That(WorstSlotError(sim.State, movers), Is.LessThan(0.05), "every mover reached his slot beyond the line (m)");
        });
    }

    [Test]
    [Category("Scenario")]
    public void AlliesStoppingJustBeyondAHoldingLine_KeepPassingUntilFormedUp_NobodyLocksUp()
    {
        // Their rear rank is still inside the allied line when the anchor arrives; the order (and so the passing) lasts
        // until every man stands on his slot (decided 2026-11-21: orders end when formed up).
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.AlliesStopJustBeyondAHoldingLine(), 1));
        var holders = sim.State.Squads[0];
        var movers = sim.State.Squads[1];
        var holderStart = holders.Members.Select(id => sim.State.FindFighter(id)!.Position).ToArray();
        sim.StepTicks(1200);
        double holderDrift = holders.Members.Select((id, k) => (sim.State.FindFighter(id)!.Position - holderStart[k]).Length.Raw / 65536.0).Max();
        Assert.Multiple(() =>
        {
            Assert.That(movers.Order.Kind, Is.EqualTo(SquadOrderKind.None), "formed up, order done");
            Assert.That(WorstSlotError(sim.State, movers), Is.LessThan(0.05), "every mover on his slot (m)");
            Assert.That(holderDrift, Is.Zero, "the holding line never moved (m)");
        });
    }

    [Test]
    [Category("Scenario")]
    public void HoldersGivenAnOrderMidSqueeze_NobodyJumps()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.AlliesThroughAHoldingLine(), 1));
        var holders = sim.State.Squads[0];
        var movers = sim.State.Squads[1];
        while (!movers.Members.Any(id => holders.Members.Any(h =>
                   (sim.State.FindFighter(h)!.Position - sim.State.FindFighter(id)!.Position).Length < Fix.Ratio(4, 10))))
        {
            sim.Step();
        }

        sim.Enqueue(new MoveSquadCommand(1, holders.Id, FixVec2.FromInts(18, 20))); // holders leave westward mid-squeeze
        long worstStep = 0;
        for (int i = 0; i < 120; i++)
        {
            var before = sim.State.Fighters.Select(f => f.Position).ToArray();
            sim.Step();
            worstStep = Math.Max(worstStep, sim.State.Fighters.Select((f, k) => (f.Position - before[k]).Length.Raw).Max());
        }

        long limit = (TestDefs.Warrior.RunPerTick + Fix.Ratio(1, 10)).Raw; // a stride plus at most personal space of parting
        Assert.That(worstStep, Is.LessThanOrEqualTo(limit), "no one-tick jumps when allies stop passing through each other");
    }

    [Test]
    [Category("Scenario")]
    public void OrderedLineShovedBackIntoItsReserve_NeverPassesIntoIt()
    {
        // The Front stays on orders (it can't form up in contact), but it is shoved backwards, not walking into its
        // reserve: it must stay solid against it (review finding, 2026-11-21).
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OrderedLineShovedIntoItsReserve(), 1));
        var front = sim.State.Squads[0];
        var reserve = sim.State.Squads[1];
        double deepest = 0;
        bool everPassing = false;
        for (int i = 0; i < 900; i++)
        {
            sim.Step();
            foreach (var f in front.Members.Select(id => sim.State.FindFighter(id)!))
            {
                everPassing |= f.PassingThrough;
                foreach (var r in reserve.Members.Select(id => sim.State.FindFighter(id)!))
                {
                    deepest = Math.Max(deepest, 0.6 - (f.Position - r.Position).Length.Raw / 65536.0);
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(front.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "the setup: still on orders");
            Assert.That(everPassing, Is.False, "never passing through its reserve");
            Assert.That(deepest, Is.LessThan(0.05), "pressed against the reserve at most a few cm, not sunk into it (m)");
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
            Assert.That(movers.Anchor, Is.EqualTo(FixVec2.FromInts(30, 32)), "march on: the movers' anchor reached its target");
            Assert.That(movers.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "but the squad never formed up, so its order stands");
            Assert.That(holdersWorst, Is.LessThan(1.0), "the holding line bends but holds (m)");
            Assert.That(holdersWorst, Is.EqualTo(holdersAt600).Within(0.01), "and stops bending once the push settles, even after the movers' order is done");
            Assert.That(holdersWorst, Is.LessThan(moversWorst), "the holders are pushed out of shape less than the movers");
        });
    }

    private static int WeightOf(BattleState state, Fighter fighter)
    {
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        return weights[state.FighterList.IndexOf(fighter)];
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
