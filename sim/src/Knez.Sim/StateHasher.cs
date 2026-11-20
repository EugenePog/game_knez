using System.Collections.Generic;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// Hashes every field of <see cref="BattleState"/> that affects the future, in a fixed canonical order.
    /// A field missing here is a desync nobody will catch: extend this whenever state grows (sim-feature checklist).
    /// </summary>
    public static class StateHasher
    {
        /// <summary>The state's hash, with section sub-hashes.</summary>
        public static StateHash Compute(BattleState state)
        {
            var core = Fnv1a64.Start();
            core.Add(state.Tick);
            core.Add(state.Seed);
            core.Add(state.NextEntityIdValue);

            var rng = Fnv1a64.Start();
            for (int i = 0; i < RngStreams.Count; i++)
            {
                SimRng stream = state.Rng((RngStream)i);
                rng.Add(stream.State);
                rng.Add(stream.Increment);
            }

            var fighters = Fnv1a64.Start();
            List<Fighter> list = state.FighterList;
            fighters.Add(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                Fighter fighter = list[i];
                fighters.Add(fighter.Id.Value);
                fighters.Add(fighter.SideId);
                fighters.Add(fighter.IsAlive);
                fighters.Add(fighter.Position.X.Raw);
                fighters.Add(fighter.Position.Y.Raw);
            }

            return new StateHash(core.Value, rng.Value, fighters.Value);
        }
    }
}
