using Knez.Sim.Formations;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests;

[TestFixture]
public class BattleSetupTests
{
    [Test]
    public void Create_SpawnsEverySquadWithItsFighters()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        Assert.Multiple(() =>
        {
            Assert.That(state.Squads.Select(s => s.Name), Is.EqualTo(new[] { "Line", "Column" }));
            Assert.That(state.Squads.Select(s => s.Members.Count), Is.EqualTo(new[] { 8, 6 }));
            Assert.That(state.Fighters, Has.Count.EqualTo(14));
            Assert.That(state.Fighters.Select(f => f.Def), Has.All.SameAs(TestDefs.Warrior));
            Assert.That(state.Scenario.Id, Is.EqualTo("test-two-squads"));
        });
    }

    [Test]
    public void Create_IdsFollowTheScenario_SquadThenItsFightersInSlotOrder()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        Squad line = state.Squads[0];
        Squad column = state.Squads[1];
        Assert.Multiple(() =>
        {
            Assert.That(line.Id.Value, Is.EqualTo(1));
            Assert.That(line.Members.Select(m => m.Value), Is.EqualTo(Enumerable.Range(2, 8)));
            Assert.That(column.Id.Value, Is.EqualTo(10));
            Assert.That(column.Members.Select(m => m.Value), Is.EqualTo(Enumerable.Range(11, 6)));
            Assert.That(state.NextEntityIdValue, Is.EqualTo(17));
            Assert.That(state.Squads.Select(s => s.Id), Is.Ordered);
            Assert.That(state.Fighters.Select(f => f.Id), Is.Ordered);
        });
    }

    [Test]
    public void Create_EveryFighterStandsOnItsSlotAndFacesWithItsSquad()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        foreach (Squad squad in state.Squads)
        {
            for (int i = 0; i < squad.Members.Count; i++)
            {
                Fighter fighter = state.FindFighter(squad.Members[i])!;
                var expected = FormationLayout.SlotPosition(squad.Formation, squad.Members.Count, i, squad.Anchor, squad.Facing);
                Assert.Multiple(() =>
                {
                    Assert.That(fighter.SquadId, Is.EqualTo(squad.Id));
                    Assert.That(fighter.SlotIndex, Is.EqualTo(i));
                    Assert.That(fighter.SideId, Is.EqualTo(squad.SideId));
                    Assert.That(fighter.Position, Is.EqualTo(expected));
                    Assert.That(fighter.Facing, Is.EqualTo(squad.Facing));
                });
            }
        }
    }

    [Test]
    public void Create_LineFacingNorth_ExactPositions()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        var line = state.Squads[0].Members.Select(id => state.FindFighter(id)!.Position).ToArray();
        // Anchor (20, 10); 1 m spacing; second rank 1.5 m south.
        Assert.That(line[0], Is.EqualTo(new FixVec2(Fix.Ratio(185, 10), Fix.FromInt(10))));
        Assert.That(line[7], Is.EqualTo(new FixVec2(Fix.Ratio(215, 10), Fix.Ratio(85, 10))));
    }

    [Test]
    public void FindSquad_ReturnsTheSquadOrNull()
    {
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        Assert.Multiple(() =>
        {
            Assert.That(state.FindSquad(new EntityId(10)), Is.SameAs(state.Squads[1]));
            Assert.That(state.FindSquad(new EntityId(2)), Is.Null); // a fighter id
            Assert.That(state.FindSquad(EntityId.None), Is.Null);
        });
    }

    [Test]
    public void Create_EmptyScenario_IsTheSameAsTheSeedConstructor()
    {
        var a = BattleSetup.Create(Knez.Sim.Defs.ScenarioDef.Empty, seed: 5);
        var b = new BattleState(5);
        Assert.That(StateHasher.Compute(a), Is.EqualTo(StateHasher.Compute(b)));
    }
}
