using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests;

[TestFixture]
public class BattleStateTests
{
    [Test]
    public void AllocateId_IsMonotonicAndNeverNone()
    {
        var state = new BattleState(1);
        var ids = Enumerable.Range(0, 5).Select(_ => state.AllocateId()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ids.Select(id => id.Value), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(ids.Any(id => id.IsNone), Is.False);
            Assert.That(EntityId.None.IsNone, Is.True);
        });
    }

    [Test]
    public void AddFighter_KeepsFightersSortedById()
    {
        var state = TestBattle.CreateState(1);
        Assert.That(state.Fighters.Select(f => f.Id), Is.Ordered);
    }

    [Test]
    public void FindFighter_ReturnsTheFighterOrNull()
    {
        var state = TestBattle.CreateState(1);
        var third = state.Fighters[2];
        Assert.Multiple(() =>
        {
            Assert.That(state.FindFighter(third.Id), Is.SameAs(third));
            Assert.That(state.FindFighter(new EntityId(99)), Is.Null);
            Assert.That(state.FindFighter(EntityId.None), Is.Null);
        });
    }

    [Test]
    public void Clone_HasTheSameHashAndAnIndependentFuture()
    {
        var original = TestBattle.CreateState(11);
        var clone = original.Clone();
        Assert.That(StateHasher.Compute(clone), Is.EqualTo(StateHasher.Compute(original)));

        var before = StateHasher.Compute(original);
        clone.Fighters[0].Position = FixVec2.FromInts(50, 50);
        clone.Rng(RngStream.Combat).NextUInt();
        clone.AddFighter(1, FixVec2.Zero);

        Assert.That(StateHasher.Compute(original), Is.EqualTo(before));
    }
}
