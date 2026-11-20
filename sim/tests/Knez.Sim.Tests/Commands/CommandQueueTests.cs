using Knez.Sim.Commands;
using Knez.Sim.Events;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Commands;

[TestFixture]
public class CommandQueueTests
{
    [Test]
    public void Enqueue_AtStateTickT_AppliesAtStartOfTickTPlusOne()
    {
        var sim = TestBattle.Create(1, wander: false);
        var journal = new List<string>();
        sim.StepTicks(4);

        sim.Enqueue(new RecordingCommand("a", journal));
        Assert.That(journal, Is.Empty, "nothing applies before the next step");

        sim.Step();
        Assert.That(journal, Is.EqualTo(new[] { "a@5" }));
    }

    [Test]
    public void SameTick_AppliesInEnqueueOrder()
    {
        var sim = TestBattle.Create(1, wander: false);
        var journal = new List<string>();
        sim.Enqueue(new RecordingCommand("first", journal));
        sim.Enqueue(new RecordingCommand("second", journal));
        sim.Enqueue(new RecordingCommand("third", journal));
        sim.Step();
        Assert.That(journal, Is.EqualTo(new[] { "first@1", "second@1", "third@1" }));
    }

    [Test]
    public void EnqueueAt_FutureTick_WaitsAndKeepsTickOrderOverEnqueueOrder()
    {
        var sim = TestBattle.Create(1, wander: false);
        var journal = new List<string>();
        sim.EnqueueAt(5, new RecordingCommand("late", journal));
        sim.EnqueueAt(3, new RecordingCommand("early", journal));
        sim.StepTicks(6);
        Assert.That(journal, Is.EqualTo(new[] { "early@3", "late@5" }));
    }

    [Test]
    public void EnqueueAt_CurrentOrPastTick_Throws()
    {
        var sim = TestBattle.Create(1, wander: false);
        sim.StepTicks(3);
        Assert.That(() => sim.EnqueueAt(3, new RejectedCommand()), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void InvalidCommand_EmitsCommandRejectedAndChangesNoState()
    {
        var sim = TestBattle.Create(1, wander: false);
        var otherSidesFighter = sim.State.Fighters[2].Id;
        var before = sim.ComputeHash();

        sim.Enqueue(new NudgeCommand(sideId: 0, otherSidesFighter, 4, 4));
        sim.Step();

        var after = sim.ComputeHash();
        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Events, Has.Count.EqualTo(1));
            Assert.That(sim.State.Events[0], Is.TypeOf<CommandRejected>());
            Assert.That(((CommandRejected)sim.State.Events[0]).Reason, Is.EqualTo(RejectReason.WrongSide));
            Assert.That(after.Fighters, Is.EqualTo(before.Fighters), "fighters untouched");
            Assert.That(after.Rng, Is.EqualTo(before.Rng), "no random numbers drawn");
        });
    }

    [Test]
    public void Events_AreClearedAtTheStartOfTheNextTick()
    {
        var sim = TestBattle.Create(1, wander: false);
        sim.Enqueue(new RejectedCommand());
        sim.Step();
        Assert.That(sim.State.Events, Has.Count.EqualTo(1));
        sim.Step();
        Assert.That(sim.State.Events, Is.Empty);
    }

    [Test]
    public void Log_RecordsAppliedAndRejectedCommandsWithTheirTicks()
    {
        var sim = TestBattle.Create(1, wander: false);
        var journal = new List<string>();
        sim.Enqueue(new RecordingCommand("ok", journal));
        sim.EnqueueAt(3, new RejectedCommand());
        sim.StepTicks(3);
        Assert.Multiple(() =>
        {
            Assert.That(sim.Commands.Log.Select(c => c.Tick), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(sim.Commands.Pending, Is.Empty);
        });
    }
}
