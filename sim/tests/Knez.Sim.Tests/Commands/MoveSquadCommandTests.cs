using Knez.Sim.Commands;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Commands;

[TestFixture]
public class MoveSquadCommandTests
{
    private static BattleState State() => BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);

    [Test]
    public void Validate_EachProblem_HasItsReason()
    {
        var state = State();
        var line = state.Squads[0]; // side 0
        var column = state.Squads[1]; // side 1
        Assert.Multiple(() =>
        {
            Assert.That(new MoveSquadCommand(0, line.Id, FixVec2.FromInts(30, 20)).Validate(state), Is.EqualTo(RejectReason.None));
            Assert.That(new MoveSquadCommand(0, new EntityId(99), FixVec2.FromInts(30, 20)).Validate(state), Is.EqualTo(RejectReason.UnknownEntity));
            Assert.That(new MoveSquadCommand(0, line.Members[0], FixVec2.FromInts(30, 20)).Validate(state), Is.EqualTo(RejectReason.UnknownEntity), "a fighter id is not a squad");
            Assert.That(new MoveSquadCommand(0, column.Id, FixVec2.FromInts(30, 20)).Validate(state), Is.EqualTo(RejectReason.WrongSide));
            Assert.That(new MoveSquadCommand(0, line.Id, FixVec2.FromInts(61, 20)).Validate(state), Is.EqualTo(RejectReason.OffMap));
        });
    }

    [Test]
    public void Validate_EveryMemberDead_NotAlive()
    {
        var state = State();
        var line = state.Squads[0];
        foreach (var id in line.Members)
        {
            state.FindFighter(id)!.IsAlive = false;
        }

        Assert.That(new MoveSquadCommand(0, line.Id, FixVec2.FromInts(30, 20)).Validate(state), Is.EqualTo(RejectReason.NotAlive));
    }

    [Test]
    public void Apply_WithoutFacing_FacesTheMarchDirection()
    {
        var state = State();
        var line = state.Squads[0]; // anchor (20, 10)
        new MoveSquadCommand(0, line.Id, FixVec2.FromInts(30, 10)).Apply(state, null!);
        Assert.That(line.Order, Is.EqualTo(SquadOrder.Move(FixVec2.FromInts(30, 10), Angle.FromDegrees(0))));
    }

    [Test]
    public void Apply_WithFacing_KeepsIt_AndATargetOnTheAnchorKeepsTheCurrentFacing()
    {
        var state = State();
        var line = state.Squads[0];
        new MoveSquadCommand(0, line.Id, FixVec2.FromInts(30, 10), Angle.FromDegrees(135)).Apply(state, null!);
        Assert.That(line.Order.Facing, Is.EqualTo(Angle.FromDegrees(135)));

        new MoveSquadCommand(0, line.Id, line.Anchor).Apply(state, null!);
        Assert.That(line.Order.Facing, Is.EqualTo(line.Facing));
    }

    [Test]
    public void Enqueued_BadCommand_IsRejectedWithAnEventAndChangesNothing()
    {
        var sim = new BattleSimulation(State());
        var before = sim.State.Squads[0].Order;
        sim.Enqueue(new MoveSquadCommand(1, sim.State.Squads[0].Id, FixVec2.FromInts(30, 20)));
        sim.Step();
        var rejected = sim.State.Events.OfType<Knez.Sim.Events.CommandRejected>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Reason, Is.EqualTo(RejectReason.WrongSide));
            Assert.That(sim.State.Squads[0].Order, Is.EqualTo(before));
        });
    }
}
