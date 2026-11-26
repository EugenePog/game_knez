using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Morale;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Retainers;

/// <summary>
/// M3 step 10 (battle-sim.md §13): Retainers — named fighters who start in a squad's front rank or alone, join and leave
/// squads, take their own orders when alone, and lead (decided by the user 2026-11-26: leadership 100 / 150 / 200 ‰ cuts
/// losses and speeds recovery; his fall costs as one mate, with a reason of its own; he counts in the break share; he can rout, and alone he
/// rallies where he stopped).
/// </summary>
[TestFixture]
public class RetainerTests
{
    private static readonly TraitDef Loyal = TraitDef.StandardOf("loyal");
    private static readonly KitDef Mail = TestDefs.KitOf("sword-shield-mail");

    private static RetainerSpawnDef InSquad(string squad = "A", string name = "Hrafn", string level = "elite", int side = 0) =>
        RetainerSpawnDef.InSquad(name, side, Loyal, TestDefs.Man, TestDefs.Level(level), Mail, squad);

    private static RetainerSpawnDef Alone(int x, int y, string name = "Ulfr", int side = 0, int facing = 90) =>
        RetainerSpawnDef.Alone(name, side, Loyal, TestDefs.Man, TestDefs.Level("elite"), Mail, FixVec2.FromInts(x, y), Angle.FromDegrees(facing));

    /// <summary>A (side 0, 4 men) and B (side 0, 4 men) facing north, E (side 1, 4 men) far to the north facing south.</summary>
    private static BattleState Battle(int aCount = 4, CombatDef? combat = null, params RetainerSpawnDef[] retainers) =>
        BattleSetup.Create(new ScenarioDef("test-retainers", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
        [
            TestDefs.Squad("A", 0, aCount, FixVec2.FromInts(20, 15), 90),
            TestDefs.Squad("B", 0, 4, FixVec2.FromInts(40, 15), 90),
            TestDefs.Squad("E", 1, 4, FixVec2.FromInts(20, 50), 270),
        ], combat: combat, retainers: retainers), 1);

    private static Squad SquadOf(BattleState state, string name) => state.Squads.Single(s => s.Name == name);

    private static Fighter RetainerOf(BattleState state, string name) =>
        state.Fighters.Single(f => f.Retainer.HasValue && f.Retainer.Value.Name == name);

    private static void RunMorale(BattleState state) => new MoraleSystem().Execute(state, new TickContext(state, new CommandQueue()));

    private static string? Reason(BattleState state, ICommand command) => command.Validate(state).Code;

    private static void Apply(BattleState state, ICommand command)
    {
        Assert.That(command.Validate(state).IsRejected, Is.False, command.Validate(state).ToString());
        command.Apply(state, new TickContext(state, new CommandQueue()));
    }

    private static FixVec2 SlotPosition(Squad squad, Fighter man)
    {
        int count = squad.Members.Count;
        int width = FormationLayout.Width(squad.Formation, squad.Frontage, count);
        return FormationLayout.ToWorld(squad.Anchor, squad.Facing, FormationLayout.SlotOffsetAt(squad.Formation, width, count, man.SlotIndex, squad.Files));
    }

    [Test]
    public void Spawn_InASquad_FrontRankNearestTheAnchor_FormedUp_CountsAsOneOfItsMen()
    {
        var state = Battle(retainers: InSquad());
        var a = SquadOf(state, "A");
        var hrafn = RetainerOf(state, "Hrafn");
        int width = FormationLayout.Width(a.Formation, a.Frontage, 5);
        Assert.Multiple(() =>
        {
            Assert.That(hrafn.Id.Value, Is.GreaterThan(state.Fighters.Where(f => !f.IsRetainer).Max(f => f.Id.Value)), "ids after every squad's men");
            Assert.That(hrafn.SquadId, Is.EqualTo(a.Id));
            Assert.That(a.Members, Has.Count.EqualTo(5));
            Assert.That(a.Members, Is.Ordered.Using<EntityId>((x, y) => x.Value.CompareTo(y.Value)), "members stay sorted by id");
            Assert.That(a.StartCount, Is.EqualTo(5), "he counts against the squad's losses");
            Assert.That(a.Frontage, Is.EqualTo(FormationLayout.Width(a.Formation, 5)), "laid out for one more man");
            Assert.That(hrafn.SlotIndex, Is.LessThan(width), "front rank");
            Assert.That(hrafn.Position, Is.EqualTo(SlotPosition(a, hrafn)));
            Assert.That((hrafn.Position - a.Anchor).Length,
                Is.LessThanOrEqualTo(Enumerable.Range(0, width).Select(s => (FormationLayout.ToWorld(a.Anchor, a.Facing,
                    FormationLayout.SlotOffsetAt(a.Formation, width, 5, s, 0)) - a.Anchor).Length).Min()), "the front slot nearest the anchor");
            Assert.That(a.Members.Select(id => state.FindFighter(id)!).All(m => m.Position == SlotPosition(a, m)), Is.True, "the squad stands formed");
            Assert.That(a.Members.Select(id => state.FindFighter(id)!.SlotIndex).Distinct().Count(), Is.EqualTo(5));
            Assert.That(hrafn.Morale, Is.EqualTo(950), "an elite starts at 950");
        });
    }

    [Test]
    public void Spawn_Alone_StandsWhereTheScenarioPutsHim()
    {
        var state = Battle(retainers: Alone(30, 30, facing: 180));
        var ulfr = RetainerOf(state, "Ulfr");
        Assert.Multiple(() =>
        {
            Assert.That(ulfr.SquadId.IsNone, Is.True);
            Assert.That(ulfr.SlotIndex, Is.EqualTo(Fighter.NoSlot));
            Assert.That(ulfr.Position, Is.EqualTo(FixVec2.FromInts(30, 30)));
            Assert.That(ulfr.Facing, Is.EqualTo(Angle.FromDegrees(180)));
            Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None));
        });
    }

    [Test]
    public void Attach_FromAlone_JoinsTheFrontRankNearestHim_LayoutForOneMore()
    {
        // He stands south-east of A, so its front-rank slot nearest him is its right end.
        var state = Battle(retainers: Alone(23, 12));
        var a = SquadOf(state, "A");
        var ulfr = RetainerOf(state, "Ulfr");
        foreach (var id in a.Members)
        {
            state.FindFighter(id)!.TurnLag = new FixVec2(Fix.One, Fix.Zero);
        }

        Apply(state, new AttachRetainerCommand(0, ulfr.Id, a.Id));
        int width = FormationLayout.Width(a.Formation, a.Frontage, 5);
        var frontSlots = Enumerable.Range(0, width).ToArray();
        int nearest = frontSlots.OrderBy(s => (FormationLayout.ToWorld(a.Anchor, a.Facing, FormationLayout.SlotOffsetAt(a.Formation, width, 5, s, 0))
            - ulfr.Position).Length.Raw).ThenBy(s => s).First();
        Assert.Multiple(() =>
        {
            Assert.That(ulfr.SquadId, Is.EqualTo(a.Id));
            Assert.That(ulfr.SlotIndex, Is.EqualTo(nearest));
            Assert.That(a.StartCount, Is.EqualTo(5));
            Assert.That(a.Frontage, Is.EqualTo(3), "a line of 5 is 3 wide");
            Assert.That(a.Members.Select(id => state.FindFighter(id)!.SlotIndex).OrderBy(x => x), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(a.Members.Select(id => state.FindFighter(id)!.TurnLag), Has.All.EqualTo(FixVec2.Zero), "turn lag resets");
        });
    }

    [Test]
    public void Attach_FromAnotherSquad_MovesAcross_Detach_StandsAlone()
    {
        var state = Battle(retainers: InSquad());
        var a = SquadOf(state, "A");
        var b = SquadOf(state, "B");
        var hrafn = RetainerOf(state, "Hrafn");
        Apply(state, new AttachRetainerCommand(0, hrafn.Id, b.Id));
        Assert.Multiple(() =>
        {
            Assert.That(hrafn.SquadId, Is.EqualTo(b.Id));
            Assert.That((a.StartCount, a.Members.Count, b.StartCount, b.Members.Count), Is.EqualTo((4, 4, 5, 5)));
            Assert.That(a.Members.Select(id => state.FindFighter(id)!.SlotIndex).OrderBy(x => x), Is.EqualTo(new[] { 0, 1, 2, 3 }), "A laid out for one fewer");
        });

        Apply(state, new DetachRetainerCommand(0, hrafn.Id));
        Assert.Multiple(() =>
        {
            Assert.That(hrafn.SquadId.IsNone, Is.True);
            Assert.That(hrafn.SlotIndex, Is.EqualTo(Fighter.NoSlot));
            Assert.That((b.StartCount, b.Members.Count), Is.EqualTo((4, 4)));
            Assert.That(b.Members.Select(id => state.FindFighter(id)!.SlotIndex).OrderBy(x => x), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        });
    }

    [Test]
    public void Commands_RefusedWithTheContractsReasons()
    {
        var state = Battle(retainers: [InSquad(), Alone(30, 30)]);
        var a = SquadOf(state, "A");
        var e = SquadOf(state, "E");
        var hrafn = RetainerOf(state, "Hrafn");
        var ulfr = RetainerOf(state, "Ulfr");
        var plainMan = state.FindFighter(a.Members[0])!;
        var enemy = state.FindFighter(e.Members[0])!;
        Assert.Multiple(() =>
        {
            Assert.That(Reason(state, new AttachRetainerCommand(0, plainMan.Id, a.Id)), Is.EqualTo("unknown-entity"), "not a Retainer");
            Assert.That(Reason(state, new AttachRetainerCommand(1, ulfr.Id, a.Id)), Is.EqualTo("wrong-side"));
            Assert.That(Reason(state, new AttachRetainerCommand(0, ulfr.Id, e.Id)), Is.EqualTo("wrong-side"), "the enemy's squad");
            Assert.That(Reason(state, new AttachRetainerCommand(0, ulfr.Id, new EntityId(9999))), Is.EqualTo("unknown-entity"));
            Assert.That(Reason(state, new AttachRetainerCommand(0, hrafn.Id, a.Id)), Is.EqualTo("in-squad"), "already in it");
            Assert.That(Reason(state, new DetachRetainerCommand(0, ulfr.Id)), Is.EqualTo("not-in-squad"));
            Assert.That(Reason(state, new MoveRetainerCommand(0, hrafn.Id, FixVec2.FromInts(10, 10))), Is.EqualTo("in-squad"));
            Assert.That(Reason(state, new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(500, 10))), Is.EqualTo("off-map"));
            Assert.That(Reason(state, new RetainerAttackCommand(0, hrafn.Id, enemy.Id)), Is.EqualTo("in-squad"));
            Assert.That(Reason(state, new RetainerAttackCommand(0, ulfr.Id, plainMan.Id)), Is.EqualTo("not-an-enemy"));
            Assert.That(Reason(state, new RetainerAttackCommand(0, ulfr.Id, new EntityId(9999))), Is.EqualTo("unknown-entity"));
            Assert.That(Reason(state, new RetainerAttackCommand(0, ulfr.Id, enemy.Id)), Is.Null, "valid");
        });

        enemy.IsAlive = false;
        enemy.Wound = WoundLevel.Down;
        a.Broken = true;
        ulfr.MoraleState = MoraleState.Routing;
        Assert.Multiple(() =>
        {
            Assert.That(Reason(state, new RetainerAttackCommand(0, hrafn.Id, enemy.Id)), Is.EqualTo("in-squad"));
            Assert.That(Reason(state, new DetachRetainerCommand(0, ulfr.Id)), Is.EqualTo("routing"));
            Assert.That(Reason(state, new AttachRetainerCommand(0, ulfr.Id, a.Id)), Is.EqualTo("routing"));
        });

        ulfr.MoraleState = MoraleState.Steady;
        Assert.Multiple(() =>
        {
            Assert.That(Reason(state, new RetainerAttackCommand(0, ulfr.Id, enemy.Id)), Is.EqualTo("not-alive"), "the enemy is down");
            Assert.That(Reason(state, new AttachRetainerCommand(0, ulfr.Id, a.Id)), Is.EqualTo("squad-broken"));
        });

        ulfr.IsAlive = false;
        Assert.That(Reason(state, new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(10, 10))), Is.EqualTo("not-alive"));
    }

    [Test]
    public void Attach_ToAFullSquad_IsRefused()
    {
        var state = Battle(retainers: Alone(30, 30));
        var b = SquadOf(state, "B");
        var group = TestDefs.Men(1)[0];
        while (b.MemberList.Count < SquadSpawnDef.MaxCount)
        {
            state.AddSquadMember(b, TestDefs.Man, group, FixVec2.FromInts(60, 40), b.MemberList.Count);
        }

        Assert.That(Reason(state, new AttachRetainerCommand(0, RetainerOf(state, "Ulfr").Id, b.Id)), Is.EqualTo("squad-full"));
    }

    [Test]
    public void MoveRetainer_Alone_WalksThere_FacesTheOrderedWay_ThenTheOrderEnds()
    {
        var sim = new BattleSimulation(Battle(retainers: Alone(30, 30)));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        sim.Enqueue(new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(36, 30), Angle.FromDegrees(0)));
        sim.StepTicks(2);
        Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.Move));
        Assert.That(ulfr.Velocity.Length.Raw, Is.EqualTo(ulfr.Body.WalkPerTick.Raw).Within(2), "at walking pace");
        sim.StepTicks(6 * BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That((ulfr.Position - FixVec2.FromInts(36, 30)).Length, Is.LessThanOrEqualTo(MovementRules.ArriveDistance));
            Assert.That(ulfr.Facing, Is.EqualTo(Angle.FromDegrees(0)));
            Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None), "done");
        });
    }

    [Test]
    public void MoveRetainer_OrderedFacingOtherThanTheMarch_TurnsAllTheWay_ThenTheOrderEnds()
    {
        // Found in review: the order used to end within a tick's turn of the facing, and nothing turned him the rest.
        var sim = new BattleSimulation(Battle(retainers: Alone(30, 30, facing: 0)));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        sim.Enqueue(new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(34, 30), Angle.FromDegrees(45)));
        sim.StepTicks(5 * BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That((ulfr.Position - FixVec2.FromInts(34, 30)).Length, Is.LessThanOrEqualTo(MovementRules.ArriveDistance));
            Assert.That(ulfr.Facing, Is.EqualTo(Angle.FromDegrees(45)), "exactly the ordered facing");
            Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None), "done");
        });
    }

    [Test]
    public void IdleRetainerAlone_NudgedByAMan_KeepsHisFacing()
    {
        var state = Battle(retainers: Alone(30, 30, facing: 180));
        var ulfr = RetainerOf(state, "Ulfr");
        var mate = state.FindFighter(SquadOf(state, "A").Members[0])!;
        ulfr.Position = mate.Position + new FixVec2(Fix.Zero, -Fix.Ratio(1, 2)); // just behind him: closer than personal space
        FixVec2 start = ulfr.Position;
        var sim = new BattleSimulation(state);
        sim.StepTicks(10);
        Assert.Multiple(() =>
        {
            Assert.That(ulfr.Position, Is.Not.EqualTo(start), "the nudge happened");
            Assert.That(ulfr.Facing, Is.EqualTo(Angle.FromDegrees(180)), "a nudge doesn't turn a man standing alone");
        });
    }

    [Test]
    public void RetainerAttack_Wounded_ApproachesAtHisWoundedPace()
    {
        var sim = new BattleSimulation(Battle(retainers: Alone(20, 40)));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        ulfr.Wound = WoundLevel.Heavy;
        sim.Enqueue(new RetainerAttackCommand(0, ulfr.Id, sim.State.FindFighter(SquadOf(sim.State, "E").Members[0])!.Id));
        sim.StepTicks(3);
        Fix pace = ulfr.Body.WalkPerTick * Wounds.Pace(sim.State.Scenario.Combat, WoundLevel.Heavy);
        Assert.That(ulfr.Velocity.Length.Raw, Is.EqualTo(pace.Raw).Within(2), "as slow as a wounded man's move");
    }

    [Test]
    public void LoneRetainer_Falls_HisOrderEnds()
    {
        var sim = new BattleSimulation(Battle(retainers: Alone(30, 30)));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        sim.Enqueue(new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(40, 30), null));
        sim.Step();
        ulfr.IsAlive = false;
        ulfr.Wound = WoundLevel.Down;
        sim.Step();
        Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None));
    }

    [Test]
    public void Victory_ASideOfRetainersAlone_IsASide()
    {
        var state = BattleSetup.Create(new ScenarioDef("test-alone-side", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
            [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90)], retainers: [Alone(20, 45, side: 1, facing: 270)]), 1);
        var sim = new BattleSimulation(state);
        sim.Step();
        Assert.That(state.Winner, Is.EqualTo(BattleState.Undecided), "two sides in the fight: undecided");
        var ulfr = RetainerOf(state, "Ulfr");
        ulfr.IsAlive = false;
        ulfr.Wound = WoundLevel.Down;
        sim.Step();
        Assert.That(state.Winner, Is.EqualTo(0), "his side is out: side 0 holds the field");
    }

    [Test]
    public void RetainerAttack_GoesAtTheEnemy_Engages_TheOrderEndsWhenHeIsDown()
    {
        // Harmless rules: they lock and swing, nobody is hurt; the test fells the enemy itself.
        var sim = new BattleSimulation(Battle(combat: TestDefs.Harmless, retainers: Alone(20, 40)));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        var target = sim.State.FindFighter(SquadOf(sim.State, "E").Members[0])!;
        sim.Enqueue(new RetainerAttackCommand(0, ulfr.Id, target.Id));
        bool engaged = false;
        for (int t = 0; t < 20 * BattleState.TicksPerSecond && !engaged; t++)
        {
            sim.Step();
            engaged = ulfr.IsEngaged;
        }

        Assert.That(engaged, Is.True, "he reached the enemy line and fights");
        Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.Attack));
        target.IsAlive = false;
        target.Wound = WoundLevel.Down;
        sim.StepTicks(2);
        Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None));
    }

    [Test]
    public void Leadership_CutsEveryLossOfHisSquad_AndHisOwn_NotOtherSquads()
    {
        // A: 4 men + an elite Retainer (200 ‰): a fallen mate costs LossCost(5) = 400, cut to 320. B (no Retainer) as usual.
        var state = Battle(retainers: InSquad());
        var a = SquadOf(state, "A");
        var b = SquadOf(state, "B");
        var enemy = state.FindFighter(SquadOf(state, "E").Members[0])!;
        var fallenA = state.FindFighter(a.Members[0])!;
        var fallenB = state.FindFighter(b.Members[0])!;
        foreach (var man in new[] { fallenA, fallenB })
        {
            man.IsAlive = false;
            man.Wound = WoundLevel.Down;
            state.AddEvent(new Wounded(state.Tick, man.Id, enemy.Id, WoundLevel.Down, WoundLevel.Down));
        }

        RunMorale(state);
        var hrafn = RetainerOf(state, "Hrafn");
        var restA = a.Members.Select(id => state.FindFighter(id)!).Where(m => m.IsAlive && !m.IsRetainer).ToArray();
        var restB = b.Members.Select(id => state.FindFighter(id)!).Where(m => m.IsAlive).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(MoraleRules.Leadership(state, restA[0]), Is.EqualTo(200));
            Assert.That(restA.Select(m => m.Morale), Has.All.EqualTo(750 - 320), "400 × 800 / 1000");
            Assert.That(hrafn.Morale, Is.EqualTo(950 - 320), "his own loss is cut too");
            Assert.That(restB.Select(m => m.Morale), Has.All.EqualTo(750 - 500), "B has no Retainer: LossCost(4)");
            Assert.That(state.Events.OfType<MoraleChanged>().Where(c => restA.Any(m => m.Id == c.Fighter)).Select(c => c.Delta),
                Has.All.EqualTo(-320), "the event carries the cut loss");
        });
    }

    [Test]
    public void Leadership_SpeedsRecovery_ByTheSameShare()
    {
        // Out of everyone's reach (the enemy is 35 m away): an elite-led man recovers 24 a second, the others 20.
        var state = Battle(retainers: InSquad());
        var led = state.FindFighter(SquadOf(state, "A").Members.First(id => !state.FindFighter(id)!.IsRetainer))!;
        var plain = state.FindFighter(SquadOf(state, "B").Members[0])!;
        led.Morale = 500;
        plain.Morale = 500;
        var sim = new BattleSimulation(state);
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(led.Morale, Is.EqualTo(524), "20 × 1200 / 1000");
            Assert.That(plain.Morale, Is.EqualTo(520));
        });
    }

    [Test]
    public void LeaderFalls_CostsHisSquadAMate_WithAReasonOfItsOwn()
    {
        var state = Battle(retainers: InSquad());
        var a = SquadOf(state, "A");
        var hrafn = RetainerOf(state, "Hrafn");
        var enemy = state.FindFighter(SquadOf(state, "E").Members[0])!;
        hrafn.IsAlive = false;
        hrafn.Wound = WoundLevel.Down;
        state.AddEvent(new Wounded(state.Tick, hrafn.Id, enemy.Id, WoundLevel.Down, WoundLevel.Down));
        RunMorale(state);
        var men = a.Members.Select(id => state.FindFighter(id)!).Where(m => m.IsAlive).ToArray();
        var changes = state.Events.OfType<MoraleChanged>().Where(c => men.Any(m => m.Id == c.Fighter)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(MoraleRules.LeaderCost(state.Scenario.Morale, 5), Is.EqualTo(400), "1 × LossCost(5) (decided 2026-11-26)");
            Assert.That(changes.Select(c => c.Reason), Has.All.EqualTo(MoraleReason.LeaderFell), "instead of a mate's fall, not besides");
            Assert.That(changes.Select(c => c.Delta), Has.All.EqualTo(-400), "no leadership left to cut it: he was the one leading");
            Assert.That(changes, Has.Length.EqualTo(4));
            Assert.That(men.Select(m => m.Morale), Has.All.EqualTo(350));
            Assert.That(a.Broken, Is.False, "losing its leader shakes a fresh squad of 5; it doesn't break it at once");
        });
    }

    [Test]
    public void RallyingInASquad_TakenOutOfIt_RalliesWhereHeStands()
    {
        // Found in review: going back to a place he no longer has, he stayed rallying for good.
        var state = Battle(retainers: InSquad());
        var hrafn = RetainerOf(state, "Hrafn");
        hrafn.MoraleState = MoraleState.Rallying;
        hrafn.Morale = 500;
        Apply(state, new DetachRetainerCommand(0, hrafn.Id));
        RunMorale(state);
        Assert.That(hrafn.MoraleState, Is.EqualTo(MoraleState.Shaken), "500 is shaken (400, + 50 to climb)");
    }

    [Test]
    public void BreakShare_TheRetainerCountsAsOneOfItsMen()
    {
        // A: 5 men + Hrafn = 6 in the fight. Three running is not more than half of 6 — it would be of 5.
        var state = Battle(aCount: 5, retainers: InSquad());
        var a = SquadOf(state, "A");
        var men = a.Members.Select(id => state.FindFighter(id)!).Where(m => !m.IsRetainer).Take(3).ToArray();
        foreach (var m in men)
        {
            m.Morale = 100;
        }

        RunMorale(state);
        Assert.Multiple(() =>
        {
            Assert.That(men.All(m => m.IsRouting), Is.True);
            Assert.That(a.Broken, Is.False, "3 of 6");
        });
    }

    [Test]
    public void LoneRetainer_Routs_DropsHisOrder_ThenRalliesWhereHeStopped()
    {
        var state = Battle(retainers: Alone(30, 30));
        var ulfr = RetainerOf(state, "Ulfr");
        ulfr.LoneOrder = LoneOrder.Move(FixVec2.FromInts(31, 30), null);
        ulfr.Morale = 100;
        RunMorale(state);
        Assert.Multiple(() =>
        {
            Assert.That(ulfr.MoraleState, Is.EqualTo(MoraleState.Routing));
            Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.None), "a running man keeps no order");
        });

        ulfr.Morale = 400; // safe (the enemy is 20 m off) and past rallyAt
        RunMorale(state);
        Assert.Multiple(() =>
        {
            Assert.That(ulfr.MoraleState, Is.EqualTo(MoraleState.Wavering), "rallied where he stopped: 400 is wavering");
            Assert.That(ulfr.SquadId.IsNone, Is.True);
        });
    }

    [Test]
    public void Script_AttachDetachMoveAttack_RunAsTheirCommands()
    {
        var state = BattleSetup.Create(new ScenarioDef("test-script", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
            [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90), TestDefs.Squad("E", 1, 4, FixVec2.FromInts(20, 50), 270)],
            [
                ScriptedOrderDef.Attach(1, "Ulfr", "A"),
                ScriptedOrderDef.Detach(3, "Ulfr"),
                ScriptedOrderDef.MoveRetainer(4, "Ulfr", FixVec2.FromInts(40, 30), Angle.FromDegrees(0)),
                ScriptedOrderDef.Attack(6, "Ulfr", new EntityId(7)),
            ], retainers: [Alone(30, 30)]), 1);
        var sim = new BattleSimulation(state);
        var ulfr = RetainerOf(state, "Ulfr");
        sim.Step();
        Assert.That(ulfr.SquadId, Is.EqualTo(SquadOf(state, "A").Id), "tick 1: attached");
        sim.StepTicks(2);
        Assert.That(ulfr.SquadId.IsNone, Is.True, "tick 3: detached");
        sim.Step();
        Assert.That(ulfr.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.Move), "tick 4: moving");
        sim.StepTicks(2);
        Assert.That((ulfr.LoneOrder.Kind, ulfr.LoneOrder.Enemy.Value), Is.EqualTo((LoneOrderKind.Attack, 7)), "tick 6: attacking fighter 7");
    }

    [Test]
    public void Scenario_RetainersChecked()
    {
        var a = TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90);
        ScenarioDef Make(params RetainerSpawnDef[] r) => new("t", "", new MapDef(Fix.FromInt(80), Fix.FromInt(60)), [a], retainers: r);
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ArgumentException>(() => Make(InSquad(), InSquad()))!.Message, Does.Contain("retainers[1]: name 'Hrafn' is already used"));
            Assert.That(Assert.Throws<ArgumentException>(() => Make(InSquad(squad: "Nobody")))!.Message, Does.Contain("no squad is named 'Nobody'"));
            Assert.That(Assert.Throws<ArgumentException>(() => Make(InSquad(side: 1)))!.Message, Does.Contain("squad 'A' is side 0, he is side 1"));
            Assert.That(Assert.Throws<ArgumentException>(() => Make(Alone(500, 10)))!.Message, Does.Contain("is off the"));
            // Four men fit at the map's edge two abreast; with Hrafn joining, the line is three wide and its end is off.
            var atEdge = TestDefs.Squad("A", 0, 4, new FixVec2(Fix.Ratio(4, 5), Fix.FromInt(15)), 90);
            Assert.That(() => new ScenarioDef("t", "", new MapDef(Fix.FromInt(80), Fix.FromInt(60)), [atEdge]), Throws.Nothing);
            Assert.That(Assert.Throws<ArgumentException>(() => new ScenarioDef("t", "", new MapDef(Fix.FromInt(80), Fix.FromInt(60)), [atEdge], retainers: [InSquad()]))!.Message,
                Does.Contain("squads[0] 'A' with its 1 Retainer(s): slot 0 at"));
            var squadOfElite = new SquadSpawnDef("V", 0, TestDefs.Man, TestDefs.Men(4, TestDefs.Level("elite")), TestDefs.Line, FixVec2.FromInts(40, 15), Angle.FromDegrees(90));
            Assert.That(Assert.Throws<ArgumentException>(() => new ScenarioDef("t", "", TestDefs.Map, [squadOfElite]))!.Message,
                Does.Contain("squads[0] 'V' men[0]: experience 'elite' is for Retainers only (D-019)"));
        });
    }

    [Test]
    [Category("Determinism")]
    public void Determinism_SnapshotRestore_WithRetainersMidOrder_ContinuesIdentically()
    {
        var sim = new BattleSimulation(Battle(retainers: [InSquad(), Alone(30, 30)]));
        var ulfr = RetainerOf(sim.State, "Ulfr");
        sim.Enqueue(new MoveRetainerCommand(0, ulfr.Id, FixVec2.FromInts(20, 45), null));
        sim.StepTicks(40);
        var snapshot = sim.State.Clone();
        var twin = new BattleSimulation(snapshot);
        sim.StepTicks(200);
        twin.StepTicks(200);
        Assert.That(twin.ComputeHash().Total, Is.EqualTo(sim.ComputeHash().Total));
    }

    [Test]
    public void Hash_CoversTheRetainerPartAndHisOrder()
    {
        var state = Battle(retainers: Alone(30, 30));
        ulong before = StateHasher.Compute(state).Total;
        var ulfr = RetainerOf(state, "Ulfr");
        ulfr.LoneOrder = LoneOrder.Move(FixVec2.FromInts(31, 30), null);
        ulong moved = StateHasher.Compute(state).Total;
        ulfr.Retainer = new RetainerPart("Ulfr", TraitDef.StandardOf("proud"));
        ulong trait = StateHasher.Compute(state).Total;
        Assert.That(new[] { before, moved, trait }.Distinct().Count(), Is.EqualTo(3));
    }
}
