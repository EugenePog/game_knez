using Knez.Sim.Commands;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Support;

/// <summary>Small battles for tests: four fighters, two per side, at fixed positions.</summary>
public static class TestBattle
{
    public static BattleState CreateState(ulong seed)
    {
        var state = new BattleState(seed);
        state.AddFighter(0, FixVec2.FromInts(0, 0));
        state.AddFighter(0, FixVec2.FromInts(2, 0));
        state.AddFighter(1, FixVec2.FromInts(0, 10));
        state.AddFighter(1, FixVec2.FromInts(2, 10));
        return state;
    }

    /// <summary>The standard pipeline plus <see cref="WanderSystem"/>, so state changes every tick.</summary>
    public static ISimSystem[] SystemsWithWander() => [.. BattleSimulation.CreateDefaultSystems(), new WanderSystem()];

    /// <summary>A simulation over <see cref="CreateState"/>; with <paramref name="wander"/> fighters drift randomly.</summary>
    public static BattleSimulation Create(ulong seed, bool wander = true) =>
        new(CreateState(seed), new CommandQueue(), wander ? SystemsWithWander() : BattleSimulation.CreateDefaultSystems());
}
