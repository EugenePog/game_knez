using Knez.Sim.Commands;

namespace Knez.Sim.Tests.Support;

/// <summary>Small battles for tests: <see cref="TestDefs.SmallBattle"/> — two squads of two, sides 0 and 1.</summary>
public static class TestBattle
{
    public static BattleState CreateState(ulong seed) => BattleSetup.Create(TestDefs.SmallBattle(), seed);

    /// <summary>The standard pipeline plus <see cref="WanderSystem"/>, so state changes every tick.</summary>
    public static ISimSystem[] SystemsWithWander() => [.. BattleSimulation.CreateDefaultSystems(), new WanderSystem()];

    /// <summary>A simulation over <see cref="CreateState"/>; with <paramref name="wander"/> fighters drift randomly.</summary>
    public static BattleSimulation Create(ulong seed, bool wander = true) =>
        new(CreateState(seed), new CommandQueue(), wander ? SystemsWithWander() : BattleSimulation.CreateDefaultSystems());
}
