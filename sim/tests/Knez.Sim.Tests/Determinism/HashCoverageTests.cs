using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Determinism;

/// <summary>
/// Each state field must actually reach its hash section — KnownFields only proves a field was acknowledged, the golden
/// hash only proves nothing moved. A wrong line in StateHasher (say, Anchor.X hashed twice instead of Anchor.Y) fails here.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class HashCoverageTests
{
    public static IEnumerable<TestCaseData> Mutations()
    {
        TestCaseData Case(string name, string section, Action<BattleState> mutate) =>
            new TestCaseData(section, mutate).SetName($"Mutate_{name}_ChangesOnly{section}");

        yield return Case("FighterX", "Fighters", s => s.FighterList[0].Position += new FixVec2(Fix.FromRaw(1), Fix.Zero));
        yield return Case("FighterY", "Fighters", s => s.FighterList[0].Position += new FixVec2(Fix.Zero, Fix.FromRaw(1)));
        yield return Case("FighterFacing", "Fighters", s => s.FighterList[0].Facing += Angle.FromRaw(1));
        yield return Case("FighterSquadId", "Fighters", s => s.FighterList[0].SquadId = EntityId.None);
        yield return Case("FighterSlot", "Fighters", s => s.FighterList[0].SlotIndex = 7);
        yield return Case("FighterAlive", "Fighters", s => s.FighterList[0].IsAlive = false);
        yield return Case("FighterPassingThrough", "Fighters", s => s.FighterList[0].PassingThrough = true);
        yield return Case("FighterVelocityX", "Fighters", s => s.FighterList[0].Velocity += new FixVec2(Fix.FromRaw(1), Fix.Zero));
        yield return Case("FighterVelocityY", "Fighters", s => s.FighterList[0].Velocity += new FixVec2(Fix.Zero, Fix.FromRaw(1)));
        yield return Case("SquadAnchorX", "Squads", s => s.SquadList[0].Anchor += new FixVec2(Fix.FromRaw(1), Fix.Zero));
        yield return Case("SquadAnchorY", "Squads", s => s.SquadList[0].Anchor += new FixVec2(Fix.Zero, Fix.FromRaw(1)));
        yield return Case("SquadFacing", "Squads", s => s.SquadList[0].Facing += Angle.FromRaw(1));
        yield return Case("SquadFormation", "Squads", s => s.SquadList[0].Formation = TestDefs.Column);
        yield return Case("SquadMembers", "Squads", s => s.SquadList[0].MemberList.RemoveAt(0));
        yield return Case("SquadVelocityX", "Squads", s => s.SquadList[0].Velocity += new FixVec2(Fix.FromRaw(1), Fix.Zero));
        yield return Case("SquadVelocityY", "Squads", s => s.SquadList[0].Velocity += new FixVec2(Fix.Zero, Fix.FromRaw(1)));
        yield return Case("SquadSpin", "Squads", s => s.SquadList[0].Spin = 3);
        yield return Case("SquadOrderKind", "Squads", s => s.SquadList[0].Order = SquadOrder.Move(FixVec2.Zero, Angle.Zero));
        yield return Case("Rng", "Rng", s => s.Rng(RngStream.Misc).NextUInt());
        yield return Case("IdCounter", "Core", s => s.AllocateId());
    }

    [TestCaseSource(nameof(Mutations))]
    public void Mutation_ChangesExactlyItsSection(string section, Action<BattleState> mutate)
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 3);
        var before = Sections(StateHasher.Compute(state));
        mutate(state);
        var after = Sections(StateHasher.Compute(state));

        var changed = before.Keys.Where(k => before[k] != after[k]).ToArray();
        Assert.That(changed, Is.EqualTo(new[] { section }));
    }

    [Test]
    public void SquadOrder_EachFieldMovesTheSquadsHashOnly()
    {
        StateHash With(SquadOrder order)
        {
            var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 3);
            state.SquadList[0].Order = order;
            return StateHasher.Compute(state);
        }

        var baseline = With(SquadOrder.Move(FixVec2.FromInts(5, 5), Angle.Zero));
        StateHash[] variants =
        [
            With(SquadOrder.Move(new FixVec2(Fix.FromInt(5) + Fix.FromRaw(1), Fix.FromInt(5)), Angle.Zero)),
            With(SquadOrder.Move(new FixVec2(Fix.FromInt(5), Fix.FromInt(5) + Fix.FromRaw(1)), Angle.Zero)),
            With(SquadOrder.Move(FixVec2.FromInts(5, 5), Angle.FromRaw(1))),
        ];
        foreach (var variant in variants)
        {
            Assert.Multiple(() =>
            {
                Assert.That(variant.Squads, Is.Not.EqualTo(baseline.Squads));
                Assert.That(variant.Fighters, Is.EqualTo(baseline.Fighters));
                Assert.That(variant.Core, Is.EqualTo(baseline.Core));
            });
        }
    }

    [Test]
    public void DifferentContent_ChangesOnlyCore_WhenTheSpawnedStateIsTheSame()
    {
        // The description is ignored, so change something the fingerprint sees but the spawn doesn't: the map size.
        var a = TestDefs.TwoSquads();
        var b = new Knez.Sim.Defs.ScenarioDef(a.Id, a.Description,
            new Knez.Sim.Defs.MapDef(Fix.FromInt(61), Fix.FromInt(40)), a.Squads.ToArray());
        var ha = Sections(StateHasher.Compute(BattleSetup.Create(a, 1)));
        var hb = Sections(StateHasher.Compute(BattleSetup.Create(b, 1)));
        Assert.That(ha.Keys.Where(k => ha[k] != hb[k]), Is.EqualTo(new[] { "Core" }));
    }

    private static Dictionary<string, ulong> Sections(StateHash hash) => new()
    {
        ["Core"] = hash.Core,
        ["Rng"] = hash.Rng,
        ["Squads"] = hash.Squads,
        ["Fighters"] = hash.Fighters,
    };
}
