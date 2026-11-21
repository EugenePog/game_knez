using Knez.Sim.Commands;
using Knez.Sim.Content;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Formations;

/// <summary>
/// M1 step 7 (battle-sim.md §10, decided 2026-11-21): losses close ranks from the back and keep the front; a squad
/// changes formation on the move at half pace; it wheels round its inner flank at corners and faces about for more than a
/// quarter turn. Formation changes name formations from the scenario's catalogue.
/// </summary>
[TestFixture]
public class FormationChangeTests
{
    /// <summary>A line of 8 at (20, 10) facing north, with line and column in the catalogue.</summary>
    private static BattleSimulation LineWithColumnAvailable(params ScriptedOrderDef[] script) =>
        new(BattleSetup.Create(new ScenarioDef("s", "", TestDefs.Map, [TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90)],
            script, formations: [TestDefs.Line, TestDefs.Column]), 1));

    private static Squad Line(BattleSimulation sim) => sim.State.Squads[0];

    private static FixVec2 SlotOf(BattleState state, Fighter fighter)
    {
        var squad = state.FindSquad(fighter.SquadId)!;
        return SlotTracking.Position(squad, SlotTracking.Offset(squad, fighter));
    }

    private static Fighter InSlot(BattleState state, int slot) => state.Fighters.Single(f => f.IsAlive && f.SlotIndex == slot);

    [Test]
    public void Loss_InTheFrontRank_TheManBehindStepsUp_TheFrontKeepsItsWidth()
    {
        // 4 × 2: slots 0–3 front, 4–7 rear. Slot 1 falls: slot 5 (behind him) steps up; the rear rank closes to 3, centred.
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), 1));
        var behind = InSlot(sim.State, 5);
        InSlot(sim.State, 1).IsAlive = false;
        sim.Step();
        var squad = Line(sim);
        Assert.Multiple(() =>
        {
            Assert.That(squad.Members, Has.Count.EqualTo(7));
            Assert.That(squad.Frontage, Is.EqualTo(4));
            Assert.That(behind.SlotIndex, Is.EqualTo(1), "the man behind the gap took it");
            Assert.That(sim.State.Fighters.Where(f => f.IsAlive).Select(f => f.SlotIndex).Order(), Is.EqualTo(Enumerable.Range(0, 7)));
            Assert.That(sim.State.Fighters.Single(f => !f.IsAlive).SquadId, Is.EqualTo(EntityId.None), "the fallen leave the squad");
        });
    }

    [Test]
    public void Losses_TheRearEmptiesFirst_TheFrontNarrowsOnlyWhenFewerThanItsFilesRemain()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), 1));
        foreach (int slot in new[] { 0, 2 })
        {
            InSlot(sim.State, slot).IsAlive = false;
        }

        sim.Step();
        var squad = Line(sim);
        var front = sim.State.Fighters.Where(f => f.IsAlive && f.SlotIndex < 4).ToArray();
        Assert.That(front, Has.Length.EqualTo(4), "6 men: the front is still 4 wide, 2 behind");

        foreach (var fighter in sim.State.Fighters.Where(f => f.IsAlive).Take(3))
        {
            fighter.IsAlive = false;
        }

        sim.Step();
        var offsets = squad.Members.Select(id => sim.State.FindFighter(id)!).Select(f => SlotTracking.Offset(squad, f)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(squad.Members, Has.Count.EqualTo(3));
            Assert.That(offsets.All(o => o.Y == Fix.Zero), "3 men: one rank of 3");
        });
    }

    [Test]
    public void SetFormation_LineToColumn_ReformsOnTheMoveAtHalfPace_ThenFullPace()
    {
        var sim = LineWithColumnAvailable(new ScriptedOrderDef(1, "Line", FixVec2.FromInts(20, 34), null));
        sim.StepTicks(20);
        sim.Enqueue(new SetFormationCommand(0, Line(sim).Id, "test-column"));
        sim.Step();
        var squad = Line(sim);
        Fix reformingStep = squad.Velocity.Length;
        Assert.Multiple(() =>
        {
            Assert.That(squad.Formation, Is.SameAs(TestDefs.Column));
            Assert.That(squad.Frontage, Is.EqualTo(2));
            Assert.That(squad.Reforming, Is.True);
            Assert.That(squad.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "keeps its order");
            Assert.That(sim.State.Fighters.Select(f => f.SlotIndex).Distinct().Count(), Is.EqualTo(8));
        });

        sim.Step();
        Assert.That(Line(sim).Velocity.Length.Raw, Is.EqualTo(TestDefs.Warrior.WalkPerTick.Raw / 2).Within(2), "half pace while re-forming");
        int ticks = 0;
        while (Line(sim).Reforming && ticks++ < 200)
        {
            sim.Step();
        }

        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(ticks, Is.LessThan(200), "formed up");
            Assert.That(Line(sim).Velocity.Length, Is.EqualTo(TestDefs.Warrior.WalkPerTick), "back to full pace");
        });
    }

    [Test]
    public void SetFormation_FromTheScript_AndUnknownOrWrongSide_Rejected()
    {
        var sim = LineWithColumnAvailable(ScriptedOrderDef.Formation(3, "Line", "test-column"));
        sim.StepTicks(3);
        Assert.That(Line(sim).Formation, Is.SameAs(TestDefs.Column));

        sim.Enqueue(new SetFormationCommand(0, Line(sim).Id, "phalanx"));
        sim.Enqueue(new SetFormationCommand(1, Line(sim).Id, "test-line"));
        sim.Step();
        var reasons = sim.State.Events.OfType<CommandRejected>().Select(e => e.Reason.Code).ToArray();
        Assert.That(reasons, Is.EquivalentTo(new[] { "unknown-formation", "wrong-side" }));
    }

    [Test]
    public void Catalogue_AFormationNoSquadUses_StillChangesTheFingerprint()
    {
        var squads = new[] { TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90) };
        var plain = new ScenarioDef("s", "", TestDefs.Map, squads);
        var withColumn = new ScenarioDef("s", "", TestDefs.Map, squads, formations: [TestDefs.Line, TestDefs.Column]);
        Assert.Multiple(() =>
        {
            Assert.That(plain.Formations, Is.EqualTo(new[] { TestDefs.Line }));
            Assert.That(withColumn.FindFormation("test-column"), Is.SameAs(TestDefs.Column));
            Assert.That(withColumn.Fingerprint, Is.Not.EqualTo(plain.Fingerprint));
            Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map, squads, formations: [TestDefs.Column]),
                "a spawn's formation must be in the catalogue");
            Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map, squads, [ScriptedOrderDef.Formation(2, "Line", "nope")]));
        });
    }

    [Test]
    public void ColumnFacingAbout_NobodyWalksFar_NoTailSwing()
    {
        // A column of 8 (2 × 4, 4.5 m deep) ordered straight back: it faces about; nobody walks more than a step.
        var scenario = new ScenarioDef("s", "", TestDefs.Map, [TestDefs.Squad("C", 0, 8, FixVec2.FromInts(30, 20), 90, TestDefs.Column)],
            [new ScriptedOrderDef(1, "C", FixVec2.FromInts(30, 5), null)]);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        var start = sim.State.Fighters.ToDictionary(f => f.Id, f => f.Position);
        sim.StepTicks(3);
        double farthest = sim.State.Fighters.Max(f => (SlotOf(sim.State, f) - start[f.Id]).Length.Raw / 65536.0);
        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Squads[0].Facing, Is.EqualTo(Angle.FromDegrees(270)));
            Assert.That(farthest, Is.LessThan(0.3), "each man's new place is about where he stands");
        });
    }

    [Test]
    public void LineWheeling90Degrees_InnerFlankMarksTime_OuterFlankSwings()
    {
        // Ordered 90° to the left of its front, far away: it wheels round its left end.
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(new ScriptedOrderDef(1, "Line", FixVec2.FromInts(2, 10), null)), 1));
        sim.Step();
        var squad = Line(sim);
        var leftEnd = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 0)));
        var rightEnd = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 3)));
        sim.StepTicks(20);
        var leftNow = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 0)));
        var rightNow = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 3)));
        Assert.Multiple(() =>
        {
            Assert.That((leftNow - leftEnd).Length.Raw / 65536.0, Is.LessThan(0.01), "the pivot man marks time");
            Assert.That((rightNow - rightEnd).Length.Raw / 65536.0, Is.GreaterThan(1.5), "the outer flank swings round");
            Assert.That(squad.Facing.DeltaTo(Angle.FromDegrees(90)), Is.LessThan(0), "turned left");
        });
    }

    [Test]
    [Category("Determinism")]
    public void Snapshot_WhileReformingAndAfterLosses_ContinuesIdentically()
    {
        // A man falls at tick 30 (combat comes in M3, so the test kills him), in both twins alike.
        void StepTo(BattleSimulation sim, int tick)
        {
            while (sim.State.Tick < tick)
            {
                if (sim.State.Tick == 29)
                {
                    sim.State.FighterList[2].IsAlive = false;
                }

                sim.Step();
            }
        }

        foreach (int at in new[] { 12, 31 })
        {
            var sim = LineWithColumnAvailable(new ScriptedOrderDef(1, "Line", FixVec2.FromInts(20, 34), null), ScriptedOrderDef.Formation(10, "Line", "test-column"));
            StepTo(sim, at);
            Assert.That(at == 12 ? sim.State.Squads[0].Reforming : sim.State.Squads[0].Members.Count == 7, Is.True, "the moment it should be");
            var restored = BattleSimulation.Restore(sim.TakeSnapshot());
            StepTo(sim, 200);
            StepTo(restored, 200);
            Assert.That(restored.ComputeHash(), Is.EqualTo(sim.ComputeHash()), $"snapshot at tick {at}");
        }
    }

    [Test]
    public void Loader_FormationOrder_AndTheWholeCatalogue()
    {
        var defs = DefLibrary.Parse([new SourceText("f.json", """
            { "fighters": [ { "id": "w", "radius": 0.3, "walkSpeed": 1.4, "runSpeed": 3.5, "turnRate": 360 } ],
              "formations": [
                { "id": "line", "spacing": 1, "rankSpacing": 1.5, "ranks": 2, "turnRate": 45, "holdWeight": 3, "allyPassSpeed": 0.5 },
                { "id": "column", "spacing": 1, "rankSpacing": 1.5, "width": 2, "turnRate": 90, "holdWeight": 3, "allyPassSpeed": 0.5 } ] }
            """)]);
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", """
            { "id": "s", "map": { "width": 60, "height": 40 },
              "squads": [ { "name": "A", "side": 0, "fighter": "w", "count": 4, "formation": "line", "position": [20, 10], "facing": 90 } ],
              "script": [ { "tick": 5, "order": "formation", "squad": "A", "formation": "column" } ] }
            """), defs);
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Formations.Select(f => f.Id), Is.EqualTo(new[] { "column", "line" }));
            Assert.That(scenario.Script[0].FormationId, Is.EqualTo("column"));
        });
    }

    private static BattleSimulation One(SquadSpawnDef squad, MapDef? map, params ScriptedOrderDef[] script) =>
        new(BattleSetup.Create(new ScenarioDef("s", "", map ?? TestDefs.Map, [squad], script, formations: [TestDefs.Line, TestDefs.Column]), 1));

    private static int RunOrder(BattleSimulation sim, out int maxSpinTicks, int limit = 1500)
    {
        maxSpinTicks = 0;
        var squad = sim.State.Squads[0];
        int ticks = 0;
        while (ticks < limit && (ticks < 2 || squad.Order.Kind == SquadOrderKind.Move))
        {
            sim.Step();
            ticks++;
            maxSpinTicks += squad.Spin != 0 ? 1 : 0;
        }

        return ticks;
    }

    [TestCase(8, "line")]
    [TestCase(8, "column")]
    public void ArrivingToFaceTheOtherWay_TheFrontRankLandsOnTheOrderedPoint(int count, string formation)
    {
        // Marching north to (30, 30), ordered to face south (decided 2026-11-21): the route runs a depth past the point,
        // the squad faces about there, and its new front rank stands on (30, 30).
        var def = formation == "line" ? TestDefs.Line : TestDefs.Column;
        var sim = One(TestDefs.Squad("A", 0, count, FixVec2.FromInts(30, 10), 90, def), null,
            new ScriptedOrderDef(1, "A", FixVec2.FromInts(30, 30), Angle.FromDegrees(270)));
        RunOrder(sim, out int turning);
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.Anchor, Is.EqualTo(FixVec2.FromInts(30, 30)));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(270)));
            Assert.That(turning, Is.LessThan(20), "no half-turn pivot: a face-about and a small correction at most");
        });
    }

    [Test]
    public void TheSameOrderAgain_ChangesNothing_NoTailSwing()
    {
        var sim = One(TestDefs.Squad("A", 0, 8, FixVec2.FromInts(30, 10), 90, TestDefs.Column), null,
            new ScriptedOrderDef(1, "A", FixVec2.FromInts(30, 30), Angle.FromDegrees(270)));
        RunOrder(sim, out _);
        var squad = sim.State.Squads[0];
        sim.Enqueue(new MoveSquadCommand(0, squad.Id, FixVec2.FromInts(30, 30), Angle.FromDegrees(270)));
        RunOrder(sim, out int turning);
        Assert.Multiple(() =>
        {
            Assert.That(squad.Anchor, Is.EqualTo(FixVec2.FromInts(30, 30)));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(270)));
            Assert.That(turning, Is.Zero);
        });
    }

    [Test]
    public void TargetBehindCloserThanTheDepth_FacesAboutAndStepsBack()
    {
        // A column 4.5 m deep, ordered 2 m straight back: it faces about (the anchor lands 2.5 m past the point) and steps
        // back onto it, facing the new way. No pivot.
        var sim = One(TestDefs.Squad("A", 0, 8, FixVec2.FromInts(30, 20), 90, TestDefs.Column), null,
            new ScriptedOrderDef(1, "A", FixVec2.FromInts(30, 18), null));
        RunOrder(sim, out int turning);
        var squad = sim.State.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(squad.Anchor, Is.EqualTo(FixVec2.FromInts(30, 18)));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(270)));
            Assert.That(turning, Is.Zero);
        });
    }

    [Test]
    [Category("Scenario")]
    public void WheelOrFaceAboutNearAWall_NeverCarriesTheAnchorIntoIt()
    {
        // Reviewer's repro (2026-11-21): a 111° corner beside a wall; the face-about and wheel used to put the anchor in
        // the wall and hold a flank behind it, 8.8 m from his slot.
        var map = new MapDef(Fix.FromInt(60), Fix.FromInt(40), [new WallDef(new FixVec2(Fix.FromInt(26), Fix.Ratio(216, 10)), FixVec2.FromInts(45, 23))]);
        var sim = One(TestDefs.Squad("A", 0, 8, FixVec2.FromInts(30, 20), 90), map, new ScriptedOrderDef(1, "A", FixVec2.FromInts(25, 30), null));
        var squad = sim.State.Squads[0];
        bool anchorInWall = false;
        for (int i = 0; i < 1500 && (i < 2 || squad.Order.Kind == SquadOrderKind.Move); i++)
        {
            sim.Step();
            anchorInWall |= Terrain.Clearance(map, squad.Anchor) == Fix.Zero;
        }

        double worst = sim.State.Fighters.Max(f => (SlotOf(sim.State, f) - f.Position).Length.Raw / 65536.0);
        Assert.Multiple(() =>
        {
            Assert.That(anchorInWall, Is.False);
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(worst, Is.LessThan(0.5));
        });
    }

    [Test]
    public void LineWheelingRight_TheRightEndMarksTime()
    {
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(new ScriptedOrderDef(1, "Line", FixVec2.FromInts(40, 10), null)), 1));
        sim.Step();
        var squad = Line(sim);
        var right = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 3)));
        var left = SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 0)));
        sim.StepTicks(20);
        Assert.Multiple(() =>
        {
            Assert.That((SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 3))) - right).Length.Raw / 65536.0, Is.LessThan(0.01));
            Assert.That((SlotTracking.Position(squad, SlotTracking.Offset(squad, InSlot(sim.State, 0))) - left).Length.Raw / 65536.0, Is.GreaterThan(1.5));
            Assert.That(squad.Facing.DeltaTo(Angle.FromDegrees(90)), Is.GreaterThan(0), "turned right");
        });
    }

    [Test]
    public void CloseRanks_RandomLosses_SlotsStayDense_TheFrontKeepsItsWidth()
    {
        var formations = new[]
        {
            TestDefs.Line, TestDefs.Column,
            new FormationDef("r3", Fix.One, Fix.Ratio(3, 2), ranks: 3, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3)),
        };
        var random = new Random(7);
        var bad = new List<string>();
        foreach (var formation in formations)
        {
            for (int count = 1; count <= 16; count++)
            {
                for (int trial = 0; trial < 6; trial++)
                {
                    var map = new MapDef(Fix.FromInt(60), Fix.FromInt(40));
                    var state = BattleSetup.Create(new ScenarioDef("s", "", map,
                        [new SquadSpawnDef("A", 0, TestDefs.Warrior, count, formation, FixVec2.FromInts(30, 30), Angle.FromDegrees(90))]), 1);
                    var squad = state.SquadList[0];
                    int frontage = squad.Frontage;
                    var frontSurvivors = new List<EntityId>();
                    int kills = random.Next(0, count + 1);
                    foreach (var f in state.Fighters.OrderBy(_ => random.Next()).Take(kills))
                    {
                        f.IsAlive = false;
                    }

                    foreach (var f in state.Fighters.Where(f => f.IsAlive && f.SlotIndex < frontage))
                    {
                        frontSurvivors.Add(f.Id);
                    }

                    SlotAssignment.CloseRanks(state, squad);
                    var living = state.Fighters.Where(f => f.IsAlive).ToArray();
                    int n = living.Length;
                    string what = $"{formation.Id} {count} men, {kills} fall";
                    if (!living.Select(f => f.SlotIndex).Order().SequenceEqual(Enumerable.Range(0, n)))
                    {
                        bad.Add(what + ": slots not 0..n-1");
                    }

                    if (living.Count(f => f.SlotIndex < Math.Min(frontage, n)) != Math.Min(frontage, n))
                    {
                        bad.Add(what + ": front not full");
                    }

                    if (frontSurvivors.Any(id => state.FindFighter(id)!.SlotIndex >= frontage))
                    {
                        bad.Add(what + ": a front-rank man was sent back");
                    }

                    if (squad.Members.Count != n || state.Fighters.Any(f => !f.IsAlive && f.SquadId != EntityId.None))
                    {
                        bad.Add(what + ": membership");
                    }
                }
            }
        }

        Assert.That(bad, Is.Empty);
    }

    [Test]
    public void SetFormation_WhileWaitingForARoute_OrWhileRegrouping_TheOrderStillCompletes()
    {
        // A 1 m wall across the map with a 2 m gap: the line must regroup for it.
        var map = new MapDef(Fix.FromInt(80), Fix.FromInt(40),
            [new WallDef(FixVec2.FromInts(0, 19), FixVec2.FromInts(29, 20)), new WallDef(FixVec2.FromInts(31, 19), FixVec2.FromInts(80, 20))]);
        var waiting = One(TestDefs.Squad("A", 0, 8, FixVec2.FromInts(30, 10), 90), map,
            new ScriptedOrderDef(1, "A", FixVec2.FromInts(30, 30), null), ScriptedOrderDef.Formation(1, "A", "test-column"));
        var regrouping = One(TestDefs.Squad("A", 0, 8, FixVec2.FromInts(30, 10), 90), map, new ScriptedOrderDef(1, "A", FixVec2.FromInts(30, 30), null));
        var squad = regrouping.State.Squads[0];
        for (int i = 0; i < 400 && squad.RegroupSince < 0; i++)
        {
            regrouping.Step();
        }

        Assert.That(squad.RegroupSince, Is.GreaterThanOrEqualTo(0), "precondition: regrouping");
        regrouping.Enqueue(new SetFormationCommand(0, squad.Id, "test-column"));
        RunOrder(waiting, out _);
        RunOrder(regrouping, out _);
        Assert.Multiple(() =>
        {
            Assert.That(waiting.State.Squads[0].Order, Is.EqualTo(SquadOrder.None));
            Assert.That(waiting.State.Squads[0].Anchor, Is.EqualTo(FixVec2.FromInts(30, 30)));
            Assert.That(squad.Order, Is.EqualTo(SquadOrder.None));
            Assert.That(squad.Anchor, Is.EqualTo(FixVec2.FromInts(30, 30)));
            Assert.That(squad.Formation, Is.SameAs(TestDefs.Column));
        });
    }
}
