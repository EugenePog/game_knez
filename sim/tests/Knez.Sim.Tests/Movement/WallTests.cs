using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// M1 step 5 (battle-sim.md §10): walls are axis-aligned rectangles no body enters; bodies slide along them and round
/// their corners; an order aimed into a wall goes to the nearest open ground; men whose slots fall in a wall stand as
/// close as they can and the squad counts as formed up without them (decided 2026-11-21).
/// </summary>
[TestFixture]
public class WallTests
{
    private const long Tolerance = 3; // raw units for decimals written with Fix.Ratio
    private static readonly Fix R = TestDefs.Man.Radius;

    private static Fix M(int hundredths) => Fix.Ratio(hundredths, 100);

    /// <summary>A foot (10, 10)–(20, 11) and an arm (19, 11)–(20, 20) rising from its east end.</summary>
    private static MapDef LShape() => new(Fix.FromInt(60), Fix.FromInt(40),
        [new WallDef(FixVec2.FromInts(10, 10), FixVec2.FromInts(20, 11)), new WallDef(FixVec2.FromInts(19, 11), FixVec2.FromInts(20, 20))]);

    /// <summary>A palisade from x 20 east, <paramref name="thicknessHundredths"/> cm thick, from y 5 to 35.</summary>
    private static MapDef Palisade(int thicknessHundredths) => new(Fix.FromInt(60), Fix.FromInt(40),
        [new WallDef(FixVec2.FromInts(20, 5), new FixVec2(M(2000 + thicknessHundredths), Fix.FromInt(35)))]);

    private static MapDef OneBlock() => new(Fix.FromInt(60), Fix.FromInt(40), [new WallDef(FixVec2.FromInts(20, 10), FixVec2.FromInts(24, 14))]);

    [Test]
    public void WallDef_CornersInAnyOrder_TooThinRejected()
    {
        var wall = new WallDef(FixVec2.FromInts(24, 14), FixVec2.FromInts(20, 10));
        Assert.Multiple(() =>
        {
            Assert.That(wall.Min, Is.EqualTo(FixVec2.FromInts(20, 10)));
            Assert.That(wall.Max, Is.EqualTo(FixVec2.FromInts(24, 14)));
            Assert.Throws<ArgumentException>(() => new WallDef(FixVec2.FromInts(20, 10), new FixVec2(Fix.FromInt(24), M(1005))));
            Assert.Throws<ArgumentException>(() => new MapDef(Fix.FromInt(60), Fix.FromInt(40), [new WallDef(FixVec2.FromInts(50, 10), FixVec2.FromInts(61, 12))]));
        });
    }

    [Test]
    public void FighterDef_RunningMoreThanRadiusPlusHalfAThinWallPerTick_Rejected()
    {
        // 0.3 m radius + 5 cm = 0.35 m per tick = 7 m/s: faster, and a man could step through a 10 cm wall in one tick.
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => new BodyDef("w", R, Fix.One, Fix.Ratio(69, 10), Fix.FromInt(360)));
            var ex = Assert.Throws<ArgumentException>(() => new BodyDef("w", R, Fix.One, Fix.FromInt(7), Fix.FromInt(360)));
            Assert.That(ex!.Message, Does.Contain("step through a wall"));
        });
    }

    [Test]
    public void Fingerprint_WithWalls_Changes()
    {
        var open = new ScenarioDef("s", "", TestDefs.Map, [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(5, 5), 90)]);
        var walled = new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(5, 5), 90)]);
        Assert.That(walled.Fingerprint, Is.Not.EqualTo(open.Fingerprint));
    }

    [Test]
    public void Constrain_CentreInsideAWall_LeavesByTheNearestSideOneRadiusOut()
    {
        var p = Terrain.Constrain(OneBlock(), new FixVec2(M(2050), Fix.FromInt(12)), R); // 0.5 m inside the west side
        Assert.That(p, Is.EqualTo(new FixVec2(Fix.FromInt(20) - R, Fix.FromInt(12))));
    }

    [Test]
    public void Constrain_InsideAWallFlushWithTheMapEdge_NeverLeavesOffTheMap()
    {
        // 0.4 m from the gate wall's west end, which is the map edge: west is nearest but off the map; south and north
        // tie at 0.5 m, and ties go south.
        var p = Terrain.Constrain(TestDefs.GateMap(), new FixVec2(M(40), M(1950)), R);
        Assert.That(p, Is.EqualTo(new FixVec2(M(40), Fix.FromInt(19) - R)));
    }

    [Test]
    public void Constrain_InsideAbuttingWalls_NeverLeavesIntoTheNeighbour()
    {
        // 0.1 m from the joint: east is nearest but inside the next segment, so out south (0.5 m) instead.
        var map = new MapDef(Fix.FromInt(60), Fix.FromInt(40),
            [new WallDef(FixVec2.FromInts(10, 10), FixVec2.FromInts(12, 11)), new WallDef(FixVec2.FromInts(12, 10), FixVec2.FromInts(20, 11))]);
        var p = Terrain.Constrain(map, new FixVec2(M(1190), M(1050)), R);
        Assert.That(p, Is.EqualTo(new FixVec2(M(1190), Fix.FromInt(10) - R)));
    }

    [Test]
    public void Constrain_InsideTheFootOfAnL_LeavesByTheNearestSideClearOfTheOtherArm()
    {
        // The foot runs east to x 20; the arm rises from its east end. 0.1 m under the foot's top, but up is the arm:
        // out east (0.5 m) rather than north into it.
        var map = LShape();
        var p = Terrain.Constrain(map, new FixVec2(M(1950), M(1090)), R);
        Assert.That(p, Is.EqualTo(new FixVec2(Fix.FromInt(20) + R, M(1090))));
    }

    [TestCase("gate")]
    [TestCase("abutting")]
    [TestCase("L")]
    public void Constrain_AnyPointNearWalls_EndsOnTheMapWithItsCentreInNoWall(string shape)
    {
        var map = shape switch
        {
            "gate" => TestDefs.GateMap(),
            "abutting" => new MapDef(Fix.FromInt(60), Fix.FromInt(40),
                [new WallDef(FixVec2.FromInts(0, 10), FixVec2.FromInts(12, 11)), new WallDef(FixVec2.FromInts(12, 10), FixVec2.FromInts(20, 11))]),
            _ => LShape(),
        };
        var bad = new List<string>();
        for (int x = 0; x <= 600; x++)
        {
            for (int y = 80; y <= 220; y++)
            {
                var point = new FixVec2(Fix.Ratio(x, 10), Fix.Ratio(y, 10));
                var p = Terrain.Constrain(map, point, R);
                bool onMap = p.X >= R && p.Y >= R && p.X <= map.Width - R && p.Y <= map.Height - R;
                if (!onMap || map.Walls.Any(w => w.Contains(p)))
                {
                    bad.Add($"{point} -> {p}");
                }
            }
        }

        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void ConstrainToward_InsideAPalisade_LeavesOnTheSideOfTheAnchor()
    {
        // Halfway through a 0.2 m palisade the two faces tie: plain Constrain goes west (ties go west), but a slot whose
        // squad stands east of it is stood next to on the east side.
        var map = Palisade(20);
        var slot = new FixVec2(M(2010), Fix.FromInt(20));
        Assert.Multiple(() =>
        {
            Assert.That(Terrain.Constrain(map, slot, R), Is.EqualTo(new FixVec2(Fix.FromInt(20) - R, Fix.FromInt(20))));
            Assert.That(Terrain.ConstrainToward(map, slot, R, new FixVec2(M(2160), M(2050))), Is.EqualTo(new FixVec2(M(2020) + R, Fix.FromInt(20))));
            Assert.That(Terrain.ConstrainToward(map, slot, R, new FixVec2(M(1860), M(2050))), Is.EqualTo(new FixVec2(Fix.FromInt(20) - R, Fix.FromInt(20))));
        });
    }

    [Test]
    public void Constrain_NearACorner_IsPushedRadiallySoItSlidesRound()
    {
        // 0.1 m south-west of the (20, 10) corner, diagonally: pushed out along the diagonal to one radius from it.
        var p = Terrain.Constrain(OneBlock(), new FixVec2(Fix.FromInt(20) - Fix.Ratio(7, 100), Fix.FromInt(10) - Fix.Ratio(7, 100)), R);
        double fromCorner = (p - FixVec2.FromInts(20, 10)).Length.Raw / 65536.0;
        Assert.Multiple(() =>
        {
            Assert.That(fromCorner, Is.EqualTo(0.3).Within(0.0002));
            Assert.That(p.X.Raw, Is.EqualTo(p.Y.Raw - Fix.FromInt(10).Raw + Fix.FromInt(20).Raw).Within(2), "still on the diagonal");
        });
    }

    [Test]
    public void Constrain_ClearOfWalls_Unchanged()
    {
        var p = new FixVec2(Fix.FromInt(19), Fix.FromInt(12)); // 1 m west of the wall
        Assert.That(Terrain.Constrain(OneBlock(), p, R), Is.EqualTo(p));
    }

    [Test]
    public void SlideAlongWalls_RemovesOnlyThePartHeadingIntoTheWall()
    {
        var map = OneBlock();
        var nearWestFace = new FixVec2(Fix.FromInt(20) - M(35), Fix.FromInt(12)); // 5 cm of personal space left
        var intoAndAlong = new FixVec2(M(5), M(5));
        var slid = Terrain.SlideAlongWalls(map, MovementDef.Standard.PersonalSpace, nearWestFace, R, intoAndAlong);
        var away = new FixVec2(-M(5), Fix.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(slid.X.Raw, Is.EqualTo(0).Within(2), "nothing left heading into the wall");
            Assert.That(slid.Y, Is.EqualTo(M(5)), "the northward slide is kept");
            Assert.That(Terrain.SlideAlongWalls(map, MovementDef.Standard.PersonalSpace, nearWestFace, R, away), Is.EqualTo(away), "walking away is untouched");
        });
    }

    [Test]
    public void Resolve_BodyInAWall_IsPushedOut_TwoPressedAgainstItStayOutside()
    {
        var state = BattleSetup.Create(new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 2, FixVec2.FromInts(5, 5), 90)]), 1);
        var a = state.FighterList[0];
        var b = state.FighterList[1];
        a.Position = new FixVec2(Fix.FromInt(20) - M(10), Fix.FromInt(12)); // 0.2 m into the wall
        b.Position = new FixVec2(Fix.FromInt(20) - M(50), Fix.FromInt(12)); // pressing a from the west
        var weights = new List<int>();
        var roles = new List<CrowdRole>();
        Crowding.Fill(state, weights, roles);
        Crowding.Resolve(state, weights, roles);
        Assert.Multiple(() =>
        {
            Assert.That(Terrain.WallDepth(state.Scenario.Map, a.Position, R).Raw, Is.LessThanOrEqualTo(Tolerance), "a out of the wall");
            // Against a wall the press only halves per pass (the wall undoes a's share), so ~1 cm may remain after one
            // tick's passes — far under the 5 cm overlap tolerance, and gone over the next ticks.
            Assert.That((a.Position - b.Position).Length.Raw, Is.GreaterThanOrEqualTo(M(59).Raw), "and out of b");
        });
    }

    [Test]
    public void MoveSquad_TargetInsideAWall_GoesToTheNearestOpenGround()
    {
        var scenario = new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(10, 20), 90)]);
        var state = BattleSetup.Create(scenario, 1);
        var squad = state.Squads[0];
        new MoveSquadCommand(0, squad.Id, new FixVec2(Fix.FromInt(22), Fix.Ratio(135, 10)), Angle.FromDegrees(90)).Apply(state, null!);
        Assert.That(squad.Order.Target, Is.EqualTo(new FixVec2(Fix.FromInt(22), Fix.FromInt(14) + R)), "0.5 m inside the north face: out north");
    }

    [Test]
    public void Spawn_InOrAgainstAWall_IsAContentError()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 4, new FixVec2(Fix.FromInt(22), Fix.Ratio(156, 10)), 90)])); // rear rank at 14.1, 0.1 m off the wall
        Assert.That(ex!.Message, Does.Contain("is in or against a wall"));
    }

    [Test]
    [Category("Scenario")]
    public void ColumnThroughAGate_PassesFormsUp_NeverInAWall()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.ThroughTheGate(TestDefs.Column, 6), 1));
        int inWalls = 0;
        int overlap = 0;
        for (int i = 0; i < 700; i++)
        {
            sim.Step();
            inWalls = Math.Max(inWalls, MovementMetrics.FightersInWalls(sim.State));
            overlap = Math.Max(overlap, MovementMetrics.OverlappingFighters(sim.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(overlap, Is.Zero);
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None), "through and formed up");
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineThroughAGateNarrowerThanIt_SqueezesThrough_NeverInAWall()
    {
        // 4 files at 1 m span 3.6 m of bodies; the gate is 3 m: the outer files are pressed inward and still get through.
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.ThroughTheGate(TestDefs.Line), 1));
        int inWalls = 0;
        for (int i = 0; i < 900; i++)
        {
            sim.Step();
            inWalls = Math.Max(inWalls, MovementMetrics.FightersInWalls(sim.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(sim.State.Fighters.All(f => f.Position.Y > Fix.FromInt(20)), "everyone made it north of the wall");
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineOrderedAgainstAWall_MenStandAsCloseAsTheyCan_TheOrderEnds()
    {
        // The rear rank's slots fall inside the wall: those men press up to it, and the squad still forms up.
        // Starts east of the block, so its march west isn't blocked (paths around walls come in step 6).
        var scenario = new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 8, FixVec2.FromInts(32, 15), 90)],
            [new ScriptedOrderDef(1, "A", new FixVec2(Fix.FromInt(22), Fix.Ratio(149, 10)), Angle.FromDegrees(90))]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.StepTicks(900);
        var map = sim.State.Scenario.Map;
        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None), "formed up without the slots inside the wall");
            Assert.That(MovementMetrics.FightersInWalls(sim.State), Is.Zero);
            Assert.That(sim.State.Fighters.Max(f => Terrain.WallDepth(map, f.Position, f.Radius).Raw), Is.LessThanOrEqualTo(Tolerance));
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineOrderedWithSlotsOnAWallFace_SkipsThem_TheOrderEnds()
    {
        // Three ranks, three files at 1 m, ordered to (23, 15) facing north beside the (20, 10)–(24, 14) block: the
        // east file's rear slots lie exactly on the block's east face, where no body fits. They don't count.
        var deep = new FormationDef("test-deep", Fix.One, Fix.Ratio(3, 2), ranks: 3, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3));
        var scenario = new ScenarioDef("s", "", OneBlock(), [TestDefs.Squad("A", 0, 9, FixVec2.FromInts(34, 15), 90, deep)],
            [new ScriptedOrderDef(1, "A", FixVec2.FromInts(23, 15), Angle.FromDegrees(90))]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.StepTicks(1200);
        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None));
            Assert.That(MovementMetrics.FightersInWalls(sim.State), Is.Zero);
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineOrderedWithASlotInAGapNarrowerThanABody_SkipsIt_TheOrderEnds()
    {
        // Two blocks 0.4 m apart (a body is 0.6 m): the rear rank's second slot falls in the gap.
        var map = new MapDef(Fix.FromInt(60), Fix.FromInt(40),
            [new WallDef(FixVec2.FromInts(20, 10), FixVec2.FromInts(22, 14)), new WallDef(new FixVec2(M(2240), Fix.FromInt(10)), FixVec2.FromInts(24, 14))]);
        var scenario = new ScenarioDef("s", "", map, [TestDefs.Squad("A", 0, 8, FixVec2.FromInts(34, 15), 90)],
            [new ScriptedOrderDef(1, "A", new FixVec2(M(2270), Fix.FromInt(15)), Angle.FromDegrees(90))]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.StepTicks(1200);
        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None));
            Assert.That(MovementMetrics.FightersInWalls(sim.State), Is.Zero);
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineWithItsBackToAPalisade_RearRankStandsOnItsOwnSide_AndHoldsItsGround()
    {
        // Facing east 0.8 m from a 1 m palisade (as thick as the one in walls.json): the rear rank's slots fall 0.3 m
        // inside its far face. Those men stand against its near face with their squad, not on the far side, and count
        // as holding: within reach of the spot they stand on, not 1.6 m from one they can't get to.
        var scenario = new ScenarioDef("s", "", Palisade(100), [TestDefs.Squad("A", 0, 8, FixVec2.FromInts(25, 20), 0)],
            [new ScriptedOrderDef(1, "A", new FixVec2(M(2180), Fix.FromInt(20)), Angle.FromDegrees(0))]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.StepTicks(900);
        var state = sim.State;
        Assert.Multiple(() =>
        {
            Assert.That(state.Squads[0].Order, Is.EqualTo(SquadOrder.None));
            Assert.That(state.Fighters.All(f => f.Position.X > Fix.FromInt(21)), "everyone east of the palisade");
            Assert.That(state.Fighters.All(f => Crowding.Holding(state, f)), "everyone within reach of his slot");
            Assert.That(MovementMetrics.FightersInWalls(state), Is.Zero);
        });
    }
}
