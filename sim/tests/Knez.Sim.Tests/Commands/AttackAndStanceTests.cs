using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Commands;

/// <summary>
/// M4 step 1 (battle-sim.md §8, §10; decided by the user 2026-11-26): the attack order on an enemy squad (it marches at it,
/// re-aimed once a second, and ends when the target breaks); stances Hold (the default) / Advance (goes at an enemy squad
/// within 15 m) / Pursue (also chases the runners: ranks broken, up to 10 s within 30 m of where it began, then re-forms);
/// runners at 0.9 of their run.
/// </summary>
[TestFixture]
public class AttackAndStanceTests
{
    private static SquadSpawnDef Squad(string name, int side, int count, int x, int y, int facing, SquadStance stance = SquadStance.Hold) =>
        new(name, side, TestDefs.Man, TestDefs.Men(count), TestDefs.Line, FixVec2.FromInts(x, y), Angle.FromDegrees(facing), stance);

    private static BattleSimulation Battle(params SquadSpawnDef[] squads) =>
        new(BattleSetup.Create(new ScenarioDef("test-attack", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)), squads), 1));

    private static Squad SquadOf(BattleSimulation sim, string name) => sim.State.Squads.Single(s => s.Name == name);

    private static Fighter[] MenOf(BattleSimulation sim, string name) =>
        SquadOf(sim, name).Members.Select(id => sim.State.FindFighter(id)!).ToArray();

    private static string? Reason(BattleSimulation sim, ICommand command) => command.Validate(sim.State).Code;

    /// <summary>Breaks <paramref name="squad"/> by hand: its men run (morale 100), as the morale system would have them.</summary>
    private static void Rout(BattleSimulation sim, string squad)
    {
        SquadOf(sim, squad).Broken = true;
        foreach (var man in MenOf(sim, squad))
        {
            man.Morale = 100;
            man.MoraleState = MoraleState.Routing;
        }
    }

    [Test]
    public void Attack_MarchesAtTheTarget_FightsIt_EndsWhenItBreaks_HoldThenStops()
    {
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90), Squad("E", 1, 4, 30, 26, 270));
        var a = SquadOf(sim, "A");
        var e = SquadOf(sim, "E");
        sim.Enqueue(new AttackSquadCommand(0, a.Id, e.Id));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(a.AttackTarget, Is.EqualTo(e.Id));
            Assert.That(a.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "it marches…");
            Assert.That((a.Order.Target - e.Anchor).Length, Is.LessThanOrEqualTo(Fix.One), "…at the target's anchor");
        });

        bool struck = false;
        for (int t = 0; t < 30 * BattleState.TicksPerSecond && !struck; t++)
        {
            sim.Step();
            struck |= sim.State.Events.OfType<BlowStruck>().Any();
        }

        Assert.That(struck, Is.True, "it marched into contact and fought");
        Rout(sim, "E"); // the target breaks (by hand, so no seed decides the test)
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(a.AttackTarget, Is.EqualTo(EntityId.None), "the target broke: the attack is over");
            Assert.That(a.Order.Kind, Is.EqualTo(SquadOrderKind.None), "Hold: it stops where it is");
            Assert.That(a.Pursuing, Is.False, "Hold doesn't chase");
        });
    }

    [Test]
    public void Attack_ReaimsOnceASecond_AsTheTargetMoves()
    {
        var sim = Battle(Squad("A", 0, 4, 10, 10, 90), Squad("E", 1, 4, 60, 40, 270));
        var a = SquadOf(sim, "A");
        var e = SquadOf(sim, "E");
        sim.Enqueue(new AttackSquadCommand(0, a.Id, e.Id));
        sim.Enqueue(new MoveSquadCommand(1, e.Id, FixVec2.FromInts(20, 40)));
        sim.StepTicks(8 * BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.Anchor.X, Is.LessThan(Fix.FromInt(52)), "the target walked west");
            Assert.That((a.Order.Target - e.Anchor).Length, Is.LessThanOrEqualTo(Fix.FromInt(4)), "the march follows it");
            Assert.That(a.AttackTarget, Is.EqualTo(e.Id));
        });
    }

    [TestCase(SquadStance.Advance, 24, true)] // E's front 14 m off, its nearest man ~13.4 m edge to edge: in reach
    [TestCase(SquadStance.Advance, 30, false)] // ~19 m: out of reach
    [TestCase(SquadStance.Hold, 24, false)] // Hold goes nowhere on its own
    [TestCase(SquadStance.Pursue, 24, true)] // Pursue advances too
    public void Stance_AdvanceGoesAtAnEnemySquadWithinFifteenMetres(SquadStance stance, int enemyY, bool attacks)
    {
        var sim = Battle(Squad("A", 0, 4, 20, 10, 90, stance), Squad("E", 1, 4, 20, enemyY, 270));
        sim.StepTicks(2 * BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "A").AttackTarget, Is.EqualTo(attacks ? SquadOf(sim, "E").Id : EntityId.None));
    }

    [Test]
    public void Pursue_TheEnemyBreaks_MenRunTheRunnersDown_WithinThirtyMetres_ThenReformWhereTheyStand()
    {
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 13, 270));
        var a = SquadOf(sim, "A");
        Rout(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(a.Pursuing, Is.True, "runners within 15 m: the chase is on");
        FixVec2 from = a.PursuitFrom;
        Assert.That(from, Is.EqualTo(FixVec2.FromInts(30, 10)), "it began at the anchor");
        int began = a.PursuitUntil - (10 * BattleState.TicksPerSecond);

        sim.StepTicks(3);
        Fix run = TestDefs.Man.RunPerTick;
        Assert.That(MenOf(sim, "A").Max(m => m.Velocity.Length.Raw), Is.GreaterThan((run * Fix.Ratio(9, 10)).Raw), "they run, not walk");

        Fix farthest = Fix.Zero;
        int ended = -1;
        for (int t = 0; t < 15 * BattleState.TicksPerSecond && ended < 0; t++)
        {
            sim.Step();
            foreach (var man in MenOf(sim, "A"))
            {
                Fix d = (man.Position - from).Length;
                farthest = d > farthest ? d : farthest;
            }

            ended = a.Pursuing ? -1 : sim.State.Tick;
        }

        Assert.Multiple(() =>
        {
            Assert.That(ended, Is.GreaterThan(0).And.LessThanOrEqualTo(began + (10 * BattleState.TicksPerSecond)), "10 s at most");
            Assert.That(farthest, Is.LessThanOrEqualTo(Fix.FromInt(30) + run), "the leash: 30 m from where it began");
            Assert.That(a.PursuitUntil, Is.EqualTo(ended), "it remembers when it ended");
            Assert.That(a.Anchor, Is.Not.EqualTo(from), "it re-formed where its men stood");
            Assert.That(a.Order.Kind, Is.EqualTo(SquadOrderKind.None));
        });
    }

    [Test]
    public void Pursue_NoNewChase_WithinAChasesLengthOfTheLastEnding()
    {
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 13, 270));
        var a = SquadOf(sim, "A");
        Rout(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assume.That(a.Pursuing, Is.True);
        sim.Enqueue(new SetStanceCommand(0, a.Id, SquadStance.Hold)); // called back: re-forms where they stand
        sim.Step();
        Assert.That(a.Pursuing, Is.False);
        sim.Enqueue(new SetStanceCommand(0, a.Id, SquadStance.Pursue));
        sim.StepTicks(2 * BattleState.TicksPerSecond);
        Assert.That(a.Pursuing, Is.False, "runners still near, but it rests a chase's length first");
    }

    [Test]
    public void Runners_RunAtNineTenthsOfTheirRun()
    {
        var sim = Battle(Squad("A", 0, 1, 30, 30, 90), Squad("E", 1, 1, 70, 50, 270));
        var man = MenOf(sim, "A")[0];
        man.MoraleState = MoraleState.Routing;
        man.Morale = 100;
        sim.StepTicks(3);
        Assert.That(man.Velocity.Length.Raw, Is.EqualTo((man.Body.RunPerTick * Fix.Ratio(9, 10)).Raw).Within(2));
    }

    [Test]
    public void Commands_RefusedWithTheirReasons_AMoveReplacesAnAttack()
    {
        var sim = Battle(Squad("A", 0, 4, 20, 10, 90), Squad("B", 0, 4, 40, 10, 90), Squad("E", 1, 4, 20, 40, 270), Squad("F", 1, 4, 40, 40, 270));
        var a = SquadOf(sim, "A");
        var b = SquadOf(sim, "B");
        var e = SquadOf(sim, "E");
        var f = SquadOf(sim, "F");
        Rout(sim, "F");
        Assert.Multiple(() =>
        {
            Assert.That(Reason(sim, new AttackSquadCommand(0, a.Id, new EntityId(999))), Is.EqualTo("unknown-entity"));
            Assert.That(Reason(sim, new AttackSquadCommand(1, a.Id, e.Id)), Is.EqualTo("wrong-side"));
            Assert.That(Reason(sim, new AttackSquadCommand(0, a.Id, b.Id)), Is.EqualTo("not-an-enemy"));
            Assert.That(Reason(sim, new AttackSquadCommand(0, a.Id, f.Id)), Is.EqualTo("target-broken"));
            Assert.That(Reason(sim, new AttackSquadCommand(0, a.Id, e.Id)), Is.Null);
            Assert.That(Reason(sim, new SetStanceCommand(1, a.Id, SquadStance.Pursue)), Is.EqualTo("wrong-side"));
            Assert.That(Reason(sim, new SetStanceCommand(1, f.Id, SquadStance.Hold)), Is.Null, "a standing instruction: a broken squad takes it");
            Assert.That(Reason(sim, new AttackSquadCommand(1, f.Id, a.Id)), Is.EqualTo("squad-broken"));
        });

        sim.Enqueue(new AttackSquadCommand(0, a.Id, e.Id));
        sim.Step();
        Assert.That(a.AttackTarget, Is.EqualTo(e.Id));
        sim.Enqueue(new MoveSquadCommand(0, a.Id, FixVec2.FromInts(20, 20)));
        sim.Step();
        Assert.That(a.AttackTarget, Is.EqualTo(EntityId.None), "a move replaces the attack");
    }

    [Test]
    public void Script_AttackAndStance_RunAsTheirCommands()
    {
        var state = BattleSetup.Create(new ScenarioDef("test-script-attack", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
            [Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 20, 50, 270)],
            [ScriptedOrderDef.SetStance(1, "A", SquadStance.Pursue), ScriptedOrderDef.AttackSquad(2, "A", "E")]), 1);
        var sim = new BattleSimulation(state);
        sim.StepTicks(2);
        var a = state.Squads.Single(s => s.Name == "A");
        Assert.That((a.Stance, a.AttackTarget), Is.EqualTo((SquadStance.Pursue, state.Squads.Single(s => s.Name == "E").Id)));
    }

    [Test]
    public void Hash_CoversStanceAttackAndChase()
    {
        var sim = Battle(Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 20, 40, 270));
        var a = SquadOf(sim, "A");
        var hashes = new List<ulong> { StateHasher.Compute(sim.State).Squads };
        a.Stance = SquadStance.Advance;
        hashes.Add(StateHasher.Compute(sim.State).Squads);
        a.AttackTarget = SquadOf(sim, "E").Id;
        hashes.Add(StateHasher.Compute(sim.State).Squads);
        a.Pursuing = true;
        hashes.Add(StateHasher.Compute(sim.State).Squads);
        a.PursuitUntil = 77;
        hashes.Add(StateHasher.Compute(sim.State).Squads);
        a.PursuitFrom = FixVec2.FromInts(3, 4);
        hashes.Add(StateHasher.Compute(sim.State).Squads);
        Assert.That(hashes.Distinct().Count(), Is.EqualTo(hashes.Count));
    }

    [Test]
    [Category("Determinism")]
    public void Determinism_SnapshotRestore_MidAttackAndChase_ContinuesIdentically()
    {
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 24, 270), Squad("F", 1, 4, 50, 14, 270));
        Rout(sim, "F");
        sim.Enqueue(new AttackSquadCommand(0, SquadOf(sim, "A").Id, SquadOf(sim, "E").Id));
        sim.StepTicks(150);
        var twin = new BattleSimulation(sim.State.Clone()); // mid-attack
        sim.StepTicks(600);
        twin.StepTicks(600);
        Assert.That(twin.ComputeHash().Total, Is.EqualTo(sim.ComputeHash().Total));
    }

    [Test]
    [Category("Determinism")]
    public void Determinism_SnapshotRestore_MidChase_ContinuesIdentically()
    {
        // Found in review: the test above clones before the chase begins.
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 13, 270));
        Rout(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond + 5);
        Assert.That(SquadOf(sim, "A").Pursuing, Is.True, "cloned mid-chase");
        var twin = new BattleSimulation(sim.State.Clone());
        sim.StepTicks(300);
        twin.StepTicks(300);
        Assert.That(twin.ComputeHash().Total, Is.EqualTo(sim.ComputeHash().Total));
    }

    [Test]
    public void Attack_TheAttackerLosesEveryMan_NoFault_TheAttackEnds()
    {
        // Found in review: an attacker with nobody left re-aimed into an empty squad and threw.
        var sim = Battle(Squad("A", 0, 1, 10, 10, 90), Squad("E", 1, 4, 60, 40, 270));
        var a = SquadOf(sim, "A");
        sim.Enqueue(new AttackSquadCommand(0, a.Id, SquadOf(sim, "E").Id));
        sim.Step();
        var man = MenOf(sim, "A")[0];
        man.IsAlive = false;
        man.Wound = WoundLevel.Down;
        Assert.DoesNotThrow(() => sim.StepTicks(3 * BattleState.TicksPerSecond));
        Assert.That(a.AttackTarget, Is.EqualTo(EntityId.None));
    }

    [Test]
    public void Attack_OnASquadBehind_ItStepsBackOutOfTheMeleeFirst_EvenAsTheTargetMoves()
    {
        // Found in review: re-aiming cleared the withdrawal, so the squad turned its back next to the enemy.
        var sim = new BattleSimulation(BattleSetup.Create(new ScenarioDef("test-withdraw-attack", "test",
            new MapDef(Fix.FromInt(100), Fix.FromInt(60)),
            [Squad("A", 0, 8, 40, 30, 90), Squad("B", 1, 8, 40, 31, 270), Squad("C", 1, 4, 40, 8, 90)]), 3));
        var a = SquadOf(sim, "A");
        var b = SquadOf(sim, "B");
        var c = SquadOf(sim, "C");
        sim.StepTicks(30); // in contact with B
        sim.Enqueue(new AttackSquadCommand(0, a.Id, c.Id));
        sim.Enqueue(new MoveSquadCommand(1, c.Id, FixVec2.FromInts(80, 8))); // C walks away: A re-aims
        bool was = false;
        Fix? gapWhenDone = null;
        for (int t = 0; t < 200 && gapWhenDone == null && !a.Broken; t++)
        {
            sim.Step();
            if (was && !a.Withdrawing)
            {
                gapWhenDone = MenOf(sim, "A").Where(m => m.IsAlive).SelectMany(m => MenOf(sim, "B").Where(x => x.IsAlive && !x.IsRouting)
                    .Select(x => (m.Position - x.Position).Length - m.Radius - x.Radius)).Min();
            }

            was = a.Withdrawing;
        }

        Assert.That(gapWhenDone, Is.Not.Null, "it stepped back out first");
        Assert.That(gapWhenDone!.Value, Is.GreaterThan(Fix.FromInt(2)), "clear of B before it turns to march");
    }

    [Test]
    public void Break_EndsTheAttack_AndTheChase()
    {
        var sim = Battle(Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 20, 40, 270), Squad("P", 0, 4, 50, 10, 90, SquadStance.Pursue),
            Squad("F", 1, 4, 50, 13, 270));
        var a = SquadOf(sim, "A");
        var p = SquadOf(sim, "P");
        sim.Enqueue(new AttackSquadCommand(0, a.Id, SquadOf(sim, "E").Id));
        Rout(sim, "F");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That((a.AttackTarget.IsNone, p.Pursuing), Is.EqualTo((false, true)), "attacking; chasing");
        foreach (var man in MenOf(sim, "A").Concat(MenOf(sim, "P")))
        {
            man.Morale = 50; // both squads run
        }

        sim.StepTicks(2);
        Assert.Multiple(() =>
        {
            Assert.That(a.Broken && p.Broken, Is.True);
            Assert.That(a.AttackTarget, Is.EqualTo(EntityId.None), "a running squad attacks no one");
            Assert.That(p.Pursuing, Is.False, "…and chases no one");
        });
    }

    [Test]
    public void AMoveOrAnAttack_CallsTheMenBackFromAChase_RememberingWhenItEnded()
    {
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 13, 270), Squad("G", 1, 4, 70, 50, 270));
        var a = SquadOf(sim, "A");
        Rout(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assume.That(a.Pursuing, Is.True);
        sim.Enqueue(new AttackSquadCommand(0, a.Id, SquadOf(sim, "G").Id));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(a.Pursuing, Is.False);
            Assert.That(a.PursuitUntil, Is.EqualTo(sim.State.Tick), "ended now");
            Assert.That(a.AttackTarget, Is.EqualTo(SquadOf(sim, "G").Id), "the march to the new target gathers the men");
        });
    }

    [Test]
    public void Pursue_OneManRunningFromASquadThatHolds_DrawsNoChase()
    {
        // Found in review: the user's rule is "when the enemy it fights breaks", so a lone runner is no bait.
        var sim = Battle(Squad("A", 0, 8, 30, 10, 90, SquadStance.Pursue), Squad("E", 1, 4, 30, 40, 270));
        var runner = MenOf(sim, "E")[0];
        runner.Position = FixVec2.FromInts(30, 16);
        runner.MoraleState = MoraleState.Routing;
        runner.Morale = 100;
        bool chased = false;
        for (int t = 0; t < 2 * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            chased |= SquadOf(sim, "A").Pursuing;
        }

        Assert.That(chased, Is.False, "not even for a tick");
    }
}
