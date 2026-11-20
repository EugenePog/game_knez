using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Support;

/// <summary>Test-only system: every alive fighter drifts by -2..2 sixteenths of a metre per axis each tick.</summary>
public sealed class WanderSystem : ISimSystem
{
    public void Execute(BattleState state, TickContext context)
    {
        var rng = context.Rng(RngStream.Ai);
        var fighters = state.FighterList; // systems iterate the list with for, as sim systems must
        for (int i = 0; i < fighters.Count; i++)
        {
            var fighter = fighters[i];
            if (!fighter.IsAlive)
            {
                continue;
            }

            fighter.Position += new FixVec2(Fix.Ratio(rng.NextInt(-2, 3), 16), Fix.Ratio(rng.NextInt(-2, 3), 16));
        }
    }
}
