using System.Collections.Generic;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Tick stage 4: plans the routes squads are waiting for (battle-sim.md §9, §10), first come first served (by the
    /// tick of the request, then squad id). Each search runs to the end; once a tick has spent
    /// <see cref="Defs.MovementDef.PathBudget"/> search nodes, the rest wait for the next tick. A squad marches the tick
    /// after its route is planned. Detached fighters (heroes) get routes when they arrive.
    /// </summary>
    public sealed class PathSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Squad> squads = state.SquadList;
            int spent = 0;
            while (spent < state.Scenario.Movement.PathBudget)
            {
                Squad? next = null;
                for (int i = 0; i < squads.Count; i++)
                {
                    Squad squad = squads[i];
                    if (squad.PathRequestTick < 0)
                    {
                        continue;
                    }

                    if (squad.Order.Kind != SquadOrderKind.Move)
                    {
                        squad.ClearPath(); // the order went away while it waited
                        continue;
                    }

                    if (next == null || squad.PathRequestTick < next.PathRequestTick)
                    {
                        next = squad; // ids ascend, so equal ticks keep the lower id
                    }
                }

                if (next == null)
                {
                    return;
                }

                spent += PathPlanner.Plan(state, next, context.Nav, context.PathScratch);
            }
        }
    }
}
