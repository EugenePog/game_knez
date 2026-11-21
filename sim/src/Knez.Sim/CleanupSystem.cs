using System.Collections.Generic;
using Knez.Sim.Formations;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// Tick stage 12: squad bookkeeping after the tick's deaths (battle-sim.md §9, §10): the fallen leave their squad and
    /// the ranks close up from the back (<see cref="SlotAssignment.CloseRanks"/>). Dead fighters stay in the fighter list,
    /// marked dead, for the view. Victory checks come with combat.
    /// </summary>
    public sealed class CleanupSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Squad> squads = state.SquadList;
            for (int i = 0; i < squads.Count; i++)
            {
                SlotAssignment.CloseRanks(state, squads[i]);
            }
        }
    }
}
