using System.Reflection;
using Knez.Sim.Commands;
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
        [typeof(BattleState)] = ["<Seed>k__BackingField", "<Tick>k__BackingField", "_events", "_fighters", "_nextEntityId", "_rng"],
        [typeof(Fighter)] = ["<Id>k__BackingField", "<IsAlive>k__BackingField", "<Position>k__BackingField", "<SideId>k__BackingField"],
        [typeof(SimRng)] = ["_increment", "_state"],
        [typeof(CommandQueue)] = ["_log", "_nextSequence", "_pending"],
    };

    [TestCaseSource(nameof(StateTypes))]
    public void StateType_HasOnlyKnownFields(Type type)
    {
        var actual = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(f => f.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.That(actual, Is.EqualTo(KnownFields[type].Order(StringComparer.Ordinal).ToArray()),
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
    }

    private static IEnumerable<Type> StateTypes() => KnownFields.Keys;

    /// <summary>Every reference-type field (except strings) must point to a different object in the copy.</summary>
    private static void AssertNoSharedReferences(object original, object copy)
    {
        foreach (var field in original.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType.IsValueType || field.FieldType == typeof(string))
            {
                continue;
            }

            object? a = field.GetValue(original);
            object? b = field.GetValue(copy);
            Assert.That(a is not null && ReferenceEquals(a, b), Is.False,
                $"{original.GetType().Name}.{field.Name} is shared between the original and its clone.");
        }
    }
}
