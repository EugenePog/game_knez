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
/// Regrouping for narrow gaps (decided 2026-11-21, battle-sim.md §10): a squad narrows to as many files as the corridor
/// allows a little before it, goes through centred, and widens once its rear is through. The measure throughout is the
/// slots, not the bodies: walls push bodies out, so a body count can't see a formation laid across a wall.
/// </summary>
[TestFixture]
public class RegroupTests
{
    private static Fix M(int hundredths) => Fix.Ratio(hundredths, 100);

    /// <summary>A 1 m wall across the map at y 19–20 with a 2 m gap from x = <paramref name="gapFromHundredths"/>/100.</summary>
    private static MapDef Gap(int gapFromHundredths = 2900) => new(Fix.FromInt(80), Fix.FromInt(40),
    [
        new WallDef(FixVec2.FromInts(0, 19), new FixVec2(M(gapFromHundredths), Fix.FromInt(20))),
        new WallDef(new FixVec2(M(gapFromHundredths + 200), Fix.FromInt(19)), FixVec2.FromInts(80, 20)),
    ]);

    private static BattleSimulation Line(MapDef map, FixVec2 from, int facing, FixVec2 target, int count = 8) =>
        new(BattleSetup.Create(new ScenarioDef("s", "", map, [TestDefs.Squad("L", 0, count, from, facing)],
            [new ScriptedOrderDef(1, "L", target, null)]), 1));

    /// <summary>Fighters whose slot has no room for a body right now (past an edge, in or against a wall).</summary>
    private static int SlotsInWalls(BattleState state)
    {
        int count = 0;
        foreach (var fighter in state.Fighters)
        {
            var squad = state.FindSquad(fighter.SquadId)!;
            var slot = SlotTracking.Position(squad, SlotTracking.Offset(squad, fighter));
            count += Terrain.Constrain(state.Scenario.Map, slot, fighter.Radius) != slot ? 1 : 0;
        }

        return count;
    }

    /// <summary>Steps until the squad's order ends (or <paramref name="limit"/>), counting slot-in-wall fighter-ticks.</summary>
    private static int Run(BattleSimulation sim, int limit = 1500)
    {
        int inWalls = 0;
        var squad = sim.State.Squads[0];
        for (int i = 0; i < limit && (i < 2 || squad.Order.Kind == SquadOrderKind.Move); i++)
        {
            sim.Step();
            inWalls += SlotsInWalls(sim.State);
        }

        return inWalls;
    }

    [TestCase(2900)]
    [TestCase(2925)]
    [TestCase(2950)]
    [TestCase(2975)]
    [Category("Scenario")]
    public void ThroughATwoMetreGap_WhereverItSitsOnTheGrid_NoSlotEverInAWall(int gapFrom)
    {
        // Reviewer's repro J: the gap's middle on a cell edge used to leave one narrowed file in the wall.
        var x = M(gapFrom + 100);
        var sim = Line(Gap(gapFrom), new FixVec2(x, Fix.FromInt(10)), 90, new FixVec2(x, Fix.FromInt(30)));
        int inWalls = Run(sim);
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.Files, Is.Zero, "widened again");
        });
    }

    [Test]
    [Category("Scenario")]
    public void TargetJustPastTheGap_StaysNarrowedTillTheRearIsThrough_NoSlotInAWall()
    {
        // Reviewer's repro H: it used to widen on arrival with its rear still in the gap.
        var sim = Line(Gap(), FixVec2.FromInts(30, 10), 90, new FixVec2(Fix.FromInt(30), M(2150)));
        int inWalls = Run(sim);
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.Files, Is.EqualTo(2), "ends in two files: widening would put its rear in the wall");
        });
    }

    [Test]
    [Category("Scenario")]
    public void TargetInTheGap_RegroupsInsteadOfGoingStraightInFullWidth()
    {
        // Reviewer's repro B: the straight-line shortcut took it in four files wide.
        var sim = Line(Gap(), FixVec2.FromInts(30, 10), 90, new FixVec2(Fix.FromInt(30), M(1950)));
        int inWalls = Run(sim);
        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(sim.State.Squads[0].Files, Is.EqualTo(2));
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None));
        });
    }

    [Test]
    [Category("Scenario")]
    public void NewOrderWhileNarrowedInTheGap_StaysNarrowedUntilThrough()
    {
        // Reviewer's repro C: a new order used to widen it at once, inside the gap.
        var sim = Line(Gap(), FixVec2.FromInts(30, 10), 90, FixVec2.FromInts(30, 30));
        var squad = sim.State.Squads[0];
        for (int i = 0; i < 1500 && !(squad.Files == 2 && squad.Anchor.Y > Fix.FromInt(20)); i++)
        {
            sim.Step();
        }

        Assert.That(squad.Files, Is.EqualTo(2), "precondition: narrowed, front just through");
        sim.Enqueue(new MoveSquadCommand(0, squad.Id, FixVec2.FromInts(30, 34), null));
        int inWalls = Run(sim);
        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.Zero);
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.Files, Is.Zero, "widened once through");
        });
    }

    [TestCase(15, 16)]
    [TestCase(15, 17)]
    [TestCase(10, 17)]
    [TestCase(20, 15)]
    [TestCase(20, 17)]
    [Category("Scenario")]
    public void ApproachingAlongTheWall_RegroupsFacingTheGap(int fromX, int fromY)
    {
        // Reviewer's repro F: it used to form its column along the wall, then pivot in the gap's mouth (130 slot-in-wall
        // fighter-ticks). Now it marches along the wall shifted clear of it, regroups facing the gap and goes in on a short
        // diagonal; on that leg one man's slot grazes the wall's corner for a few ticks (6 measured): the sideways room
        // is sampled at cell centres. Known, for the bench.
        // From y 17 the shifted runs' corner used to land behind the anchor (81 slot-in-wall fighter-ticks).
        var sim = Line(Gap(), FixVec2.FromInts(fromX, fromY), 0, FixVec2.FromInts(30, 30));
        sim.Step();
        AssertNoDoublingBack(sim.State.Squads[0], FixVec2.FromInts(fromX, fromY), "planned route");
        int inWalls = Run(sim);
        Assert.Multiple(() =>
        {
            Assert.That(inWalls, Is.LessThanOrEqualTo(10));
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(SquadOrder.None));
        });
    }

    [Test]
    [Category("Scenario")]
    public void BackToAPalisade_IsNoNarrowGround()
    {
        // A single wall behind (or beside) a squad never makes it regroup: only gaps do. Ordered to stand 0.9 m north of a
        // palisade, facing north, it ends in its own width.
        var map = new MapDef(Fix.FromInt(60), Fix.FromInt(40), [new WallDef(FixVec2.FromInts(5, 19), FixVec2.FromInts(55, 20))]);
        var sim = new BattleSimulation(BattleSetup.Create(new ScenarioDef("s", "", map, [TestDefs.Squad("L", 0, 8, FixVec2.FromInts(30, 30), 270)],
            [new ScriptedOrderDef(1, "L", new FixVec2(Fix.FromInt(30), M(2090)), Angle.FromDegrees(90))]), 1));
        Run(sim);
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(squad.Files, Is.Zero);
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.PathFiles, Is.Empty.Or.All.EqualTo(-1));
        });
    }

    [TestCase(6, 2)]
    [TestCase(8, 3)]
    [TestCase(10, 2)]
    [TestCase(12, 3)]
    [TestCase(7, 2)]
    [TestCase(8, 1)]
    [TestCase(9, 3)]
    [TestCase(11, 4)]
    [TestCase(13, 3)]
    public void NarrowedLayout_FilesAsEvenAsPossible_EverySlotDistinct(int count, int files)
    {
        // Reviewer's repro A: uneven bands made the column deeper than the planner thought. 12 men use a 6-wide line
        // (2 ranks); the rest use the test line (2 ranks).
        var formation = count == 12 ? new FormationDef("six", Fix.One, Fix.Ratio(3, 2), ranks: 0, width: 6, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3)) : TestDefs.Line;
        var offsets = Enumerable.Range(0, count).Select(s => FormationLayout.SlotOffset(formation, count, s, files)).ToArray();
        int deepest = offsets.GroupBy(o => o.X).Max(g => g.Count());
        Assert.Multiple(() =>
        {
            Assert.That(offsets.Distinct().Count(), Is.EqualTo(count));
            Assert.That(offsets.Select(o => o.X).Distinct().Count(), Is.EqualTo(files));
            Assert.That(deepest, Is.EqualTo((count + files - 1) / files), "files differ by one man at most");
            Assert.That(FormationLayout.NarrowedRanks(formation, count, files), Is.EqualTo(deepest));
        });
    }

    [Test]
    [Category("Determinism")]
    public void Snapshot_WhileRegroupingAndWhileNarrowed_ContinuesIdentically()
    {
        var reference = Line(Gap(), FixVec2.FromInts(30, 10), 90, FixVec2.FromInts(30, 30));
        int regroupingAt = -1;
        int narrowedAt = -1;
        for (int i = 0; i < 400 && (regroupingAt < 0 || narrowedAt < 0); i++)
        {
            reference.Step();
            var squad = reference.State.Squads[0];
            if (regroupingAt < 0 && squad.RegroupSince >= 0)
            {
                regroupingAt = reference.State.Tick;
            }

            if (narrowedAt < 0 && squad.Files > 0 && squad.RegroupSince < 0)
            {
                narrowedAt = reference.State.Tick;
            }
        }

        Assert.That(regroupingAt >= 0 && narrowedAt >= 0, Is.True, "both moments happened");
        foreach (int at in new[] { regroupingAt, narrowedAt })
        {
            var sim = Line(Gap(), FixVec2.FromInts(30, 10), 90, FixVec2.FromInts(30, 30));
            sim.StepTicks(at);
            var restored = BattleSimulation.Restore(sim.TakeSnapshot());
            sim.StepTicks(300);
            restored.StepTicks(300);
            Assert.That(restored.ComputeHash(), Is.EqualTo(sim.ComputeHash()), $"snapshot at tick {at}");
        }
    }

    [Test]
    public void NarrowedLayout_EveryFormationAndCount_FilesDifferByOneManAtMost()
    {
        var formations = new[]
        {
            TestDefs.Line, TestDefs.Column,
            new FormationDef("w4", Fix.One, Fix.Ratio(3, 2), ranks: 0, width: 4, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3)),
            new FormationDef("r3", Fix.One, Fix.Ratio(3, 2), ranks: 3, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3)),
        };
        var bad = new List<string>();
        foreach (var formation in formations)
        {
            for (int count = 1; count <= 24; count++)
            {
                int width = FormationLayout.Width(formation, count);
                for (int files = 1; files < width; files++)
                {
                    int deepest = Enumerable.Range(0, count).Select(s => FormationLayout.SlotOffset(formation, count, s, files))
                        .GroupBy(o => o.X).Max(g => g.Count());
                    if (deepest != (count + files - 1) / files)
                    {
                        bad.Add($"{formation.Id} {count} men → {files} files: {deepest} deep");
                    }
                }
            }
        }

        Assert.That(bad, Is.Empty);
    }

    /// <summary>No two legs in a row turn back on each other (more than 90°) where either is shorter than half a cell.</summary>
    private static void AssertNoDoublingBack(Squad squad, FixVec2 anchor, string what)
    {
        var points = new[] { anchor }.Concat(squad.Path).ToArray();
        for (int i = 1; i + 1 < points.Length; i++)
        {
            var inLeg = points[i] - points[i - 1];
            var outLeg = points[i + 1] - points[i];
            bool shortLeg = inLeg.Length < Fix.Ratio(1, 2) || outLeg.Length < Fix.Ratio(1, 2);
            Assert.That(shortLeg && FixVec2.Dot(inLeg, outLeg) < Fix.Zero, Is.False,
                $"{what}: {points[i - 1]} → {points[i]} → {points[i + 1]} doubles back");
        }
    }

    [Test]
    public void RandomMaps_NoPlannedRouteDoublesBack()
    {
        // The reviewer's fuzz (2026-11-21) found 25 doubled-back legs in 400 random maps. Seeded, so it is the same set
        // every run; System.Random is fine in a test.
        var random = new Random(20261121);
        int planned = 0;
        for (int trial = 0; trial < 200; trial++)
        {
            var walls = new List<WallDef>();
            int wallCount = random.Next(3, 12);
            for (int w = 0; w < wallCount; w++)
            {
                int x = random.Next(4, 72);
                int y = random.Next(4, 34);
                bool horizontal = random.Next(2) == 0;
                int length = random.Next(3, 15);
                var from = FixVec2.FromInts(x, y);
                var to = horizontal ? new FixVec2(Fix.FromInt(Math.Min(78, x + length)), Fix.FromInt(y) + Fix.Ratio(random.Next(2, 11), 10))
                                    : new FixVec2(Fix.FromInt(x) + Fix.Ratio(random.Next(2, 11), 10), Fix.FromInt(Math.Min(38, y + length)));
                walls.Add(new WallDef(from, to));
            }

            var map = new MapDef(Fix.FromInt(80), Fix.FromInt(40), walls);
            var start = FixVec2.FromInts(random.Next(3, 77), random.Next(3, 37));
            var target = FixVec2.FromInts(random.Next(3, 77), random.Next(3, 37));
            ScenarioDef scenario;
            try
            {
                scenario = new ScenarioDef("s", "", map, [TestDefs.Squad("L", 0, 8, start, random.Next(0, 4) * 90)]);
            }
            catch (ArgumentException)
            {
                continue; // spawned in or against a wall: not a route question
            }

            var state = BattleSetup.Create(scenario, 1);
            var squad = state.SquadList[0];
            squad.Order = SquadOrder.Move(Terrain.Constrain(map, target, TestDefs.Man.Radius), Angle.Zero, facingFromMarch: true);
            var grid = new NavGrid(map);
            PathPlanner.Plan(state, squad, grid, new PathScratch(grid.Count));
            AssertNoDoublingBack(squad, start, $"trial {trial}");
            planned++;
        }

        Assert.That(planned, Is.GreaterThan(100));
    }

    [Test]
    public void PersonalSpaceWiderThanTheGapBetweenMen_IsAContentError()
    {
        var tight = new FormationDef("tight", Fix.Ratio(65, 100), Fix.One, ranks: 2, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3));
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map,
            [new SquadSpawnDef("A", 0, TestDefs.Man, TestDefs.Men(4), tight, FixVec2.FromInts(10, 10), Angle.FromDegrees(90))]));
        Assert.That(ex!.Message, Does.Contain("personalSpace"));
    }
}
