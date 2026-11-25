using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Combat;

/// <summary>
/// M3 step 3 (battle-sim.md §11): blows — windup, strike, recovery; the chance a blow gets through from the matrix,
/// protection, sector (decided 2026-11-23: front ±60°, flank to 120°, rear beyond) and shield wall; engaged men turn to
/// face their enemy (decided 2026-11-23) and strike once he is within the strike arc.
/// </summary>
[TestFixture]
public class BlowTests
{
    private static readonly CombatDef C = CombatDef.Standard;

    private static readonly FormationDef ShieldWall = new("test-shieldwall", Fix.Ratio(3, 4), Fix.One, ranks: 2, width: 0,
        turnRate: Fix.FromInt(30), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3), shieldWall: true);

    /// <summary>An elite man in mail and helm with sword and round shield.</summary>
    private static SquadMenDef Armoured { get; } = TestDefs.Men(1, TestDefs.Level("elite"), TestDefs.KitOf("sword-shield-mail"))[0];

    /// <summary>Lone men (squads of one) at points in centimetres, each facing the given way.</summary>
    private static BattleSimulation Men(params (int side, int xCm, int yCm, int facing, SquadMenDef? def, FormationDef? formation)[] men)
    {
        var squads = men.Select((m, i) => new SquadSpawnDef($"m{i}", m.side, TestDefs.Man, [m.def ?? TestDefs.Men(1)[0]],
            m.formation ?? TestDefs.Line, new FixVec2(Fix.Ratio(m.xCm, 100), Fix.Ratio(m.yCm, 100)),
            Angle.FromDegrees(m.facing))).ToArray();
        var formations = men.Select(m => m.formation ?? TestDefs.Line).Distinct().ToArray();
        return new BattleSimulation(BattleSetup.Create(new ScenarioDef("test-men", "test", TestDefs.Map, squads, formations: formations), seed: 1));
    }

    private static Fighter Man(BattleSimulation sim, int index) => sim.State.FighterList[index];

    private static int Chance(BattleSimulation sim, int attacker, int defender, out BlowSector sector) =>
        Blows.Chance(sim.State, Man(sim, attacker), Man(sim, defender), out sector);

    [Test]
    public void Chance_FrontFlankRear_FollowTheTable()
    {
        // Experienced swordsman (500) against an experienced man with round shield, padded coat and cap: 220 + 70 + 30 + 40.
        var front = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, null, null));
        var flank = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 0, null, null));
        var rear = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 90, null, null));
        Assert.Multiple(() =>
        {
            Assert.That(Chance(front, 0, 1, out var s1), Is.EqualTo(500 - 220 - 70 - 30 - 40));
            Assert.That(s1, Is.EqualTo(BlowSector.Front));
            Assert.That(Chance(flank, 0, 1, out var s2), Is.EqualTo(500 - 110 - 70 - 30 - 40 + 100), "half the shield, +100");
            Assert.That(s2, Is.EqualTo(BlowSector.Flank));
            Assert.That(Chance(rear, 0, 1, out var s3), Is.EqualTo(500 - 0 - 70 - 30 - 40 + 200), "no shield, +200");
            Assert.That(s3, Is.EqualTo(BlowSector.Rear));
        });
    }

    [Test]
    public void Chance_AttackersWeaponPenetrates_DefendersWeaponDefends()
    {
        // M3 step 9: the attacker's weapon penetration takes its share off the defender's armour and head cover; the
        // defender's own weapon adds its defence (the spear's 30).
        var spear = TestDefs.Men(1, kit: TestDefs.KitOf("spear-shield-padded"))[0];
        var newAxe = TestDefs.Men(1, TestDefs.Level("new"), TestDefs.KitOf("axe-shield"))[0];
        var spearAttacks = Men((0, 2000, 1000, 90, spear, null), (1, 2000, 1100, 270, null, null));
        var swordAttacksSpear = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, spear, null));
        var axeOnTheFlank = Men((0, 2000, 1000, 90, newAxe, null), (1, 2000, 1100, 0, Armoured, null));
        Assert.Multiple(() =>
        {
            Assert.That(Chance(spearAttacks, 0, 1, out _), Is.EqualTo(460 - 220 - (100 * 800 / 1000) - 40),
                "spear 200: padded coat and cap count 80 of 100");
            Assert.That(Chance(swordAttacksSpear, 0, 1, out _), Is.EqualTo(500 - 220 - 100 - 40 - 30),
                "the defender's spear keeps the sword off: −30; the sword penetrates nothing");
            Assert.That(Chance(axeOnTheFlank, 0, 1, out var sector), Is.EqualTo(400 - 110 - (200 * 700 / 1000) - 120 + 100),
                "axe 300 against mail and helm: 140 of 200 count");
            Assert.That(sector, Is.EqualTo(BlowSector.Flank));
        });
    }

    [TestCase(60, BlowSector.Front)]
    [TestCase(-60, BlowSector.Front)]
    [TestCase(120, BlowSector.Flank)]
    [TestCase(30, BlowSector.Front)]
    [TestCase(59, BlowSector.Front)]
    [TestCase(61, BlowSector.Flank)]
    [TestCase(119, BlowSector.Flank)]
    [TestCase(121, BlowSector.Rear)]
    [TestCase(-100, BlowSector.Flank)]
    [TestCase(-150, BlowSector.Rear)]
    public void Sector_ByAngleFromTheDefendersFacing(int offDegrees, BlowSector expected)
    {
        // The attacker stands due south of the defender (270°); the defender faces 270° + offDegrees.
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270 + offDegrees, null, null));
        Chance(sim, 0, 1, out var sector);
        Assert.That(sector, Is.EqualTo(expected));
    }

    [Test]
    public void Chance_ShieldWall_FrontOnly_AndNeedsAShield()
    {
        var wall = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, null, ShieldWall));
        var wallFromFlank = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 0, null, ShieldWall));
        Assert.Multiple(() =>
        {
            Assert.That(Chance(wall, 0, 1, out _), Is.EqualTo(140 - 80));
            Assert.That(Chance(wallFromFlank, 0, 1, out _), Is.EqualTo(350), "a shield wall helps only from the front");
        });
    }

    [Test]
    public void Chance_KeptWithinTheLimits()
    {
        // Elite in mail and helm with a round shield: 500 − 220 − 140 − 60 − 120 = −40 → the 20 ‰ floor.
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, Armoured, null));
        Assert.That(Chance(sim, 0, 1, out _), Is.EqualTo(C.MinHit));
    }

    private static List<BlowStruck> RunCollecting(BattleSimulation sim, int ticks)
    {
        var blows = new List<BlowStruck>();
        for (int i = 0; i < ticks; i++)
        {
            sim.Step();
            blows.AddRange(sim.State.Events.OfType<BlowStruck>());
        }

        return blows;
    }

    [Test]
    public void Blows_FacingPair_StrikeEveryWindupPlusRecovery()
    {
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, null, null));
        var blows = RunCollecting(sim, 100); // three cycles of 0.7 + 1.2 s
        var sword = C.FindWeapon("sword")!;
        int first = 1 + sword.WindupTicks; // engaged at tick 1, the windup starts that tick
        int cycle = sword.WindupTicks + sword.RecoveryTicks;
        Assert.Multiple(() =>
        {
            Assert.That(blows.Where(b => b.Attacker == Man(sim, 0).Id).Select(b => b.Tick), Is.EqualTo(new[] { first, first + cycle, first + (2 * cycle) }));
            Assert.That(blows.Where(b => b.Attacker == Man(sim, 1).Id).Select(b => b.Tick), Is.EqualTo(new[] { first, first + cycle, first + (2 * cycle) }));
            Assert.That(blows.All(b => b.Chance == 140 && b.Sector == BlowSector.Front), Is.True);
        });
    }

    [Test]
    public void Blows_OutOfReach_NoneStruck()
    {
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1200, 270, null, null)); // gap 1.4 m
        Assert.That(RunCollecting(sim, 60), Is.Empty);
    }

    [Test]
    public void Blows_EnemyBehind_ManTurnsToFaceHim_ThenStrikes()
    {
        // Man 0 faces north; man 1 stands behind him facing north too — at his back.
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 900, 90, null, null));
        var blows = RunCollecting(sim, 2 * BattleState.TicksPerSecond);
        int firstBack = blows.First(b => b.Attacker == Man(sim, 0).Id).Tick;
        int firstIn = blows.First(b => b.Attacker == Man(sim, 1).Id).Tick;
        Angle towardEnemy = Angle.FromDegrees(270);
        int off = Man(sim, 0).Facing.DeltaTo(towardEnemy);
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(off), Is.LessThanOrEqualTo(Man(sim, 0).Body.TurnPerTick), "he turned to face his enemy");
            Assert.That(firstBack, Is.GreaterThan(firstIn), "turning cost him the first blow");
        });
    }

    [Test]
    public void Blows_SameSeed_SameRolls_OtherSeed_OtherRolls()
    {
        string Rolls(ulong seed)
        {
            var scenario = new ScenarioDef("test-pair", "test", TestDefs.Map,
            [
                TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90),
                TestDefs.Squad("b", 1, 1, FixVec2.FromInts(20, 11), 270),
            ]);
            var sim = new BattleSimulation(BattleSetup.Create(scenario, seed));
            return string.Concat(RunCollecting(sim, 60 * BattleState.TicksPerSecond).Select(b => b.Landed ? '1' : '0'));
        }

        string first = Rolls(5);
        Assert.Multiple(() =>
        {
            Assert.That(Rolls(5), Is.EqualTo(first));
            Assert.That(Rolls(6), Is.Not.EqualTo(first));
        });
    }

    [Test]
    [Category("Slow")]
    public void Blows_ManyBlows_LandAtTheirChance()
    {
        // Duels over 80 seeds (each ends when one man falls, step 4): the blows that land match the sum of their chances.
        int blows = 0;
        int landed = 0;
        double expected = 0;
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var scenario = new ScenarioDef("test-pair", "test", TestDefs.Map,
            [
                TestDefs.Squad("a", 0, 1, FixVec2.FromInts(20, 10), 90),
                TestDefs.Squad("b", 1, 1, FixVec2.FromInts(20, 11), 270),
            ]);
            var struck = RunCollecting(new BattleSimulation(BattleSetup.Create(scenario, seed)), 300 * BattleState.TicksPerSecond);
            blows += struck.Count;
            landed += struck.Count(b => b.Landed);
            expected += struck.Sum(b => b.Chance / 1000.0); // wounded attackers strike worse, so sum each blow's own chance
        }

        double sigma = Math.Sqrt(expected);
        Assert.Multiple(() =>
        {
            Assert.That(blows, Is.GreaterThan(800), "enough blows to judge");
            Assert.That(landed, Is.InRange(expected - (4 * sigma), expected + (4 * sigma)), $"{landed} of {blows}, expected {expected:0}");
        });
    }

    [Test]
    public void Blows_TargetGoneDuringWindup_BlowLost_NoRoll_StillRecovers()
    {
        var sim = Men((0, 2000, 1000, 90, null, null), (1, 2000, 1100, 270, null, null));
        sim.Step(); // tick 1: both engaged, both winding up
        var attacker = Man(sim, 0);
        var defender = Man(sim, 1);
        Assume.That(attacker.Action, Is.EqualTo(FighterAction.Windup));
        defender.Position = new FixVec2(defender.Position.X, defender.Position.Y + Fix.FromInt(3)); // he steps away
        defender.Action = FighterAction.Idle; // and drops his own blow, so only the attacker's could roll
        ulong rngBefore = sim.State.Rng(RngStream.Combat).State;
        var blows = RunCollecting(sim, attacker.Kit.Weapon.WindupTicks);
        Assert.Multiple(() =>
        {
            Assert.That(blows, Is.Empty, "no blow struck at nobody");
            Assert.That(sim.State.Rng(RngStream.Combat).State, Is.EqualTo(rngBefore), "a lost blow rolls nothing");
            Assert.That(attacker.Action, Is.EqualTo(FighterAction.Recovery), "but he still recovers");
        });
    }
}
