using Knez.Sim.Ai;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Formations;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Ai;

/// <summary>
/// M4 step 2, the AI frame (battle-sim.md §14; decided by the user 2026-11-26): the scenario names the sides the AI plays;
/// once a second each of its squads decides — attack while its side's men in the fight are at least 0.8 of the enemy's
/// (a heavily wounded man counts half), else hold in Advance; the nearest target, one an ally fights 10 m nearer, at most
/// two on one while another stands free, the second on a target going round to its open flank first; regroup (fall back
/// 20 m toward an ally) below 400 average morale out of melee or after re-forming, until 600; leave the field below a
/// third. AI Retainers use their built abilities at the right moment.
/// </summary>
[TestFixture]
public class AiTests
{
    private static readonly KitDef Mail = TestDefs.KitOf("sword-shield-mail");

    private static SquadSpawnDef Squad(string name, int side, int count, int x, int y, int facing) =>
        TestDefs.Squad(name, side, count, FixVec2.FromInts(x, y), facing);

    private static BattleSimulation Battle(SquadSpawnDef[] squads, int[]? ai = null, RetainerSpawnDef[]? retainers = null,
        ulong seed = 1, ScriptedOrderDef[]? script = null) =>
        new(BattleSetup.Create(new ScenarioDef("test-ai", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)), squads, script,
            retainers: retainers, aiSides: ai ?? [1]), seed));

    private static Squad SquadOf(BattleSimulation sim, string name) => sim.State.Squads.Single(s => s.Name == name);

    private static Fighter[] MenOf(BattleSimulation sim, string name) =>
        SquadOf(sim, name).Members.Select(id => sim.State.FindFighter(id)!).ToArray();

    private static RetainerSpawnDef Leader(string trait, string squad, int side = 1) =>
        RetainerSpawnDef.InSquad("Ulfr", side, TraitDef.StandardOf(trait), TestDefs.Man, TestDefs.Level("elite"), Mail, squad);

    /// <summary>Takes men out of the fight by hand (fallen), so no seed decides the odds.</summary>
    private static void Fell(BattleSimulation sim, string squad, int count)
    {
        foreach (var man in MenOf(sim, squad).Take(count))
        {
            man.IsAlive = false;
            man.Wound = WoundLevel.Down;
        }
    }

    /// <summary>Sets <paramref name="squad"/> attacking <paramref name="target"/> as the AI would have it.</summary>
    private static void Engaged(BattleSimulation sim, string squad, string target)
    {
        var s = SquadOf(sim, squad);
        var t = SquadOf(sim, target);
        OrderSystem.Attack(sim.State, s, t);
        s.AiState = SquadAiState.Engage;
        s.AiTarget = t.Id;
    }

    /// <summary>Steps until <paramref name="done"/> or <paramref name="seconds"/> run out; whether it happened.</summary>
    private static bool StepUntil(BattleSimulation sim, Func<bool> done, int seconds)
    {
        for (int t = 0; t < seconds * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            if (done())
            {
                return true;
            }
        }

        return false;
    }

    private static List<SimEvent> StepCollect(BattleSimulation sim, int ticks)
    {
        var events = new List<SimEvent>();
        for (int t = 0; t < ticks; t++)
        {
            sim.Step();
            events.AddRange(sim.State.Events);
        }

        return events;
    }

    [Test]
    public void NoAiSide_SquadsStayIdle_AndTakeCommands()
    {
        var sim = Battle([Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 25, 40, 270)], ai: []);
        var e = SquadOf(sim, "E");
        sim.Enqueue(new MoveSquadCommand(1, e.Id, FixVec2.FromInts(25, 45)));
        var events = StepCollect(sim, 2 * BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Idle));
            Assert.That(e.Stance, Is.EqualTo(SquadStance.Hold));
            Assert.That(events.OfType<CommandRejected>(), Is.Empty);
            Assert.That(events.OfType<SquadAiChanged>(), Is.Empty);
        });
    }

    [TestCase(1UL)]
    [TestCase(2UL)]
    [TestCase(3UL)]
    public void NotOutnumbered_EachSquadAttacksTheNearestEnemySquad(ulong seed)
    {
        // E is 30 m from A and 46 m from B: more than the 5 m of randomness apart. 8 men against 4 + 4: even odds.
        var sim = Battle([Squad("A", 0, 4, 20, 10, 90), Squad("B", 0, 4, 60, 10, 90), Squad("E", 1, 8, 25, 40, 270)], seed: seed);
        var e = SquadOf(sim, "E");
        var events = StepCollect(sim, BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Engage));
            Assert.That(e.AiTarget, Is.EqualTo(SquadOf(sim, "A").Id));
            Assert.That(e.AttackTarget, Is.EqualTo(SquadOf(sim, "A").Id), "it attacks with the player's own attack order");
            Assert.That(e.Stance, Is.EqualTo(SquadStance.Advance));
            Assert.That(events.OfType<SquadAiChanged>().Single().To, Is.EqualTo(SquadAiState.Engage));
        });
    }

    [Test]
    public void Outnumbered_HoldsItsGroundInAdvance_AttacksOnceTheOddsTurn()
    {
        // 8 against 16: 0.5 of the enemy, under 0.8.
        var sim = Battle([Squad("A", 0, 8, 20, 10, 90), Squad("B", 0, 8, 60, 10, 90), Squad("E", 1, 8, 40, 45, 270)]);
        var e = SquadOf(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Hold));
            Assert.That(e.Stance, Is.EqualTo(SquadStance.Advance), "it meets anyone who comes within 15 m");
            Assert.That(e.AttackTarget, Is.EqualTo(EntityId.None));
            Assert.That(e.Order.Kind, Is.EqualTo(SquadOrderKind.None));
        });

        Fell(sim, "B", 6); // 8 against 10: 0.8 — not outnumbered any more
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Engage), "re-checked each second");
    }

    [Test]
    public void Attacking_HoldsOnlyBelowSevenTenths_ResumesAtEightTenths()
    {
        // 10 against 10: it attacks. Heavy wounds bring it to 0.75 (still attacking), 0.65 (it holds), 0.75 (still
        // holding) and 0.8 (it attacks again).
        var sim = Battle([Squad("A", 0, 10, 20, 10, 90), Squad("E", 1, 10, 40, 50, 270)]);
        var e = SquadOf(sim, "E");
        var enemy = MenOf(sim, "E");
        void Strength(int halves) // E's strength in halves, against A's 20 halves: heavy wounds count half
        {
            for (int i = 0; i < enemy.Length; i++)
            {
                enemy[i].Wound = i < 20 - halves ? WoundLevel.Heavy : WoundLevel.None;
            }
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Engage), "1.0: attacks");
        Strength(15); // 0.75
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Engage), "0.75: an attack under way goes on");
        Strength(13); // 0.65
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Hold), "0.65: it holds");
        Strength(15);
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Hold), "0.75: still holding");
        Strength(16);
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Engage), "0.8: attacks again");
    }

    [Test]
    public void Strength_AHeavilyWoundedManCountsHalf()
    {
        // 8 against 10 is 0.8: it attacks. With 4 of the 8 heavily wounded it is 6 against 10: it holds.
        var sim = Battle([Squad("A", 0, 10, 20, 10, 90), Squad("E", 1, 8, 40, 45, 270)]);
        foreach (var man in MenOf(sim, "E").Take(4))
        {
            man.Wound = WoundLevel.Heavy;
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "E").AiState, Is.EqualTo(SquadAiState.Hold));
    }

    [TestCase(1UL)]
    [TestCase(2UL)]
    [TestCase(3UL)]
    public void Target_OneAnAllyFightsCountsTenMetresNearer(ulong seed)
    {
        // E2 stands 32 m from A and from B; E1 already attacks A, so E2 goes for A too.
        var sim = Battle([Squad("A", 0, 4, 20, 20, 90), Squad("B", 0, 4, 60, 20, 90),
            Squad("E1", 1, 4, 20, 34, 270), Squad("E2", 1, 4, 40, 45, 270)], seed: seed);
        Engaged(sim, "E1", "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "E2").AiTarget, Is.EqualTo(SquadOf(sim, "A").Id));
    }

    [TestCase(1UL)]
    [TestCase(2UL)]
    public void Target_NoThirdSquadOnOne_WhileAnotherEnemySquadStandsFree(ulong seed)
    {
        // E3 is 20 m from A and 43 m from B, but E1 and E2 already attack A.
        var sim = Battle([Squad("A", 0, 4, 20, 20, 90), Squad("B", 0, 4, 60, 20, 90),
            Squad("E1", 1, 4, 14, 34, 270), Squad("E2", 1, 4, 26, 34, 270), Squad("E3", 1, 4, 22, 40, 270)], seed: seed);
        Engaged(sim, "E1", "A");
        Engaged(sim, "E2", "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "E3").AiTarget, Is.EqualTo(SquadOf(sim, "B").Id));
    }

    [Test]
    public void Target_AnAttackItsStanceBegan_StillFollowsTheAisRules()
    {
        // Found in review: an attack begun by its Advance stance (here by hand) was kept, a third squad on A while B stood free.
        var sim = Battle([Squad("A", 0, 4, 20, 20, 90), Squad("B", 0, 4, 60, 20, 90),
            Squad("E1", 1, 4, 14, 34, 270), Squad("E2", 1, 4, 26, 34, 270), Squad("E3", 1, 4, 22, 40, 270)]);
        Engaged(sim, "E1", "A");
        Engaged(sim, "E2", "A");
        OrderSystem.Attack(sim.State, SquadOf(sim, "E3"), SquadOf(sim, "A"));
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "E3").AttackTarget, Is.EqualTo(SquadOf(sim, "B").Id));
    }

    [Test]
    public void Flank_TheSecondSquadInTheTargetsFront_GoesRoundToItsOpenSide_ThenAttacks()
    {
        // A faces north at (40, 20); E1 fights it head on; E2 stands in A's front, a little east: it goes round to A's east
        // flank — 8 m beyond A's 1 m half-width — and attacks once it is out of A's front.
        var sim = Battle([Squad("A", 0, 6, 40, 20, 90), Squad("E1", 1, 6, 40, 23, 270), Squad("E2", 1, 6, 44, 40, 270)]);
        var a = SquadOf(sim, "A");
        var e2 = SquadOf(sim, "E2");
        Engaged(sim, "E1", "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e2.AiState, Is.EqualTo(SquadAiState.Advance));
            Assert.That(e2.AiTarget, Is.EqualTo(a.Id));
            Assert.That(e2.AttackTarget, Is.EqualTo(EntityId.None), "no attack yet: it marches round first");
            Assert.That(e2.Order.Kind, Is.EqualTo(SquadOrderKind.Move));
            Assert.That((e2.Order.Target - FixVec2.FromInts(49, 20)).Length, Is.LessThan(Fix.FromInt(2)), "A's east flank");
        });

        bool engaged = StepUntil(sim, () => e2.AiState == SquadAiState.Engage, 30);
        FixVec2 from = e2.Anchor - a.Anchor;
        int off = a.Facing.DeltaTo(FixMath.Atan2(from.Y, from.X));
        Assert.Multiple(() =>
        {
            Assert.That(engaged, Is.True);
            Assert.That(e2.AttackTarget, Is.EqualTo(a.Id));
            Assert.That(Math.Abs(off), Is.GreaterThan(sim.State.Scenario.Combat.FrontArcUnits), "it attacks from outside A's front");
        });
    }

    [Test]
    public void Flank_GoesToTheSideAwayFromTheAllyAlreadyThere_EvenWhenTheOtherIsNearer()
    {
        // E1 fights A from A's west; E2, in A's front a little west, is nearer A's west flank point (31, 20) but goes east.
        var sim = Battle([Squad("A", 0, 6, 40, 20, 90), Squad("E1", 1, 6, 30, 20, 0), Squad("E2", 1, 6, 36, 40, 270)]);
        var e2 = SquadOf(sim, "E2");
        Engaged(sim, "E1", "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e2.AiState, Is.EqualTo(SquadAiState.Advance));
            Assert.That((e2.Order.Target - FixVec2.FromInts(49, 20)).Length, Is.LessThan(Fix.FromInt(2)), "A's east flank, away from E1");
        });
    }

    [Test]
    public void Flank_ASquadAlreadyOnTheTargetsSide_AttacksStraight()
    {
        var sim = Battle([Squad("A", 0, 6, 40, 20, 90), Squad("E1", 1, 6, 40, 23, 270), Squad("E2", 1, 6, 60, 20, 180)]);
        Engaged(sim, "E1", "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        var e2 = SquadOf(sim, "E2");
        Assert.Multiple(() =>
        {
            Assert.That(e2.AiState, Is.EqualTo(SquadAiState.Engage));
            Assert.That(e2.AttackTarget, Is.EqualTo(SquadOf(sim, "A").Id));
        });
    }

    [Test]
    public void Regroup_LowMoraleOutOfMelee_FallsBackTwentyMetresTowardAnAlly_FightsAgainAtSixHundred()
    {
        // E1's men average 300; A is 20 m off (no melee); its ally E2 is 32 m away.
        var sim = Battle([Squad("A", 0, 4, 20, 10, 90), Squad("E1", 1, 8, 20, 30, 270), Squad("E2", 1, 4, 40, 55, 270)]);
        var e1 = SquadOf(sim, "E1");
        var e2 = SquadOf(sim, "E2");
        FixVec2 start = e1.Anchor;
        foreach (var man in MenOf(sim, "E1"))
        {
            man.Morale = 300;
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Fix toAlly = (e2.Anchor - start).Length - (e2.Anchor - e1.Order.Target).Length;
        Assert.Multiple(() =>
        {
            Assert.That(e1.AiState, Is.EqualTo(SquadAiState.Regroup));
            Assert.That(e1.Stance, Is.EqualTo(SquadStance.Hold));
            Assert.That(e1.AttackTarget, Is.EqualTo(EntityId.None));
            Assert.That(e1.Order.Kind, Is.EqualTo(SquadOrderKind.Move));
            Assert.That((e1.Order.Target - start).Length, Is.InRange(Fix.FromInt(19), Fix.FromInt(21)), "20 m…");
            Assert.That(toAlly, Is.GreaterThan(Fix.FromInt(19)), "…toward its ally");
        });

        foreach (var man in MenOf(sim, "E1"))
        {
            man.Morale = 650;
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            // E2 already attacks A, so E1 — in A's front — may go round its flank first.
            Assert.That(e1.AiState, Is.AnyOf(SquadAiState.Engage, SquadAiState.Advance), "back at 600: it fights again");
            Assert.That(e1.AiTarget, Is.EqualTo(SquadOf(sim, "A").Id));
        });
    }

    [Test]
    public void Regroup_InMelee_ItFightsOn()
    {
        var sim = Battle([Squad("A", 0, 4, 20, 20, 90), Squad("E", 1, 8, 20, 23, 270)]);
        Assert.That(StepUntil(sim, () => OrderSystem.InMelee(sim.State, SquadOf(sim, "E")), 10), Is.True, "in melee");
        foreach (var man in MenOf(sim, "E"))
        {
            man.Morale = 380; // under 400, above what a fallen mate costs on top
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(SquadOf(sim, "E").AiState, Is.Not.EqualTo(SquadAiState.Regroup));
    }

    [Test]
    public void Regroup_ABrokenSquad_RegroupsOnceItReforms()
    {
        var sim = Battle([Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 8, 20, 25, 270), Squad("E2", 1, 4, 60, 55, 270)]);
        var e = SquadOf(sim, "E");
        e.Broken = true;
        foreach (var man in MenOf(sim, "E"))
        {
            man.Morale = 100;
            man.MoraleState = MoraleState.Routing;
        }

        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Regroup), "running: regrouping is what comes next");

        // It re-forms (by hand) 10 m from A, its men at 450: it falls back and stays out until 600.
        e.Broken = false;
        foreach (var man in MenOf(sim, "E"))
        {
            man.Morale = 450;
            man.MoraleState = MoraleState.Shaken;
        }

        SlotAssignment.ReformAtCentre(sim.State, e);
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Regroup));
            Assert.That(e.Order.Kind, Is.EqualTo(SquadOrderKind.Move), "an enemy within 20 m: it falls back");
        });
    }

    [Test]
    public void Withdraw_BelowAThird_ItMarchesOffItsEdge_ItsMenLeaveAlive_TheOtherSideWins()
    {
        // 2 against 8: a quarter.
        var sim = Battle([Squad("A", 0, 8, 40, 20, 90), Squad("E", 1, 2, 40, 45, 270)]);
        var e = SquadOf(sim, "E");
        var men = MenOf(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Withdraw));
            Assert.That(e.Leaving, Is.True);
            Assert.That(e.Stance, Is.EqualTo(SquadStance.Hold));
            Assert.That(e.Order.Kind, Is.EqualTo(SquadOrderKind.Move));
            Assert.That(e.Order.Target.Y, Is.GreaterThan(Fix.FromInt(58)), "its own edge, north, away from the enemy");
        });

        Fell(sim, "A", 6); // the odds turn, but leaving is never undone
        var events = new List<SimEvent>();
        bool gone = StepUntil(sim, () =>
        {
            events.AddRange(sim.State.Events);
            return men.All(m => !m.IsAlive);
        }, 40);
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(gone, Is.True, "both men reached the edge");
            Assert.That(men.Select(m => m.MoraleState), Is.All.EqualTo(MoraleState.Left));
            Assert.That(men.Select(m => m.Wound), Is.All.EqualTo(WoundLevel.None), "alive");
            Assert.That(events.OfType<MoraleStateChanged>().Count(c => c.To == MoraleState.Left), Is.EqualTo(2));
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Withdraw));
            Assert.That(sim.State.Winner, Is.EqualTo(0));
        });
    }

    [Test]
    public void Withdraw_ALeavingSquadDoesNotBreak_ItsRunnersRunAlone_TheRestLeave()
    {
        // Found in review: men leaving at the edge left its runners the majority, so it "broke" at the edge.
        var sim = Battle([Squad("A", 0, 8, 40, 10, 90), Squad("B", 0, 8, 60, 10, 90), Squad("E", 1, 5, 40, 45, 270)]);
        var e = SquadOf(sim, "E");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.Leaving, Is.True, "5 against 16: under a third");
        foreach (var man in MenOf(sim, "E").Take(3)) // more than half running: a squad on the field would break
        {
            man.Morale = 100;
            man.MoraleState = MoraleState.Routing;
        }

        var men = MenOf(sim, "E");
        var events = new List<SimEvent>();
        bool decided = StepUntil(sim, () =>
        {
            events.AddRange(sim.State.Events);
            return sim.State.Winner != BattleState.Undecided;
        }, 60);
        Assert.Multiple(() =>
        {
            Assert.That(events.OfType<SquadBroke>(), Is.Empty);
            Assert.That(decided, Is.True);
            Assert.That(sim.State.Winner, Is.EqualTo(0));
            Assert.That(men.Select(m => m.MoraleState), Is.All.AnyOf(MoraleState.Left, MoraleState.Fled));
            Assert.That(men.Count(m => m.MoraleState == MoraleState.Left), Is.EqualTo(2), "the two who never ran left in order");
        });
    }

    [Test]
    public void Withdraw_ASquadBrokenWhenItsSideLeaves_ReformsOnceAManRallies_AndMarchesOff()
    {
        var sim = Battle([Squad("A", 0, 8, 40, 10, 90), Squad("B", 0, 8, 60, 10, 90), Squad("E", 1, 5, 40, 40, 270)]);
        var e = SquadOf(sim, "E");
        e.Broken = true;
        var all = MenOf(sim, "E");
        for (int i = 0; i < all.Length; i++)
        {
            // Two rally as soon as they are safe (350); three keep running until they flee — more than half, so on the
            // field it couldn't re-form until they were gone.
            all[i].Morale = i < 2 ? 380 : 100;
            all[i].MoraleState = MoraleState.Routing;
        }

        var men = MenOf(sim, "E");
        var events = new List<SimEvent>();
        bool decided = StepUntil(sim, () =>
        {
            events.AddRange(sim.State.Events);
            return sim.State.Winner != BattleState.Undecided;
        }, 90);
        Assert.Multiple(() =>
        {
            Assert.That(e.AiState, Is.EqualTo(SquadAiState.Withdraw));
            Assert.That(events.OfType<SquadBroke>().Any(b => b.Reformed && b.Running * 2 >= b.InFight), Is.True,
                "it re-formed to march off while most were still running");
            Assert.That(e.Facing, Is.EqualTo(Angle.FromDegrees(90)), "re-formed facing its edge (north), so its men walk off forwards");
            Assert.That(decided, Is.True);
            Assert.That(men.Select(m => m.MoraleState), Is.All.AnyOf(MoraleState.Left, MoraleState.Fled));
            Assert.That(men.Any(m => m.MoraleState == MoraleState.Left), Is.True);
        });
    }

    [Test]
    public void Withdraw_AHouseAgainstTheEdgeStraightAcross_ItMarchesOffBesideIt()
    {
        // Found in review: the march ended in front of the house and nobody ever reached the edge.
        SquadSpawnDef[] squads = [Squad("A", 0, 8, 40, 10, 90), Squad("E", 1, 2, 40, 40, 270)];
        var map = new MapDef(Fix.FromInt(80), Fix.FromInt(60), [new WallDef(FixVec2.FromInts(30, 54), FixVec2.FromInts(50, 60), WallKind.House)]);
        var sim = new BattleSimulation(BattleSetup.Create(new ScenarioDef("test-ai-exit", "test", map, squads, aiSides: [1]), 1));
        var men = MenOf(sim, "E");
        var meter = new Knez.Sim.Movement.MovementMeter();
        bool decided = StepUntil(sim, () =>
        {
            meter.Observe(sim.State);
            return sim.State.Winner != BattleState.Undecided;
        }, 60);
        Assert.Multiple(() =>
        {
            Assert.That(decided, Is.True);
            Assert.That(men.Select(m => m.MoraleState), Is.All.EqualTo(MoraleState.Left));
            Assert.That(men.All(m => m.Position.X < Fix.FromInt(30) || m.Position.X > Fix.FromInt(50)), Is.True, "beside the house");
            Assert.That(meter.LongestFarFromSlot, Is.LessThan(2 * BattleState.TicksPerSecond),
                "walking off one by one is off their slots on purpose, not a spell away from them");
        });
    }

    [Test]
    public void Hold_AnAttackKeptFromAMelee_IsDroppedOnceItsTargetIsBeyondTheAdvanceRange()
    {
        // Found in review: holding, it followed a retreating enemy across the field.
        var sim = Battle([Squad("A", 0, 8, 40, 10, 90), Squad("B", 0, 8, 70, 10, 90), Squad("E", 1, 8, 40, 40, 270)]);
        var e = SquadOf(sim, "E");
        var a = SquadOf(sim, "A");
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(e.AiState, Is.EqualTo(SquadAiState.Hold), "8 against 16");
        OrderSystem.Attack(sim.State, e, a); // as if kept from a melee
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.Multiple(() =>
        {
            Assert.That(e.AttackTarget, Is.EqualTo(EntityId.None), "A is 30 m off: beyond 15 m");
            Assert.That(e.Order.Kind, Is.EqualTo(SquadOrderKind.None));
        });
    }

    [Test]
    public void Withdraw_AManWalkingOffInMelee_StepsBackAtStepBackPace()
    {
        // Found in the re-review: an engaged man walked off at full walk, faster than a marching squad steps back.
        var sim = Battle([Squad("A", 0, 1, 40, 20, 90), Squad("E", 1, 1, 40, 21, 270)], ai: []);
        var e = SquadOf(sim, "E");
        var man = MenOf(sim, "E")[0];
        Assert.That(StepUntil(sim, () => man.IsEngaged, 5), Is.True, "in melee");
        e.Leaving = true; // its march off has ended (no order): he walks the rest
        FixVec2 before = man.Position;
        sim.Step();
        Fix stepBack = man.Body.WalkPerTick * sim.State.Scenario.Movement.StepBackPace;
        Assert.That((man.Position - before).Length, Is.LessThanOrEqualTo(stepBack + Fix.Ratio(1, 100)));
    }

    [Test]
    public void Pursue_ChasesTheRunnersOfALeavingSquad_OnceMoreThanHalfRun()
    {
        // A leaving squad doesn't break; its runners are fair game once there are as many as would have broken it.
        var sim = Battle([Squad("A", 0, 8, 40, 30, 90), Squad("E", 1, 5, 40, 36, 270)], ai: []);
        var a = SquadOf(sim, "A");
        var e = SquadOf(sim, "E");
        sim.Enqueue(new SetStanceCommand(0, a.Id, SquadStance.Pursue));
        e.Leaving = true;
        var men = MenOf(sim, "E");
        void Run(int count)
        {
            for (int i = 0; i < count; i++)
            {
                men[i].Morale = 100;
                men[i].MoraleState = MoraleState.Routing;
            }
        }

        Run(2); // 2 of 5: not half
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(a.Pursuing, Is.False);
        Run(3); // 3 of 5: more than half
        sim.StepTicks(BattleState.TicksPerSecond);
        Assert.That(a.Pursuing, Is.True);
    }

    [Test]
    [Category("Slow")]
    public void Villages_AiAgainstAi_EveryBattleIsDecided()
    {
        // Found in the re-review: seed 7 never ended (two men walking off swapped between two staggered rocks).
#if DEBUG
        Assert.Ignore("Release only (slow in Debug): dotnet test sim/Knez.Sim.sln -c Release --filter Name~Villages_AiAgainstAi");
#else
        var scenario = Knez.Sim.Cli.ScenarioFiles.Load(RepoPaths.Scenario("sandbox/villages")).WithAi([0, 1]);
        var undecided = new System.Collections.Concurrent.ConcurrentBag<ulong>();
        Parallel.For(1, 21, seed =>
        {
            var sim = new BattleSimulation(BattleSetup.Create(scenario, (ulong)seed));
            for (int t = 0; t < 7200 && sim.State.Winner == BattleState.Undecided; t++)
            {
                sim.Step();
            }

            if (sim.State.Winner == BattleState.Undecided)
            {
                undecided.Add((ulong)seed);
            }
        });
        Assert.That(undecided.OrderBy(s => s), Is.Empty, "undecided after 6 minutes");
#endif
    }

    [Test]
    public void Commands_ForASideTheAiPlays_AreRefused()
    {
        var sim = Battle([Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 25, 40, 270)]);
        sim.Enqueue(new SetStanceCommand(1, SquadOf(sim, "E").Id, SquadStance.Pursue));
        sim.Enqueue(new SetStanceCommand(0, SquadOf(sim, "A").Id, SquadStance.Pursue));
        sim.Step();
        var refused = sim.State.Events.OfType<CommandRejected>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(refused.Select(r => r.Reason.Code), Is.EqualTo(new[] { "ai-side" }));
            Assert.That(SquadOf(sim, "A").Stance, Is.EqualTo(SquadStance.Pursue), "the player's own side takes orders");
        });
    }

    [Test]
    public void Scenario_ScriptOrdersForAnAiSide_AreAnError_WithAiDropsThem()
    {
        SquadSpawnDef[] squads = [Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 25, 40, 270)];
        ScriptedOrderDef[] script = [new(1, "A", FixVec2.FromInts(20, 12), null), new(1, "E", FixVec2.FromInts(25, 38), null)];
        var map = new MapDef(Fix.FromInt(80), Fix.FromInt(60));
        var error = Assert.Throws<ArgumentException>(() => new ScenarioDef("t", "t", map, squads, script, aiSides: [1]));
        Assert.That(error!.Message, Does.Contain("script[1]").And.Contain("AI"));

        var plain = new ScenarioDef("t", "t", map, squads, script);
        var withAi = plain.WithAi([1]);
        Assert.Multiple(() =>
        {
            Assert.That(withAi.AiSides, Is.EqualTo(new[] { 1 }));
            Assert.That(withAi.Script.Select(o => o.SquadName), Is.EqualTo(new[] { "A" }));
            Assert.That(withAi.Fingerprint, Is.Not.EqualTo(plain.Fingerprint));
            Assert.That(plain.PlaysAi(1), Is.False);
            Assert.That(withAi.PlaysAi(1), Is.True);
            Assert.That(withAi.PlaysAi(0), Is.False);
        });
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void Scenario_AnAiSideMustHaveSquadsOrRetainers(int side)
    {
        Assert.Throws<ArgumentException>(() => new ScenarioDef("t", "t", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
            [Squad("A", 0, 4, 20, 10, 90), Squad("E", 1, 4, 25, 40, 270)], aiSides: [side]));
    }

    [Test]
    public void BattleCry_WithFourEnemiesInItsRadius_AndContact()
    {
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("E", 1, 8, 20, 24, 270)], retainers: [Leader("proud", "E")]);
        var cries = StepCollect(sim, 3 * BattleState.TicksPerSecond).OfType<AbilityUsed>().ToList();
        Assert.That(cries.Select(c => c.Ability), Is.EqualTo(new[] { "battle-cry" }), "once: it cools down for 2 minutes");
    }

    [Test]
    public void BattleCry_NotForThreeEnemies()
    {
        var sim = Battle([Squad("A", 0, 3, 20, 20, 90), Squad("E", 1, 8, 20, 24, 270)], retainers: [Leader("proud", "E")]);
        Assert.That(StepCollect(sim, 5 * BattleState.TicksPerSecond).OfType<AbilityUsed>(), Is.Empty);
    }

    [Test]
    public void BattleCry_NotBeforeContact()
    {
        // 8 men within 15 m, but nobody within 5 m and no melee yet: E (outnumbered, in Advance) only starts toward A.
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("B", 0, 8, 60, 20, 90), Squad("E", 1, 8, 20, 32, 270)],
            retainers: [Leader("proud", "E")]);
        Assert.That(StepCollect(sim, 3 * BattleState.TicksPerSecond).OfType<AbilityUsed>(), Is.Empty);
    }

    [Test]
    public void CloseRanks_WhenAnEnemySquadAttackingItComesWithinTenMetres_BeforeTheFirstBlow()
    {
        // E is outnumbered (holds); A attacks it from 30 m.
        var sim = Battle([Squad("A", 0, 8, 20, 10, 90), Squad("B", 0, 8, 70, 10, 90), Squad("E", 1, 8, 20, 40, 270)],
            retainers: [Leader("cautious", "E")]);
        var a = SquadOf(sim, "A");
        var e = SquadOf(sim, "E");
        sim.Enqueue(new AttackSquadCommand(0, a.Id, e.Id));
        int used = -1;
        int firstBlow = -1;
        StepUntil(sim, () =>
        {
            used = used < 0 && sim.State.Events.OfType<AbilityUsed>().Any() ? sim.State.Tick : used;
            firstBlow = firstBlow < 0 && sim.State.Events.OfType<BlowStruck>().Any() ? sim.State.Tick : firstBlow;
            return firstBlow >= 0;
        }, 30);
        Assert.Multiple(() =>
        {
            Assert.That(used, Is.GreaterThan(0), "closed ranks");
            Assert.That(firstBlow, Is.GreaterThan(used), "before contact");
        });
    }

    [Test]
    public void CloseRanks_NotWhileNobodyAttacksIt()
    {
        // A holds 13 m off: nobody attacks E (E itself starts toward A, in Advance), and there is no melee within 3 s.
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("B", 0, 8, 70, 10, 90), Squad("E", 1, 8, 20, 34, 270)],
            retainers: [Leader("cautious", "E")]);
        Assert.That(StepCollect(sim, 3 * BattleState.TicksPerSecond).OfType<AbilityUsed>(), Is.Empty);
    }

    [Test]
    public void StandToTheDeath_InMelee_WhenAQuarterOfItsMenWaverOrRun()
    {
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("E", 1, 8, 20, 23, 270)], retainers: [Leader("loyal", "E")]);
        Assert.That(StepUntil(sim, () => sim.State.Events.OfType<BlowStruck>().Any(), 10), Is.True, "in melee");
        var events = StepCollect(sim, BattleState.TicksPerSecond / 2);
        Assert.That(events.OfType<AbilityUsed>(), Is.Empty, "steady: not yet");

        foreach (var man in MenOf(sim, "E").Where(m => !m.IsRetainer).Take(3)) // 3 of 9: a third
        {
            man.Morale = 300;
        }

        events = StepCollect(sim, BattleState.TicksPerSecond / 2);
        Assert.That(events.OfType<AbilityUsed>().Select(u => u.Ability), Is.EqualTo(new[] { "stand-to-the-death" }));
    }

    [Test]
    public void AbilitiesNotBuilt_AreNeverUsed()
    {
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("E", 1, 8, 20, 23, 270)], retainers: [Leader("bold", "E")]);
        Assert.That(StepCollect(sim, 10 * BattleState.TicksPerSecond).OfType<AbilityUsed>(), Is.Empty);
    }

    [Test]
    public void PlayerRetainers_AreNotUsedByTheAi()
    {
        var sim = Battle([Squad("A", 0, 8, 20, 20, 90), Squad("E", 1, 8, 20, 24, 270)], retainers: [Leader("proud", "A", side: 0)]);
        Assert.That(StepCollect(sim, 3 * BattleState.TicksPerSecond).OfType<AbilityUsed>(), Is.Empty);
    }

    [Category("Determinism")]
    [Test]
    public void AiAgainstAi_TwinRunsAndARestoredSnapshot_GiveTheSameHashes()
    {
        SquadSpawnDef[] squads = [Squad("A", 0, 8, 20, 10, 90), Squad("B", 0, 6, 50, 12, 90),
            Squad("E1", 1, 8, 25, 48, 270), Squad("E2", 1, 6, 55, 50, 270)];
        RetainerSpawnDef[] retainers = [Leader("proud", "E1"), RetainerSpawnDef.InSquad("Gaut", 0, TraitDef.StandardOf("loyal"),
            TestDefs.Man, TestDefs.Level("elite"), Mail, "A")];

        static List<ulong> Run(BattleSimulation sim, int ticks)
        {
            var hashes = new List<ulong>();
            for (int t = 0; t < ticks; t++)
            {
                sim.Step();
                hashes.Add(sim.ComputeHash().Total);
            }

            return hashes;
        }

        var first = Battle(squads, ai: [0, 1], retainers: retainers, seed: 7);
        var second = Battle(squads, ai: [0, 1], retainers: retainers, seed: 7);
        var a = Run(first, 300);
        var b = Run(second, 300);
        var snapshot = first.TakeSnapshot();
        var restored = BattleSimulation.Restore(snapshot);
        Assert.Multiple(() =>
        {
            Assert.That(b, Is.EqualTo(a));
            Assert.That(first.State.Squads.Any(s => s.AiState != SquadAiState.Idle), Is.True, "the AI played");
            Assert.That(Run(restored, 400), Is.EqualTo(Run(first, 400)));
        });
    }
}
