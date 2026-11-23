using System.Reflection;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Determinism;

/// <summary>
/// Guards the new-state checklist (sim-feature skill §4). When one of these fails because you added a field:
/// add it to <see cref="StateHasher"/> and to the type's Clone, cover it in a determinism test, then list it here.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class StateCoverageTests
{
    private static readonly Dictionary<Type, string[]> KnownFields = new()
    {
        [typeof(BattleState)] = ["<Scenario>k__BackingField", "<Seed>k__BackingField", "<Tick>k__BackingField", "<Winner>k__BackingField", "_events",
            "_fighters", "_nextEntityId", "_rng", "_squads"],
        [typeof(Squad)] = ["<Anchor>k__BackingField", "<Facing>k__BackingField", "<Files>k__BackingField", "<Formation>k__BackingField", "<Frontage>k__BackingField",
            "<Id>k__BackingField", "<Name>k__BackingField", "<Order>k__BackingField", "<PathIndex>k__BackingField",
            "<PathRequestTick>k__BackingField", "<Reforming>k__BackingField", "<RegroupSince>k__BackingField", "<SideId>k__BackingField", "<Spin>k__BackingField",
            "<Velocity>k__BackingField", "<Withdrawing>k__BackingField", "<StartCount>k__BackingField", "<Broken>k__BackingField", "_members", "_path", "_pathFiles"],
        [typeof(Fighter)] = ["<Action>k__BackingField", "<ActionTicks>k__BackingField", "<Def>k__BackingField", "<Facing>k__BackingField", "<Id>k__BackingField", "<IsAlive>k__BackingField",
            "<PassingThrough>k__BackingField", "<Position>k__BackingField", "<SideId>k__BackingField", "<SlotIndex>k__BackingField",
            "<SquadId>k__BackingField", "<Target>k__BackingField", "<Velocity>k__BackingField", "<Wound>k__BackingField", "<Morale>k__BackingField", "<MoraleState>k__BackingField"],
        [typeof(SimRng)] = ["_increment", "_state"],
        [typeof(CommandQueue)] = ["_log", "_nextSequence", "_pending"],
    };

    /// <summary>
    /// Def types are shared, not hashed field by field: their numbers reach the state hash only through
    /// <see cref="ScenarioDef.Fingerprint"/>. A new def field must go into the def's <c>AddTo</c> (and its loader).
    /// </summary>
    private static readonly Dictionary<Type, string[]> KnownDefFields = new()
    {
        [typeof(FighterDef)] = ["<Id>k__BackingField", "<Kit>k__BackingField", "<Radius>k__BackingField", "<RunSpeed>k__BackingField",
            "<TurnRate>k__BackingField", "<WalkSpeed>k__BackingField"],
        [typeof(FormationDef)] = ["<AllyPassSpeed>k__BackingField", "<Id>k__BackingField", "<RankSpacing>k__BackingField", "<Ranks>k__BackingField",
            "<HoldWeight>k__BackingField", "<ShieldWall>k__BackingField", "<Spacing>k__BackingField", "<TurnRate>k__BackingField", "<Width>k__BackingField"],
        [typeof(ScriptedOrderDef)] = ["<Facing>k__BackingField", "<FormationId>k__BackingField", "<SquadName>k__BackingField",
            "<Target>k__BackingField", "<Tick>k__BackingField"],
        [typeof(MapDef)] = ["<Height>k__BackingField", "<Walls>k__BackingField", "<Width>k__BackingField"],
        [typeof(WallDef)] = ["<Max>k__BackingField", "<Min>k__BackingField"],
        [typeof(SquadSpawnDef)] = ["<Count>k__BackingField", "<Facing>k__BackingField", "<Fighter>k__BackingField",
            "<Formation>k__BackingField", "<Name>k__BackingField", "<Position>k__BackingField", "<SideId>k__BackingField"],
        [typeof(ScenarioDef)] = ["<Combat>k__BackingField", "<Morale>k__BackingField", "<Description>k__BackingField", "<Fingerprint>k__BackingField", "<Id>k__BackingField",
            "<Formations>k__BackingField", "<Map>k__BackingField", "<Movement>k__BackingField", "<Script>k__BackingField",
            "<Squads>k__BackingField"],
        [typeof(MovementDef)] = ["<AllyPartingDepth>k__BackingField", "<CollisionPasses>k__BackingField",
            "<FormedUpReach>k__BackingField", "<HoldingReach>k__BackingField", "<PathBudget>k__BackingField",
            "<PersonalSpace>k__BackingField", "<PivotTolerance>k__BackingField", "<RegroupCost>k__BackingField",
            "<ReformPace>k__BackingField", "<RegroupLead>k__BackingField", "<RegroupMaxWaitTicks>k__BackingField", "<SeparationTicks>k__BackingField", "<StepBackPace>k__BackingField", "<WithdrawClearance>k__BackingField",
            "<SlotCatchUpTicks>k__BackingField"],
        [typeof(WeaponDef)] = ["<DownWound>k__BackingField", "<HeavyWound>k__BackingField", "<Id>k__BackingField",
            "<LightWound>k__BackingField", "<Reach>k__BackingField", "<RecoveryTicks>k__BackingField", "<WindupTicks>k__BackingField"],
        [typeof(MoraleDef)] = ["<AllySquadBroke>k__BackingField", "<BreakShare>k__BackingField", "<EnemySquadBroke>k__BackingField",
            "<Fingerprint>k__BackingField", "<FlankBlow>k__BackingField", "<Hysteresis>k__BackingField", "<LossWeight>k__BackingField",
            "<MateFlankBlow>k__BackingField", "<MateHeavyWound>k__BackingField", "<MateRearBlow>k__BackingField", "<MateRouts>k__BackingField",
            "<Nearby>k__BackingField", "<OwnHeavyWound>k__BackingField", "<OwnLightWound>k__BackingField", "<RallyAt>k__BackingField",
            "<RearBlow>k__BackingField", "<RecoveryPerSecond>k__BackingField", "<ReformShare>k__BackingField", "<Routing>k__BackingField",
            "<SafeDistance>k__BackingField", "<Shaken>k__BackingField", "<Wavering>k__BackingField", "<WaveringHitPenalty>k__BackingField"],
        [typeof(ProtectionDef)] = ["<Id>k__BackingField", "<Protection>k__BackingField"],
        [typeof(ExperienceDef)] = ["<Defence>k__BackingField", "<Id>k__BackingField", "<Morale>k__BackingField"],
        [typeof(WarriorTypeDef)] = ["<Defence>k__BackingField", "<Id>k__BackingField"],
        [typeof(FighterKit)] = ["<BodyArmour>k__BackingField", "<Experience>k__BackingField", "<HeadCover>k__BackingField",
            "<Shield>k__BackingField", "<Type>k__BackingField", "<Weapon>k__BackingField"],
        [typeof(CombatDef)] = ["_hitChance", "<BodyArmours>k__BackingField", "<DefaultKit>k__BackingField",
            "<ExperienceLevels>k__BackingField", "<Fingerprint>k__BackingField", "<FlankBonus>k__BackingField",
            "<HeadCovers>k__BackingField", "<HeavyWoundPace>k__BackingField", "<HeavyWoundPenalty>k__BackingField",
            "<LightWoundPace>k__BackingField", "<LightWoundPenalty>k__BackingField", "<MaxHit>k__BackingField",
            "<MinHit>k__BackingField", "<RearArc>k__BackingField", "<RearBonus>k__BackingField", "<StrikeArc>k__BackingField", "<FrontArc>k__BackingField", "<ShieldFromFlank>k__BackingField",
            "<ShieldFromRear>k__BackingField", "<ShieldWallBonus>k__BackingField", "<Shields>k__BackingField",
            "<WarriorTypes>k__BackingField", "<Weapons>k__BackingField"],
    };

    [TestCaseSource(nameof(DefTypes))]
    public void DefType_HasOnlyKnownFields(Type type)
    {
        Assert.That(FieldNames(type), Is.EqualTo(KnownDefFields[type].Order(StringComparer.Ordinal).ToArray()),
            $"{type.Name} gained or lost a field. Add it to {type.Name}.AddTo (the scenario fingerprint) and its loader, " +
            "test that it changes the fingerprint, then update KnownDefFields.");
    }

    [Test]
    public void KnownDefFields_CoversEveryDefType()
    {
        var defTypes = typeof(ScenarioDef).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ScenarioDef).Namespace && t.IsClass && !t.IsAbstract);
        Assert.That(KnownDefFields.Keys, Is.SupersetOf(defTypes));
    }

    [TestCaseSource(nameof(StateTypes))]
    public void StateType_HasOnlyKnownFields(Type type)
    {
        Assert.That(FieldNames(type), Is.EqualTo(KnownFields[type].Order(StringComparer.Ordinal).ToArray()),
            $"{type.Name} gained or lost a field. Hash it, deep-copy it in Clone, test it, then update KnownFields.");
    }

    [Test]
    public void Clone_SharesNoMutableObjectWithTheOriginal()
    {
        var sim = TestBattle.Create(3);
        sim.StepTicks(5);
        var original = sim.State;
        var clone = original.Clone();

        AssertNoSharedReferences(original, clone);
        for (int i = 0; i < RngStreams.Count; i++)
        {
            Assert.That(clone.Rng((RngStream)i), Is.Not.SameAs(original.Rng((RngStream)i)));
        }

        for (int i = 0; i < original.Fighters.Count; i++)
        {
            Assert.That(clone.Fighters[i], Is.Not.SameAs(original.Fighters[i]));
            AssertNoSharedReferences(original.Fighters[i], clone.Fighters[i]);
        }

        Assert.That(original.Squads, Is.Not.Empty);
        for (int i = 0; i < original.Squads.Count; i++)
        {
            Assert.That(clone.Squads[i], Is.Not.SameAs(original.Squads[i]));
            AssertNoSharedReferences(original.Squads[i], clone.Squads[i]);
        }

        Assert.That(clone.Scenario, Is.SameAs(original.Scenario), "content is immutable and shared, never copied");
    }

    /// <summary>Defs are shared between clones without copying, which is only safe while nothing can change them.</summary>
    [Test]
    public void Defs_AreImmutable_OnlyReadonlyFields()
    {
        var defTypes = typeof(ScenarioDef).Assembly.GetTypes().Where(t => t.Namespace == typeof(ScenarioDef).Namespace && t.IsClass);
        Assert.That(defTypes, Is.Not.Empty);
        foreach (var type in defTypes)
        {
            var writable = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => !f.IsInitOnly)
                .Select(f => f.Name);
            Assert.That(writable, Is.Empty, $"{type.Name} has a writable field; defs must be immutable.");
            var setters = type.GetProperties().Where(p => p.SetMethod != null).Select(p => p.Name);
            Assert.That(setters, Is.Empty, $"{type.Name} has a property setter; defs must be immutable.");
            var mutableCollections = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType.IsArray || (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>)))
                .Select(f => f.Name);
            Assert.That(mutableCollections, Is.Empty, $"{type.Name} keeps a writable collection; expose a ReadOnlyCollection copy.");
        }
    }

    private static IEnumerable<Type> StateTypes() => KnownFields.Keys;

    private static IEnumerable<Type> DefTypes() => KnownDefFields.Keys;

    private static string[] FieldNames(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => f.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every reference-type field (except strings) must point to a different object in the copy.</summary>
    private static void AssertNoSharedReferences(object original, object copy)
    {
        foreach (var field in original.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType.IsValueType || field.FieldType == typeof(string) ||
                field.FieldType.Namespace == typeof(ScenarioDef).Namespace)
            {
                continue; // values, strings and immutable defs may be shared
            }

            object? a = field.GetValue(original);
            object? b = field.GetValue(copy);
            Assert.That(a is not null && ReferenceEquals(a, b), Is.False,
                $"{original.GetType().Name}.{field.Name} is shared between the original and its clone.");
        }
    }
}
