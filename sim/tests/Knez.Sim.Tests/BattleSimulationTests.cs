using Knez.Sim.Ai;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Morale;
using Knez.Sim.Movement;
using Knez.Sim.Orders;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests;

[TestFixture]
public class BattleSimulationTests
{
    [Test]
    public void Step_AdvancesTickByExactlyOne()
    {
        var sim = TestBattle.Create(1);
        sim.Step();
        Assert.That(sim.State.Tick, Is.EqualTo(1));
        sim.StepTicks(9);
        Assert.That(sim.State.Tick, Is.EqualTo(10));
    }

    [Test]
    public void DefaultSystems_RunInTheOrderOfBattleSimSection9()
    {
        Type[] expected =
        [
            typeof(CommandSystem), typeof(AiSystem), typeof(OrderSystem), typeof(PathSystem),
            typeof(SteeringSystem), typeof(MovementSystem), typeof(TargetingSystem), typeof(CombatSystem),
            typeof(DamageSystem), typeof(MoraleSystem), typeof(FatigueSystem), typeof(CleanupSystem),
        ];
        Assert.That(BattleSimulation.CreateDefaultSystems().Select(s => s.GetType()), Is.EqualTo(expected));
    }

    [Test]
    public void ExceptionDuringTick_FaultsTheSimulationAndBlocksFurtherSteps()
    {
        var sim = TestBattle.Create(1);
        sim.Enqueue(new ThrowingCommand());
        Assert.Multiple(() =>
        {
            Assert.That(() => sim.Step(), Throws.InvalidOperationException.With.Message.EqualTo("boom"));
            Assert.That(sim.IsFaulted, Is.True);
            Assert.That(() => sim.Step(), Throws.InvalidOperationException.With.Message.Contains("faulted"));
        });
    }

    [Test]
    public void EmptyPipeline_ChangesOnlyTheTickCounter()
    {
        var sim = TestBattle.Create(1, wander: false);
        var before = sim.ComputeHash();
        sim.StepTicks(100);
        var after = sim.ComputeHash();
        Assert.Multiple(() =>
        {
            Assert.That(after.Fighters, Is.EqualTo(before.Fighters));
            Assert.That(after.Squads, Is.EqualTo(before.Squads));
            Assert.That(after.Rng, Is.EqualTo(before.Rng));
            Assert.That(after.Core, Is.Not.EqualTo(before.Core));
        });
    }
}
