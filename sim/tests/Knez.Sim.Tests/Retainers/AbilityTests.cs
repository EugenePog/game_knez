using Knez.Sim.Abilities;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Retainers;

/// <summary>
/// M3 step 11 (battle-sim.md §13): Retainers' active abilities. All six are defined; three are built (decided by the user
/// 2026-11-26): battle cry (proud) +100 to own men and −50 to enemies within 15 m, at once; close ranks (cautious) −30 ‰
/// from the front and ×0.7 pace for 10 s; stand to the death (loyal) no morale loss for 5 s; every cooldown 2 minutes. A
/// lasting ability stays with the squad if he leaves.
/// </summary>
[TestFixture]
public class AbilityTests
{
    private static readonly KitDef Mail = TestDefs.KitOf("sword-shield-mail");

    private static RetainerSpawnDef InSquad(string trait, string squad = "A", string name = "Hrafn", string level = "elite", int side = 0) =>
        RetainerSpawnDef.InSquad(name, side, TraitDef.StandardOf(trait), TestDefs.Man, TestDefs.Level(level), Mail, squad);

    private static RetainerSpawnDef Alone(string trait, int x, int y, string name = "Ulfr", int side = 0) =>
        RetainerSpawnDef.Alone(name, side, TraitDef.StandardOf(trait), TestDefs.Man, TestDefs.Level("elite"), Mail, FixVec2.FromInts(x, y), Angle.FromDegrees(90));

    /// <summary>A (side 0, 4 men) at (20, 15) and B (side 0, 4 men) at (40, 15) facing north; E (side 1, 4 men) facing
    /// south at (20, <paramref name="enemyY"/>).</summary>
    private static BattleState Battle(int enemyY = 50, params RetainerSpawnDef[] retainers) =>
        BattleSetup.Create(new ScenarioDef("test-abilities", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
        [
            TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90),
            TestDefs.Squad("B", 0, 4, FixVec2.FromInts(40, 15), 90),
            TestDefs.Squad("E", 1, 4, FixVec2.FromInts(20, enemyY), 270),
        ], retainers: retainers), 1);

    private static Squad SquadOf(BattleState state, string name) => state.Squads.Single(s => s.Name == name);

    private static Fighter[] MenOf(BattleState state, string squad) => SquadOf(state, squad).Members.Select(id => state.FindFighter(id)!).ToArray();

    private static Fighter RetainerOf(BattleState state, string name) =>
        state.Fighters.Single(f => f.Retainer.HasValue && f.Retainer.Value.Name == name);

    private static string? Reason(BattleState state, ICommand command) => command.Validate(state).Code;

    private static void Apply(BattleState state, ICommand command)
    {
        Assert.That(command.Validate(state).IsRejected, Is.False, command.Validate(state).ToString());
        command.Apply(state, new TickContext(state, new CommandQueue()));
    }

    [Test]
    public void BattleCry_OwnMenInReachGain_EnemiesInReachLose_OthersUntouched()
    {
        // E stands 10 m north of A: in reach; B is 20 m east: out of it.
        var state = Battle(enemyY: 25, retainers: InSquad("proud"));
        var hrafn = RetainerOf(state, "Hrafn");
        foreach (var man in state.Fighters)
        {
            man.Morale = 500;
        }

        MenOf(state, "A")[0].Morale = 950;
        Apply(state, new UseAbilityCommand(0, hrafn.Id));
        var changes = state.Events.OfType<MoraleChanged>().ToDictionary(c => c.Fighter);
        Assert.Multiple(() =>
        {
            Assert.That(state.Events.OfType<AbilityUsed>().Single().Ability, Is.EqualTo("battle-cry"));
            Assert.That(state.Events.OfType<AbilityUsed>().Single().Squad, Is.EqualTo(SquadOf(state, "A").Id));
            Assert.That(MenOf(state, "A").Select(m => m.Morale), Is.EqualTo(new[] { 1000, 600, 600, 600, 600 }), "him too; up to 1000");
            Assert.That(MenOf(state, "A").Select(m => changes[m.Id].Reason), Has.All.EqualTo(MoraleReason.BattleCry));
            Assert.That(MenOf(state, "E").Select(m => m.Morale), Has.All.EqualTo(450));
            Assert.That(MenOf(state, "E").Select(m => changes[m.Id].Reason), Has.All.EqualTo(MoraleReason.EnemyBattleCry));
            Assert.That(MenOf(state, "B").Select(m => m.Morale), Has.All.EqualTo(500), "20 m off: out of reach");
        });
    }

    [Test]
    public void Ability_CoolsDownTwoMinutes_RefusedUntilReady()
    {
        var sim = new BattleSimulation(Battle(retainers: InSquad("proud")));
        var hrafn = RetainerOf(sim.State, "Hrafn");
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        int used = sim.State.Tick;
        Assert.That(hrafn.Retainer!.Value.AbilityReadyTick, Is.EqualTo(used + (120 * BattleState.TicksPerSecond)));
        sim.StepTicks((120 * BattleState.TicksPerSecond) - 2);
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        Assert.That(sim.State.Events.OfType<CommandRejected>().Single().Reason.Code, Is.EqualTo("cooling-down"), "one tick short");
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        Assert.That(sim.State.Events.OfType<AbilityUsed>(), Has.Exactly(1).Items, "ready again");
    }

    [Test]
    public void CloseRanks_FrontalBlowsLessLikelyByItsShare_FlankUnchanged_ForTenSeconds()
    {
        var sim = new BattleSimulation(Battle(retainers: InSquad("cautious")));
        var state = sim.State;
        var hrafn = RetainerOf(state, "Hrafn");
        var man = MenOf(state, "A").First(m => !m.IsRetainer);
        var enemy = MenOf(state, "E")[0];
        int Chance(FixVec2 offset)
        {
            enemy.Position = man.Position + offset;
            return Blows.Chance(state, enemy, man, out _);
        }

        FixVec2 front = FixVec2.FromInts(0, 1);
        FixVec2 flank = FixVec2.FromInts(1, 0); // A faces north: east is its flank
        int frontBefore = Chance(front);
        int flankBefore = Chance(flank);
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(Chance(front), Is.EqualTo(frontBefore - 30));
            Assert.That(Chance(flank), Is.EqualTo(flankBefore));
            Assert.That(AbilityRules.Active(state, SquadOf(state, "A"), AbilityKind.CloseRanks), Is.Not.Null);
            Assert.That(AbilityRules.Active(state, SquadOf(state, "B"), AbilityKind.CloseRanks), Is.Null, "his squad only");
        });

        sim.StepTicks((10 * BattleState.TicksPerSecond) - 1);
        Assert.That(AbilityRules.Active(state, SquadOf(state, "A"), AbilityKind.CloseRanks), Is.Not.Null, "used at tick T, it acts on T..T+199");
        sim.Step();
        Assert.That(AbilityRules.Active(state, SquadOf(state, "A"), AbilityKind.CloseRanks), Is.Null, "over after 10 s");
        Assert.That(Chance(front), Is.EqualTo(frontBefore));
    }

    [Test]
    public void CloseRanks_TheSquadMarchesAtSevenTenthsOfItsPace()
    {
        Fix Speed(bool closed)
        {
            var sim = new BattleSimulation(Battle(retainers: InSquad("cautious")));
            var a = SquadOf(sim.State, "A");
            if (closed)
            {
                sim.Enqueue(new UseAbilityCommand(0, RetainerOf(sim.State, "Hrafn").Id));
            }

            sim.Enqueue(new MoveSquadCommand(0, a.Id, FixVec2.FromInts(20, 40)));
            sim.StepTicks(20);
            return a.Velocity.Length;
        }

        Fix open = Speed(false);
        Fix closed = Speed(true);
        Assert.That(open, Is.GreaterThan(Fix.Zero));
        Assert.That(closed.Raw, Is.EqualTo((open * Fix.Ratio(7, 10)).Raw).Within(4));
    }

    [Test]
    public void StandToTheDeath_ItsSquadLosesNoMorale_GainsStill_ForFiveSeconds()
    {
        var sim = new BattleSimulation(Battle(retainers: InSquad("loyal")));
        var state = sim.State;
        var hrafn = RetainerOf(state, "Hrafn");
        var man = MenOf(state, "A").First(m => !m.IsRetainer);
        var other = MenOf(state, "B")[0];
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        man.Morale = 500;
        other.Morale = 500;
        MoraleSystem.Change(state, man, -200, MoraleReason.SquadmateFell);
        MoraleSystem.Change(state, other, -200, MoraleReason.SquadmateFell);
        Assert.Multiple(() =>
        {
            Assert.That(man.Morale, Is.EqualTo(500), "dropped, not owed");
            Assert.That(other.Morale, Is.EqualTo(300), "another squad loses as ever");
        });
        MoraleSystem.Change(state, man, 50, MoraleReason.EnemySquadBroke);
        Assert.That(man.Morale, Is.EqualTo(550), "gains still count");

        sim.StepTicks(5 * BattleState.TicksPerSecond);
        int now = man.Morale;
        MoraleSystem.Change(state, man, -200, MoraleReason.SquadmateFell);
        Assert.That(man.Morale, Is.EqualTo(now - 160), "over after 5 s: the loss counts, cut by Hrafn's leadership (200 ‰)");
    }

    [Test]
    public void StandToTheDeath_InARealFight_NoLossReachesItsMenForFiveSeconds_ThenLossesAgain()
    {
        // Found in review: the unit test above calls Change directly; this one lets blows, wounds and mates' falls do it.
        BattleSimulation Fight()
        {
            var state = BattleSetup.Create(new ScenarioDef("test-stand-fight", "test", new MapDef(Fix.FromInt(60), Fix.FromInt(40)),
            [
                TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90),
                TestDefs.Squad("E", 1, 8, FixVec2.FromInts(20, 19), 270),
            ], retainers: [InSquad("loyal")]), 3);
            var sim = new BattleSimulation(state);
            sim.Enqueue(new MoveSquadCommand(0, SquadOf(state, "A").Id, FixVec2.FromInts(20, 19)));
            sim.Enqueue(new MoveSquadCommand(1, SquadOf(state, "E").Id, FixVec2.FromInts(20, 15)));
            return sim;
        }

        HashSet<EntityId> MenOfA(BattleSimulation sim) => SquadOf(sim.State, "A").Members.ToHashSet();
        bool ALost(BattleSimulation sim, HashSet<EntityId> a) => sim.State.Events.OfType<MoraleChanged>().Any(c => c.Delta < 0 && a.Contains(c.Fighter));

        // Without it: the first tick A's men lose morale.
        var baseline = Fight();
        var men = MenOfA(baseline);
        int first = -1;
        for (int t = 0; t < 1200 && first < 0; t++)
        {
            baseline.Step();
            first = ALost(baseline, men) ? baseline.State.Tick : -1;
        }

        Assert.That(first, Is.GreaterThan(0), "the fight costs A morale");

        // With it, used that very tick (same seed, same battle until then): nothing for 5 s, then losses again.
        var stand = Fight();
        stand.StepTicks(first - 1);
        stand.Enqueue(new UseAbilityCommand(0, RetainerOf(stand.State, "Hrafn").Id));
        int lostDuring = 0;
        for (int t = 0; t < 5 * BattleState.TicksPerSecond; t++)
        {
            stand.Step();
            lostDuring += ALost(stand, men) ? 1 : 0;
        }

        int after = -1;
        for (int t = 0; t < 1200 && after < 0; t++)
        {
            stand.Step();
            after = ALost(stand, men) ? stand.State.Tick : -1;
        }

        Assert.Multiple(() =>
        {
            Assert.That(lostDuring, Is.EqualTo(0), "no loss for ticks T..T+99");
            Assert.That(after, Is.GreaterThanOrEqualTo(first + 100), "losses again once it is over");
        });
    }

    [Test]
    public void BattleCry_ReachIsFifteenMetres_TheFallenLeftOut_RunningMatesIncluded()
    {
        var state = Battle(retainers: Alone("proud", 30, 30));
        var proud = RetainerOf(state, "Ulfr");
        var a = MenOf(state, "A");
        foreach (var man in state.Fighters)
        {
            man.Morale = 500;
        }

        a[0].Position = FixVec2.FromInts(30, 45); // exactly 15 m: in
        a[1].Position = new FixVec2(Fix.FromInt(30), Fix.FromInt(45) + Fix.Ratio(1, 100)); // 15.01 m: out
        a[2].Position = FixVec2.FromInts(31, 30);
        a[2].IsAlive = false;
        a[2].Wound = WoundLevel.Down;
        a[3].Position = FixVec2.FromInts(29, 30);
        a[3].MoraleState = MoraleState.Routing;
        Apply(state, new UseAbilityCommand(0, proud.Id));
        Assert.Multiple(() =>
        {
            Assert.That(a[0].Morale, Is.EqualTo(600));
            Assert.That(a[1].Morale, Is.EqualTo(500));
            Assert.That(a[2].Morale, Is.EqualTo(500), "the fallen are out of it");
            Assert.That(a[3].Morale, Is.EqualTo(600), "a running mate hears it too");
            Assert.That(proud.Morale, Is.EqualTo(600), "and he himself");
        });
    }

    [Test]
    public void BattleCry_CanRouteAnEnemyTheSameTick()
    {
        var sim = new BattleSimulation(Battle(enemyY: 25, retainers: InSquad("proud")));
        var enemy = MenOf(sim.State, "E")[0];
        enemy.Morale = 230; // wavering; −50 takes him below routing (200)
        enemy.MoraleState = MoraleState.Wavering;
        sim.Enqueue(new UseAbilityCommand(0, RetainerOf(sim.State, "Hrafn").Id));
        sim.Step();
        Assert.Multiple(() =>
        {
            Assert.That(enemy.MoraleState, Is.EqualTo(MoraleState.Routing));
            Assert.That(sim.State.Events.OfType<MoraleStateChanged>().Any(e => e.Fighter == enemy.Id && e.To == MoraleState.Routing), Is.True);
        });
    }

    [Test]
    public void SquadAbility_OnABrokenSquad_OrAlone_IsRefused()
    {
        var state = Battle(retainers: [InSquad("loyal"), Alone("cautious", 30, 30)]);
        Assert.That(Reason(state, new UseAbilityCommand(0, RetainerOf(state, "Ulfr").Id)), Is.EqualTo("not-in-squad"), "close ranks alone");
        var hrafn = RetainerOf(state, "Hrafn");
        SquadOf(state, "A").Broken = true;
        hrafn.MoraleState = MoraleState.Rallying;
        Assert.That(Reason(state, new UseAbilityCommand(0, hrafn.Id)), Is.EqualTo("squad-broken"), "a broken squad takes no orders");
    }

    [Test]
    public void LastingAbility_StaysWithTheSquad_WhenHeLeavesIt()
    {
        var state = Battle(retainers: InSquad("loyal"));
        var hrafn = RetainerOf(state, "Hrafn");
        Apply(state, new UseAbilityCommand(0, hrafn.Id));
        Apply(state, new AttachRetainerCommand(0, hrafn.Id, SquadOf(state, "B").Id));
        Assert.Multiple(() =>
        {
            Assert.That(AbilityRules.Active(state, SquadOf(state, "A"), AbilityKind.StandToTheDeath), Is.Not.Null, "A keeps it");
            Assert.That(AbilityRules.Active(state, SquadOf(state, "B"), AbilityKind.StandToTheDeath), Is.Null, "B doesn't get it");
            Assert.That(SquadOf(state, "A").Effects.Single().By, Is.EqualTo(hrafn.Id));
        });
    }

    [Test]
    public void UsedAgain_TheEffectStartsOver_OnePerKind()
    {
        var sim = new BattleSimulation(Battle(retainers: InSquad("cautious")));
        var hrafn = RetainerOf(sim.State, "Hrafn");
        var a = SquadOf(sim.State, "A");
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        int first = a.Effects.Single().Until;
        sim.StepTicks(120 * BattleState.TicksPerSecond);
        sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
        sim.Step();
        Assert.That(a.Effects, Has.Count.EqualTo(1));
        Assert.That(a.Effects[0].Until, Is.EqualTo(first + (120 * BattleState.TicksPerSecond) + 1));
    }

    [Test]
    public void Refusals_NotBuilt_SquadAbilityAlone_BattleCryAloneIsFine()
    {
        var state = Battle(retainers:
        [
            InSquad("greedy", name: "Greedy"), InSquad("bold", squad: "B", name: "Bold"), InSquad("cruel", squad: "E", name: "Cruel", side: 1),
            Alone("loyal", 30, 30, name: "Loyal"), Alone("proud", 32, 30, name: "Proud"),
        ]);
        Assert.Multiple(() =>
        {
            Assert.That(Reason(state, new UseAbilityCommand(0, RetainerOf(state, "Greedy").Id)), Is.EqualTo("not-built"));
            Assert.That(Reason(state, new UseAbilityCommand(0, RetainerOf(state, "Bold").Id)), Is.EqualTo("not-built"));
            Assert.That(Reason(state, new UseAbilityCommand(1, RetainerOf(state, "Cruel").Id)), Is.EqualTo("not-built"));
            Assert.That(Reason(state, new UseAbilityCommand(0, RetainerOf(state, "Loyal").Id)), Is.EqualTo("not-in-squad"));
            Assert.That(Reason(state, new UseAbilityCommand(0, RetainerOf(state, "Proud").Id)), Is.Null, "a cry needs no squad");
            Assert.That(Reason(state, new UseAbilityCommand(1, RetainerOf(state, "Proud").Id)), Is.EqualTo("wrong-side"));
        });
        var loyal = RetainerOf(state, "Loyal");
        loyal.MoraleState = MoraleState.Routing;
        Assert.That(Reason(state, new UseAbilityCommand(0, loyal.Id)), Is.EqualTo("routing"));
    }

    [Test]
    public void Script_Ability_RunsAsTheCommand()
    {
        var state = BattleSetup.Create(new ScenarioDef("test-script-ability", "test", new MapDef(Fix.FromInt(80), Fix.FromInt(60)),
            [TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 15), 90), TestDefs.Squad("E", 1, 4, FixVec2.FromInts(20, 50), 270)],
            [ScriptedOrderDef.Ability(2, "Hrafn")], retainers: [InSquad("loyal")]), 1);
        var sim = new BattleSimulation(state);
        sim.StepTicks(2);
        Assert.That(state.Events.OfType<AbilityUsed>().Single().Retainer, Is.EqualTo(RetainerOf(state, "Hrafn").Id));
    }

    [Test]
    public void Hash_CoversTheReadyTickAndTheSquadsEffects()
    {
        var state = Battle(retainers: InSquad("cautious"));
        var hrafn = RetainerOf(state, "Hrafn");
        var before = StateHasher.Compute(state);
        hrafn.Retainer = hrafn.Retainer!.Value.WithAbilityReadyTick(7);
        var ready = StateHasher.Compute(state);
        SquadOf(state, "A").EffectList.Add(new AbilityEffect(TraitDef.StandardOf("cautious").Ability, 9, hrafn.Id));
        var effect = StateHasher.Compute(state);
        Assert.Multiple(() =>
        {
            Assert.That(ready.Fighters, Is.Not.EqualTo(before.Fighters));
            Assert.That(effect.Squads, Is.Not.EqualTo(ready.Squads));
        });
    }

    [Test]
    [Category("Determinism")]
    public void Determinism_SnapshotRestore_WithAnEffectRunning_ContinuesIdentically()
    {
        var sim = new BattleSimulation(Battle(enemyY: 25, retainers: InSquad("cautious")));
        sim.Enqueue(new UseAbilityCommand(0, RetainerOf(sim.State, "Hrafn").Id));
        sim.Enqueue(new MoveSquadCommand(0, SquadOf(sim.State, "A").Id, FixVec2.FromInts(20, 22)));
        sim.StepTicks(40);
        var twin = new BattleSimulation(sim.State.Clone());
        sim.StepTicks(300);
        twin.StepTicks(300);
        Assert.That(twin.ComputeHash().Total, Is.EqualTo(sim.ComputeHash().Total));
    }
}
