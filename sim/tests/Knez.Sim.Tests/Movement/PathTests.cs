using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Movement;

/// <summary>
/// M1 step 6 (battle-sim.md §10): squads route around walls over a 0.5 m grid. Any gap a man fits through is passable;
/// before ground narrower than its formation a squad regroups into fewer files and widens again once through, and the
/// route search charges only the extra depth that costs (decided 2026-11-21); a target out of reach becomes the nearest
/// reachable spot on the squad's side of the obstacle (decided 2026-11-21); routes are planned first come first served
/// within a per-tick budget.
/// </summary>
[TestFixture]
public class PathTests
{
    private static readonly Fix R = TestDefs.Warrior.Radius;

    private static Fix M(int hundredths) => Fix.Ratio(hundredths, 100);

    private static MapDef Block() => new(Fix.FromInt(60), Fix.FromInt(40), [new WallDef(FixVec2.FromInts(20, 10), FixVec2.FromInts(24, 30))]);

    /// <summary>A 1 m wall across the map at y 19–20 with a narrow gap from x 29 (<paramref name="narrowCm"/> wide) and a
    /// 6 m gap centred on <paramref name="wideGapCentre"/>.</summary>
    private static MapDef TwoGaps(int wideGapCentre, int narrowCm = 200) => new(Fix.FromInt(80), Fix.FromInt(40),
    [
        new WallDef(FixVec2.FromInts(0, 19), FixVec2.FromInts(29, 20)),
        new WallDef(new FixVec2(M(2900 + narrowCm), Fix.FromInt(19)), FixVec2.FromInts(wideGapCentre - 3, 20)),
        new WallDef(FixVec2.FromInts(wideGapCentre + 3, 19), FixVec2.FromInts(80, 20)),
    ]);

    private static BattleSimulation Sim(MapDef map, SquadSpawnDef squad, FixVec2 target, int? facing = null) =>
        new(BattleSetup.Create(new ScenarioDef("s", "", map, [squad],
            [new ScriptedOrderDef(1, squad.Name, target, facing.HasValue ? Angle.FromDegrees(facing.Value) : null)]), 1));

    /// <summary>Where the squad's route crosses the line y = <paramref name="y"/> (anchor first), or null.</summary>
    private static double? CrossingX(BattleState state, Squad squad, FixVec2 from, double y)
    {
        var points = new[] { from }.Concat(squad.Path).Select(p => (X: p.X.Raw / 65536.0, Y: p.Y.Raw / 65536.0)).ToArray();
        for (int i = 1; i < points.Length; i++)
        {
            var (ax, ay) = points[i - 1];
            var (bx, by) = points[i];
            if ((ay - y) * (by - y) <= 0 && ay != by)
            {
                return ax + ((bx - ax) * (y - ay) / (by - ay));
            }
        }

        return null;
    }

    [Test]
    public void NavGrid_HalfMetreCells_ClearanceIsTheExactDistanceToTheNearestWallOrEdge()
    {
        var grid = new NavGrid(Block());
        int cell = grid.CellOf(new FixVec2(M(1910), M(2010))); // centre (19.25, 20.25): 0.75 m west of the block
        Assert.Multiple(() =>
        {
            Assert.That((grid.Columns, grid.Rows), Is.EqualTo((120, 80)));
            Assert.That(grid.Centre(cell), Is.EqualTo(new FixVec2(M(1925), M(2025))));
            Assert.That(grid.Clearance(cell), Is.EqualTo(M(75)));
            Assert.That(grid.Clearance(grid.CellOf(FixVec2.FromInts(22, 20))), Is.EqualTo(Fix.Zero), "inside the block");
            Assert.That(grid.Clearance(0), Is.EqualTo(M(25)), "the corner cell: a quarter metre from both edges");
            Assert.That(grid.CellOf(FixVec2.FromInts(-5, 99)), Is.EqualTo(79 * 120), "points off the map clamp onto it");
        });
    }

    [Test]
    public void MoveSquad_StraightLineHasRoom_GoesStraightWithoutWaiting()
    {
        var sim = Sim(Block(), TestDefs.Squad("A", 0, 8, FixVec2.FromInts(10, 35), 0), FixVec2.FromInts(40, 35));
        sim.Step();
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(squad.Path, Is.EqualTo(new[] { FixVec2.FromInts(40, 35) }));
            Assert.That(squad.PathRequestTick, Is.EqualTo(-1));
        });
    }

    [Test]
    [Category("Scenario")]
    public void MoveSquad_WallInTheWay_RoutesRoundIt_ArrivesFormedUp()
    {
        var sim = Sim(Block(), TestDefs.Squad("A", 0, 8, FixVec2.FromInts(10, 20), 0), FixVec2.FromInts(34, 20), facing: 0);
        sim.Step();
        var squad = sim.State.Squads[0];
        Assert.That(squad.Path.Count, Is.GreaterThan(1), "planned in the tick of the order");
        var map = sim.State.Scenario.Map;
        var from = squad.Anchor;
        foreach (var p in squad.Path)
        {
            Assert.That(Terrain.SegmentClear(map, from, p, R), Is.True, $"leg {from} → {p} keeps clear of the wall");
            from = p;
        }

        int inWalls = 0;
        for (int i = 0; i < 1200 && squad.Order.Kind == SquadOrderKind.Move; i++)
        {
            sim.Step();
            inWalls = Math.Max(inWalls, MovementMetrics.FightersInWalls(sim.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None), "arrived and formed up");
            Assert.That(squad.Anchor, Is.EqualTo(FixVec2.FromInts(34, 20)));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(0)), "the facing ordered");
            Assert.That(inWalls, Is.Zero);
        });
    }

    [Test]
    public void MoveSquad_NoFacing_EndsFacingAlongTheRoutesLastLeg()
    {
        // North round the block's top end, then south-east down to a target behind it: the last leg runs south-east.
        var sim = Sim(Block(), TestDefs.Squad("A", 0, 4, FixVec2.FromInts(18, 20), 90), FixVec2.FromInts(26, 20));
        sim.Step();
        var squad = sim.State.Squads[0];
        var last = squad.Path[^1] - (squad.Path.Count > 1 ? squad.Path[^2] : squad.Anchor);
        Assert.Multiple(() =>
        {
            Assert.That(squad.Order.FacingFromMarch, Is.True);
            Assert.That(squad.Order.Facing, Is.EqualTo(FixMath.Atan2(last.Y, last.X)));
            Assert.That(squad.Order.Facing, Is.Not.EqualTo(Angle.FromDegrees(0)), "not the straight line's east");
        });
    }

    [Test]
    public void Route_TwoMetreGap_TheLineRegroupsIntoTwoFilesRatherThanDetour_TheColumnJustGoes()
    {
        // The 2 m gap is straight ahead, a 6 m one 8 m east. Regrouping the 3.6 m line into 2 files makes it 3 m deeper
        // (4 ranks instead of 2): cheaper than the ~5 m detour. The 1.6 m column fits as it is.
        var line = Sim(TwoGaps(38), TestDefs.Squad("L", 0, 8, FixVec2.FromInts(30, 10), 90), FixVec2.FromInts(30, 30));
        var column = Sim(TwoGaps(38), TestDefs.Squad("C", 0, 6, FixVec2.FromInts(30, 10), 90, TestDefs.Column), FixVec2.FromInts(30, 30));
        line.Step();
        column.Step();
        var l = line.State.Squads[0];
        var c = column.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(CrossingX(line.State, l, FixVec2.FromInts(30, 10), 19.5), Is.InRange(29.0, 31.0), "the line goes by the narrow gap");
            Assert.That(l.PathFiles.Where(f => f > 0), Is.EqualTo(new[] { 2 }), "regrouping once, into 2 files");
            Assert.That(l.PathFiles.Count(f => f == 0), Is.EqualTo(1), "and widening once");
            Assert.That(CrossingX(column.State, c, FixVec2.FromInts(30, 10), 19.5), Is.InRange(29.0, 31.0), "the column too");
            Assert.That(c.PathFiles, Is.All.EqualTo(-1), "without regrouping");
        });
    }

    [Test]
    public void Route_GapOnlyASingleFileFits_TheLineTakesTheWideGapNearby()
    {
        // 1.2 m: single file, 8 ranks deep (9 m deeper) — the ~5 m detour to the wide gap is cheaper.
        var line = Sim(TwoGaps(38, narrowCm: 120), TestDefs.Squad("L", 0, 8, FixVec2.FromInts(30, 10), 90), FixVec2.FromInts(30, 30));
        line.Step();
        var squad = line.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(CrossingX(line.State, squad, FixVec2.FromInts(30, 10), 19.5), Is.InRange(35.0, 41.0));
            Assert.That(squad.PathFiles, Is.All.EqualTo(-1));
        });
    }

    [Test]
    [Category("Scenario")]
    public void LineThroughATwoMetreGap_StopsToRegroup_PassesInTwoFiles_WidensAndFormsUp()
    {
        var sim = Sim(TwoGaps(70), TestDefs.Squad("L", 0, 8, FixVec2.FromInts(30, 10), 90), FixVec2.FromInts(30, 30));
        var squad = sim.State.Squads[0];
        int regroupTicks = 0;
        int narrowTicks = 0;
        int inWalls = 0;
        for (int i = 0; i < 1500 && (i < 2 || squad.Order.Kind == SquadOrderKind.Move); i++)
        {
            sim.Step();
            regroupTicks += squad.RegroupSince >= 0 ? 1 : 0;
            narrowTicks += squad.Files == 2 ? 1 : 0;
            inWalls = Math.Max(inWalls, MovementMetrics.FightersInWalls(sim.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None), "through and formed up");
            Assert.That(squad.Files, Is.Zero, "back in its own width");
            Assert.That(regroupTicks, Is.InRange(1, MovementDef.Standard.RegroupMaxWaitTicks), "it stopped to regroup, within the limit");
            Assert.That(narrowTicks, Is.GreaterThan(100), "and went through in two files");
            Assert.That(inWalls, Is.Zero);
            Assert.That(sim.State.Fighters.All(f => f.Position.Y > Fix.FromInt(20)));
        });
    }

    [Test]
    public void SlotOffset_NarrowedToTwoFiles_InnerFilesInFront_FlanksBehind()
    {
        // A line of 8 (4 files × 2 ranks) narrowed to 2 files: the left file takes old files 0–1, the right 2–3; within
        // each, old rank first, inner file before outer one. x: ±0.5 m; ranks 1.5 m apart.
        var offsets = Enumerable.Range(0, 8).Select(i => Knez.Sim.Formations.FormationLayout.SlotOffset(TestDefs.Line, 8, i, 2)).ToArray();
        Fix x0 = -Fix.Ratio(1, 2);
        Fix x1 = Fix.Ratio(1, 2);
        Fix Rank(int r) => -(Fix.Ratio(3, 2) * r);
        Assert.That(offsets, Is.EqualTo(new[]
        {
            new FixVec2(x0, Rank(1)), new FixVec2(x0, Rank(0)), new FixVec2(x1, Rank(0)), new FixVec2(x1, Rank(1)),
            new FixVec2(x0, Rank(3)), new FixVec2(x0, Rank(2)), new FixVec2(x1, Rank(2)), new FixVec2(x1, Rank(3)),
        }));
    }

    [Test]
    [Category("Scenario")]
    public void MoveSquad_TargetWalledIn_GoesToTheNearestReachableSpot_AndTheOrderEnds()
    {
        // A closed 6 × 6 m yard (walls 0.5 m thick); the target is in its middle. The squad comes from the west, so it
        // stops just outside the west wall, not at the north or south wall, though those are as close (decided
        // 2026-11-21: the squad's own side of the obstacle).
        var yard = new MapDef(Fix.FromInt(60), Fix.FromInt(40),
        [
            new WallDef(FixVec2.FromInts(30, 15), new FixVec2(Fix.FromInt(36), M(1550))),
            new WallDef(new FixVec2(Fix.FromInt(30), M(2050)), FixVec2.FromInts(36, 21)),
            new WallDef(FixVec2.FromInts(30, 15), new FixVec2(M(3050), Fix.FromInt(21))),
            new WallDef(new FixVec2(M(3550), Fix.FromInt(15)), FixVec2.FromInts(36, 21)),
        ]);
        var sim = Sim(yard, TestDefs.Squad("A", 0, 4, FixVec2.FromInts(10, 18), 0), FixVec2.FromInts(33, 18));
        sim.Step();
        var squad = sim.State.Squads[0];
        var target = squad.Order.Target;
        Assert.Multiple(() =>
        {
            Assert.That(target.X, Is.LessThan(Fix.FromInt(30)), "outside the yard, on the near side");
            Assert.That(target.X, Is.GreaterThan(Fix.FromInt(29)), "right against its west wall");
            Assert.That(squad.Path[^1], Is.EqualTo(target));
        });

        sim.StepTicks(900);
        Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
    }

    [Test]
    public void PathSystem_ManyRequestsInOneTick_ServedInIdOrderWithinTheBudget()
    {
        // A wall across a 120 × 100 m map with one gap at its far east end: each search floods the whole south half
        // (well over the 4 000-node budget) before it finds the gap, so one squad is served per tick.
        var map = new MapDef(Fix.FromInt(120), Fix.FromInt(100), [new WallDef(FixVec2.FromInts(0, 50), FixVec2.FromInts(115, 51))]);
        var squads = Enumerable.Range(0, 3).Select(i => TestDefs.Squad("S" + i, 0, 2, FixVec2.FromInts(10 + (10 * i), 10), 90)).ToArray();
        var scenario = new ScenarioDef("s", "", map, squads,
            squads.Select(s => new ScriptedOrderDef(1, s.Name, new FixVec2(s.Position.X, Fix.FromInt(90)), null)).ToArray());
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var planned = new List<int>();
        for (int tick = 1; tick <= 3; tick++)
        {
            sim.Step();
            planned.Add(sim.State.Squads.Count(s => s.PathRequestTick < 0));
        }

        Assert.Multiple(() =>
        {
            Assert.That(planned, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(sim.State.Squads.Select(s => s.Path.Count > 1), Is.All.True, "each routed through the gap");
        });
    }

    [Test]
    [Category("Determinism")]
    public void Snapshot_WhileWaitingForARouteAndWhileFollowingIt_ContinuesIdentically()
    {
        var map = new MapDef(Fix.FromInt(120), Fix.FromInt(100), [new WallDef(FixVec2.FromInts(0, 50), FixVec2.FromInts(115, 51))]);
        var squads = Enumerable.Range(0, 3).Select(i => TestDefs.Squad("S" + i, 0, 2, FixVec2.FromInts(10 + (10 * i), 10), 90)).ToArray();
        var scenario = new ScenarioDef("s", "", map, squads,
            squads.Select(s => new ScriptedOrderDef(1, s.Name, new FixVec2(s.Position.X, Fix.FromInt(90)), null)).ToArray());
        foreach (int at in new[] { 1, 200 })
        {
            var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
            sim.StepTicks(at); // after tick 1 two squads still wait; by 200 all march their routes
            var restored = BattleSimulation.Restore(sim.TakeSnapshot());
            sim.StepTicks(300);
            restored.StepTicks(300);
            Assert.That(restored.ComputeHash(), Is.EqualTo(sim.ComputeHash()), $"snapshot at tick {at}");
        }
    }

    /// <summary>A 40 × 20 m map with a 12.5 cm fence along x ≈ 10.1 from y 0 to 18: a 2 m gap at its north end.</summary>
    private static MapDef ThinFence() => new(Fix.FromInt(40), Fix.FromInt(20),
        [new WallDef(new FixVec2(Fix.Ratio(161, 16), Fix.Zero), new FixVec2(Fix.Ratio(163, 16), Fix.FromInt(18)))]);

    /// <summary>Every leg of the route, sampled every 2 cm, never has a point strictly inside a wall (walls are ≥ 10 cm).</summary>
    private static void AssertEveryLegClear(MapDef map, Squad squad, FixVec2 from, string what)
    {
        foreach (var p in squad.Path)
        {
            var leg = p - from;
            long samples = Math.Max(1, leg.Length.Raw / 1310);
            for (long i = 0; i <= samples; i++)
            {
                var point = new FixVec2(from.X + Fix.FromRaw(leg.X.Raw * i / samples), from.Y + Fix.FromRaw(leg.Y.Raw * i / samples));
                for (int w = 0; w < map.Walls.Count; w++)
                {
                    if (map.Walls[w].Contains(point))
                    {
                        Assert.Fail($"{what}: leg {from} → {p} enters {map.Walls[w]} at {point}");
                    }
                }
            }

            from = p;
        }
    }

    [TestCase(20, 5, true)]
    [TestCase(5, 20, false)]
    [Category("Scenario")]
    public void ThinFence_AnchorOrTargetInTheFencesCell_RoutesThroughTheGap_AndTheOrderEnds(int fromX, int toX, bool startNearFence)
    {
        // Reviewer's repro (2026-11-21): one leg from the anchor (or into the target) lay in a cell the fence crosses and
        // went straight through it; the man stuck against the fence and the order never ended.
        var near = new FixVec2(Fix.Ratio(1343, 128), Fix.FromInt(2)); // 0.3 m east of the fence
        var start = startNearFence ? near : FixVec2.FromInts(fromX, 2);
        var target = startNearFence ? FixVec2.FromInts(toX, 2) : near;
        var fighter = new SquadSpawnDef("A", 0, TestDefs.Warrior, 1, TestDefs.Line, start, Angle.FromDegrees(90));
        var sim = Sim(ThinFence(), fighter, target);
        sim.Step();
        var squad = sim.State.Squads[0];
        AssertEveryLegClear(sim.State.Scenario.Map, squad, start, "planned");
        sim.StepTicks(2000);
        Assert.Multiple(() =>
        {
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None), "through the gap and formed up");
            Assert.That((sim.State.Fighters[0].Position - target).Length, Is.LessThan(Fix.Ratio(3, 10)));
        });
    }

    [Test]
    public void CrossesWall_ThroughAlongAndPast_OnlyThroughTheInsideCounts()
    {
        var map = Block(); // (20, 10)–(24, 30)
        Assert.Multiple(() =>
        {
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(10, 20), FixVec2.FromInts(30, 20)), Is.True, "straight through");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(30, 20), FixVec2.FromInts(10, 20)), Is.True, "the other way");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(19, 9), FixVec2.FromInts(21, 11)), Is.True, "clipping a corner");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(18, 8), FixVec2.FromInts(22, 12)), Is.True, "diagonally into it");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(19, 9), FixVec2.FromInts(21, 9)), Is.False, "passing below");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(20, 5), FixVec2.FromInts(20, 35)), Is.False, "along its west face");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(19, 11), FixVec2.FromInts(21, 9)), Is.False, "touching only its corner");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(10, 20), FixVec2.FromInts(19, 20)), Is.False, "stopping short");
            Assert.That(Terrain.CrossesWall(map, FixVec2.FromInts(22, 20), FixVec2.FromInts(22, 20)), Is.True, "a point inside");
        });
    }

    [TestCase("paths")]
    [TestCase("fence")]
    public void Plan_AnyTargetFromSeveralStarts_EveryLegKeepsClearOfWalls(string mapName)
    {
        var map = mapName == "fence" ? ThinFence() : new MapDef(Fix.FromInt(60), Fix.FromInt(40),
        [
            new WallDef(FixVec2.FromInts(0, 19), FixVec2.FromInts(17, 20)), new WallDef(FixVec2.FromInts(19, 19), FixVec2.FromInts(23, 20)),
            new WallDef(FixVec2.FromInts(29, 19), FixVec2.FromInts(36, 20)), new WallDef(FixVec2.FromInts(44, 22), FixVec2.FromInts(52, 27)),
            new WallDef(FixVec2.FromInts(4, 28), new FixVec2(Fix.FromInt(10), M(2850))), new WallDef(new FixVec2(Fix.FromInt(4), M(3350)), FixVec2.FromInts(10, 34)),
            new WallDef(FixVec2.FromInts(4, 28), new FixVec2(M(450), Fix.FromInt(34))), new WallDef(new FixVec2(M(950), Fix.FromInt(28)), FixVec2.FromInts(10, 34)),
        ]);
        var state = BattleSetup.Create(new ScenarioDef("s", "", map, [TestDefs.Squad("A", 0, 8, FixVec2.FromInts(3, 3), 90)]), 1);
        var squad = state.SquadList[0];
        var grid = new NavGrid(map);
        var scratch = new PathScratch(grid.Count);
        int width = map.Width.Raw > Fix.FromInt(50).Raw ? 60 : 40;
        int height = width == 60 ? 40 : 20;
        var starts = new[] { new FixVec2(M(130), M(170)), new FixVec2(M(2510), M(1210)), new FixVec2(M(1043), M(1530)), new FixVec2(M(5590), M(3870)) };
        int planned = 0;
        foreach (var start in starts.Where(p => p.X.Raw < Fix.FromInt(width).Raw && p.Y.Raw < Fix.FromInt(height).Raw))
        {
            for (int x = 1; x < width * 2; x += 4)
            {
                for (int y = 1; y < height * 2; y += 4)
                {
                    var target = Terrain.Constrain(map, new FixVec2(Fix.Ratio((x * 50) + 17, 100), Fix.Ratio((y * 50) + 9, 100)), R);
                    squad.Anchor = start;
                    squad.Order = SquadOrder.Move(target, Angle.Zero);
                    PathPlanner.Plan(state, squad, grid, scratch);
                    AssertEveryLegClear(map, squad, start, $"{start} → {target}");
                    planned++;
                }
            }
        }

        Assert.That(planned, Is.GreaterThan(150));
    }

    [Test]
    public void NewOrder_WhileWaitingForARouteOrMidRoute_ReplacesTheRoute()
    {
        var map = new MapDef(Fix.FromInt(120), Fix.FromInt(100), [new WallDef(FixVec2.FromInts(0, 50), FixVec2.FromInts(115, 51))]);
        var squads = Enumerable.Range(0, 2).Select(i => TestDefs.Squad("S" + i, 0, 2, FixVec2.FromInts(10 + (10 * i), 10), 90)).ToArray();
        var scenario = new ScenarioDef("s", "", map, squads,
            squads.Select(s => new ScriptedOrderDef(1, s.Name, new FixVec2(s.Position.X, Fix.FromInt(90)), null)).ToArray());
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.Step(); // S0 planned, S1 still waiting (the budget went on S0)
        var s0 = sim.State.Squads[0];
        var s1 = sim.State.Squads[1];
        Assert.That(s1.PathRequestTick, Is.EqualTo(1));
        sim.Enqueue(new MoveSquadCommand(0, s1.Id, FixVec2.FromInts(25, 20), null)); // open ground: straight, no wait
        sim.StepTicks(100);
        sim.Enqueue(new MoveSquadCommand(0, s0.Id, FixVec2.FromInts(10, 30), null)); // mid-route: back to straight
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(s1.Path, Is.EqualTo(new[] { FixVec2.FromInts(25, 20) }));
            Assert.That(s1.PathRequestTick, Is.EqualTo(-1));
            Assert.That(s0.Path, Is.EqualTo(new[] { FixVec2.FromInts(10, 30) }));
            Assert.That(s0.PathIndex, Is.Zero);
        });
    }
}
