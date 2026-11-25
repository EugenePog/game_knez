using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Defs;

/// <summary>The combat rules (battle-sim.md §11, M3 step 1): catalogues, the hit-chance matrix, the named kits (step 9), checks.</summary>
[TestFixture]
public class CombatDefTests
{
    private static readonly CombatDef S = CombatDef.Standard;

    private static int[][] Matrix() => S.Weapons.Select(w => S.ExperienceLevels.Select(e => S.BaseHit(w, e)).ToArray()).ToArray();

    /// <summary>The standard rules with one part replaced.</summary>
    private static CombatDef With(int[][]? hitChance = null, IReadOnlyList<WeaponDef>? weapons = null, IReadOnlyList<KitDef>? kits = null,
        int minHit = 20, int maxHit = 950) =>
        new CombatDef(weapons ?? S.Weapons, S.Shields, S.BodyArmours, S.HeadCovers, S.ExperienceLevels, kits ?? S.Kits,
            hitChance ?? Matrix(), minHit, maxHit, S.FlankBonus, S.RearBonus, S.ShieldFromFlank,
            S.ShieldFromRear, S.ShieldWallBonus, S.LightWoundPenalty, S.HeavyWoundPenalty, S.LightWoundPace, S.HeavyWoundPace,
            S.FrontArc, S.RearArc, S.StrikeArc);

    [Test]
    public void BaseHit_Standard_ReadsWeaponRowAndExperienceColumn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(S.BaseHit(S.FindWeapon("sword")!, S.FindExperience("new")!), Is.EqualTo(420));
            Assert.That(S.BaseHit(S.FindWeapon("sword")!, S.FindExperience("elite")!), Is.EqualTo(660));
            Assert.That(S.BaseHit(S.FindWeapon("spear")!, S.FindExperience("experienced")!), Is.EqualTo(460));
            Assert.That(S.BaseHit(S.FindWeapon("seax")!, S.FindExperience("professional")!), Is.EqualTo(500));
        });
    }

    [Test]
    public void BaseHit_DefFromAnotherCatalogue_Throws()
    {
        var stranger = new ExperienceDef("elite", 120);
        Assert.Throws<ArgumentException>(() => S.BaseHit(S.Weapons[1], stranger));
    }

    [Test]
    public void Standard_CataloguesInFileOrder_AndTheSevenKits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(S.Weapons.Select(w => w.Id), Is.EqualTo(new[] { "sword", "axe", "spear", "seax" }));
            Assert.That(S.ExperienceLevels.Select(e => e.Id), Is.EqualTo(new[] { "new", "experienced", "professional", "elite" }));
            Assert.That(S.Shields.Select(s => s.Id), Is.EqualTo(new[] { "none", "round" }));
            Assert.That(S.BodyArmours.Select(s => s.Id), Is.EqualTo(new[] { "none", "padded", "mail" }));
            Assert.That(S.HeadCovers.Select(s => s.Id), Is.EqualTo(new[] { "none", "cap", "helm" }));

            // M3 step 9 (decided 2026-11-25): seven kits named by what they hold, every one with a round shield.
            Assert.That(S.Kits.Select(k => $"{k.Id}={k.Weapon.Id}/{k.Shield.Id}/{k.BodyArmour.Id}/{k.HeadCover.Id}"), Is.EqualTo(new[]
            {
                "axe-shield=axe/round/none/none", "spear-shield=spear/round/none/none",
                "axe-shield-padded=axe/round/padded/cap", "spear-shield-padded=spear/round/padded/cap",
                "sword-shield-padded=sword/round/padded/cap", "sword-shield-mail=sword/round/mail/helm",
                "axe-shield-mail=axe/round/mail/helm",
            }));
            Assert.That(S.Kits.All(S.Contains), Is.True);
            Assert.That(S.FindKit("sword-shield-padded"), Is.SameAs(S.Kits[4]));
            Assert.That(S.FindKit("sword-padded"), Is.Null);
        });
    }

    [Test]
    public void Standard_PenetrationAndDefence_AsDecided()
    {
        // Penetration decided by the user 2026-11-25 (the milder set); the spear's 30 was the spearman warrior type's.
        Assert.That(S.Weapons.Select(w => (w.Id, w.Penetration, w.Defence)), Is.EqualTo(new[]
        {
            ("sword", 0, 0), ("axe", 300, 0), ("spear", 200, 30), ("seax", 100, 0),
        }));
    }

    [Test]
    public void WeaponDef_TimesBecomeWholeTicks()
    {
        var sword = S.FindWeapon("sword")!;
        Assert.Multiple(() =>
        {
            Assert.That(sword.WindupTicks, Is.EqualTo(14), "0.7 s");
            Assert.That(sword.RecoveryTicks, Is.EqualTo(24), "1.2 s");
            Assert.That(sword.Reach, Is.EqualTo(Fix.Ratio(1, 2)));
            Assert.That(sword.LightWound + sword.HeavyWound + sword.DownWound, Is.EqualTo(1000));
        });
    }

    [Test]
    public void WeaponDef_WoundRowNotSummingToAThousand_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new WeaponDef("w", Fix.Ratio(1, 2), Fix.Ratio(7, 10), Fix.Ratio(8, 10), 600, 300, 50, 0, 0));
        Assert.That(ex!.Message, Does.Contain("light + heavy + down must be 1000").And.Contain("950"));
    }

    [Test]
    public void Constructor_MatrixMissingAWeaponRow_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => With(hitChance: Matrix().Take(3).ToArray()));
        Assert.That(ex!.Message, Does.Contain("hitChance has 3 rows; it needs one per weapon (4)"));
    }

    [Test]
    public void Constructor_MatrixRowShort_ThrowsNamingTheWeapon()
    {
        var matrix = Matrix();
        matrix[2] = matrix[2].Take(3).ToArray();
        var ex = Assert.Throws<ArgumentException>(() => With(hitChance: matrix));
        Assert.That(ex!.Message, Does.Contain("hitChance 'spear' has 3 values; it needs one per experience level (4)"));
    }

    [Test]
    public void Constructor_MatrixValueOutOfRange_Throws()
    {
        var matrix = Matrix();
        matrix[0][1] = 1001;
        var ex = Assert.Throws<ArgumentException>(() => With(hitChance: matrix));
        Assert.That(ex!.Message, Does.Contain("hitChance 'sword' 'experienced' must be 0..1000 (got 1001)"));
    }

    [Test]
    public void Constructor_DuplicateWeaponId_Throws()
    {
        var weapons = S.Weapons.Append(new WeaponDef("axe", Fix.One, Fix.One, Fix.One, 500, 300, 200, 0, 0)).ToArray();
        var matrix = Matrix().Append(new[] { 1, 2, 3, 4 }).ToArray();
        var ex = Assert.Throws<ArgumentException>(() => With(hitChance: matrix, weapons: weapons));
        Assert.That(ex!.Message, Does.Contain("weapon 'axe' is listed twice"));
    }

    [Test]
    public void Constructor_HitLimitsCrossed_Throws()
    {
        Assert.Throws<ArgumentException>(() => With(minHit: 500, maxHit: 400));
        Assert.Throws<ArgumentException>(() => With(maxHit: 1001));
    }

    [Test]
    public void Constructor_KitFromOtherCatalogues_Throws()
    {
        var stranger = new KitDef("odd", new WeaponDef("sword", Fix.One, Fix.One, Fix.One, 500, 300, 200, 0, 0), S.Shields[1],
            S.BodyArmours[1], S.HeadCovers[1]);
        var ex = Assert.Throws<ArgumentException>(() => With(kits: S.Kits.Append(stranger).ToArray()));
        Assert.That(ex!.Message, Does.Contain("kit 'odd'").And.Contain("weapon 'sword'"));
    }

    [Test]
    public void Constructor_KitListedTwice_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => With(kits: S.Kits.Append(S.Kits[0]).ToArray()));
        Assert.That(ex!.Message, Does.Contain("kit 'axe-shield' is listed twice"));
    }

    [Test]
    public void Fingerprint_ChangesWithOneMatrixCell()
    {
        var matrix = Matrix();
        matrix[1][3] += 1;
        Assert.Multiple(() =>
        {
            Assert.That(With().Fingerprint, Is.EqualTo(S.Fingerprint), "same numbers, same fingerprint");
            Assert.That(With(hitChance: matrix).Fingerprint, Is.Not.EqualTo(S.Fingerprint));
        });
    }

    [Test]
    public void ScenarioDef_KitOrExperienceFromOtherRules_Throws()
    {
        // Rules whose kits and levels are other objects with the same ids: a squad must use the battle's own.
        var copies = S.Kits.Select(k => new KitDef(k.Id, k.Weapon, k.Shield, k.BodyArmour, k.HeadCover)).ToArray();
        var other = With(kits: copies);
        var squad = Support.TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 20), 90);
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", Support.TestDefs.Map, [squad], combat: other));
        Assert.That(ex!.Message, Does.Contain("squads[0] 'A' men[0]: kit 'sword-shield-padded' is not the combat rules' def"));

        var level = new ExperienceDef("experienced", 40, 750);
        var odd = new SquadSpawnDef("B", 0, Support.TestDefs.Man, [new SquadMenDef(4, level, S.Kits[4])], Support.TestDefs.Line,
            FixVec2.FromInts(20, 20), Angle.FromDegrees(90));
        var ex2 = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", Support.TestDefs.Map, [odd]));
        Assert.That(ex2!.Message, Does.Contain("squads[0] 'B' men[0]: experience level 'experienced' is not the combat rules' def"));
    }

    [Test]
    public void ScenarioDef_Fingerprint_IncludesCombatRules()
    {
        var squad = Support.TestDefs.Squad("A", 0, 4, FixVec2.FromInts(20, 20), 90);
        var standard = new ScenarioDef("s", "", Support.TestDefs.Map, [squad]);
        var tuned = new ScenarioDef("s", "", Support.TestDefs.Map, [squad], combat: With(maxHit: 900));
        Assert.Multiple(() =>
        {
            Assert.That(standard.Combat, Is.SameAs(S));
            Assert.That(tuned.Fingerprint, Is.Not.EqualTo(standard.Fingerprint));
        });
    }

    /// <summary>Every number and id of the rules reaches the fingerprint (StateCoverageTests lists the fields).</summary>
    [Test]
    public void Fingerprint_ChangesWithEveryKindOfField()
    {
        var w = S.Weapons.ToArray();
        CombatDef Rules(IReadOnlyList<WeaponDef>? weapons = null, IReadOnlyList<ProtectionDef>? shields = null,
            IReadOnlyList<ExperienceDef>? levels = null, IReadOnlyList<KitDef>? kits = null,
            IReadOnlyList<ProtectionDef>? body = null, IReadOnlyList<ProtectionDef>? head = null, int minHit = 20,
            int flank = 100, int rear = 200, int fromFlank = 500, int fromRear = 0, int wall = 80, int light = 40, int heavy = 120,
            Fix? lightPace = null, Fix? heavyPace = null, Fix? frontArc = null, Fix? rearArc = null, Fix? strikeArc = null)
        {
            var newShields = shields ?? S.Shields;
            var newLevels = levels ?? S.ExperienceLevels;
            var newWeapons = weapons ?? S.Weapons;
            var newBody = body ?? S.BodyArmours;
            var newHead = head ?? S.HeadCovers;

            // Kits must hold these rules' very parts: the same id where there is one, else the same place in the list.
            T Same<T>(IReadOnlyList<T> now, IReadOnlyList<T> was, T part, Func<T, string> id) =>
                now.FirstOrDefault(x => id(x) == id(part)) ?? now[was.ToList().IndexOf(part)];
            var newKits = (kits ?? S.Kits).Select(k => new KitDef(k.Id, Same(newWeapons, S.Weapons, k.Weapon, x => x.Id),
                Same(newShields, S.Shields, k.Shield, x => x.Id), Same(newBody, S.BodyArmours, k.BodyArmour, x => x.Id),
                Same(newHead, S.HeadCovers, k.HeadCover, x => x.Id))).ToArray();
            return new CombatDef(newWeapons, newShields, newBody, newHead, newLevels, newKits, Matrix(), minHit, 950,
                flank, rear, fromFlank, fromRear, wall, light, heavy, lightPace ?? S.LightWoundPace, heavyPace ?? S.HeavyWoundPace,
                frontArc ?? S.FrontArc, rearArc ?? S.RearArc, strikeArc ?? S.StrikeArc);
        }

        WeaponDef[] Swap(WeaponDef replacement) => w.Select(x => x.Id == replacement.Id ? replacement : x).ToArray();
        var variants = new Dictionary<string, CombatDef>
        {
            ["reach"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.One, Fix.Ratio(9, 10), Fix.Ratio(3, 2), 500, 330, 170, 300, 0))),
            ["windup"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.Ratio(1, 2), Fix.One, Fix.Ratio(3, 2), 500, 330, 170, 300, 0))),
            ["recovery"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.Ratio(1, 2), Fix.Ratio(9, 10), Fix.One, 500, 330, 170, 300, 0))),
            ["penetration"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.Ratio(1, 2), Fix.Ratio(9, 10), Fix.Ratio(3, 2), 500, 330, 170, 301, 0))),
            ["defence"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.Ratio(1, 2), Fix.Ratio(9, 10), Fix.Ratio(3, 2), 500, 330, 170, 300, 1))),
            ["weapon id"] = Rules(weapons: w.Select(x => x.Id == "axe" ? new WeaponDef("hatchet", Fix.Ratio(1, 2), Fix.Ratio(9, 10), Fix.Ratio(3, 2), 500, 330, 170, 300, 0) : x).ToArray()),
            ["weapon order"] = Rules(weapons: [w[0], w[2], w[1], w[3]]),
            ["body armour"] = Rules(body: [S.BodyArmours[0], S.BodyArmours[1], new ProtectionDef("mail", 141)]),
            ["head cover"] = Rules(head: [S.HeadCovers[0], S.HeadCovers[1], new ProtectionDef("helm", 61)]),
            ["minHit"] = Rules(minHit: 21),
            ["wound row"] = Rules(weapons: Swap(new WeaponDef("axe", Fix.Ratio(1, 2), Fix.Ratio(9, 10), Fix.Ratio(3, 2), 510, 320, 170, 300, 0))),
            ["shield"] = Rules(shields: [S.Shields[0], new ProtectionDef("round", 221)]),
            ["experience"] = Rules(levels: S.ExperienceLevels.Select(e => e.Id == "elite" ? new ExperienceDef("elite", 121) : e).ToArray()),
            ["kit part"] = Rules(kits: S.Kits.Select(k => k.Id == "axe-shield" ? new KitDef(k.Id, S.Weapons[2], k.Shield, k.BodyArmour, k.HeadCover) : k).ToArray()),
            ["kit id"] = Rules(kits: S.Kits.Select(k => k.Id == "axe-shield" ? new KitDef("axe-round", k.Weapon, k.Shield, k.BodyArmour, k.HeadCover) : k).ToArray()),
            ["kit order"] = Rules(kits: S.Kits.Reverse().ToArray()),
            ["flank"] = Rules(flank: 101), ["rear"] = Rules(rear: 201), ["fromFlank"] = Rules(fromFlank: 501),
            ["fromRear"] = Rules(fromRear: 1), ["wall"] = Rules(wall: 81), ["light"] = Rules(light: 41), ["heavy"] = Rules(heavy: 121),
            ["frontArc"] = Rules(frontArc: Fix.FromInt(45)), ["rearArc"] = Rules(rearArc: Fix.FromInt(135)),
            ["strikeArc"] = Rules(strikeArc: Fix.FromInt(90)),
            ["lightPace"] = Rules(lightPace: Fix.Ratio(1, 2)), ["heavyPace"] = Rules(heavyPace: Fix.Ratio(1, 2)),
        };
        Assert.Multiple(() =>
        {
            foreach (var (name, rules) in variants)
            {
                Assert.That(rules.Fingerprint, Is.Not.EqualTo(S.Fingerprint), name);
            }
        });
    }
}
