using System.Collections.Generic;
using Knez.Sim.Formations;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// Tick stage 12: squad bookkeeping after the tick's deaths (battle-sim.md §9, §10): the fallen leave their squad and
    /// the ranks close up from the back (<see cref="SlotAssignment.CloseRanks"/>). Dead fighters stay in the fighter list,
    /// marked dead, for the view. Then, once, the victory check: the battle is decided when at most one side has men in the
    /// fight who aren't routing (<see cref="BattleState.Winner"/>, a <see cref="Events.BattleEnded"/> event).
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

            if (state.Winner == BattleState.Undecided)
            {
                Decide(state);
            }
        }

        /// <summary>
        /// The battle is decided once at most one side has a man in the fight who isn't routing (battle-sim.md §12): that
        /// side holds the field, or nobody (−1). Sides are those the scenario's squads belong to.
        /// </summary>
        private static void Decide(BattleState state)
        {
            int standing = -1;
            int sides = 0;
            List<Fighter> fighters = state.FighterList;
            var scenarioSquads = state.Scenario.Squads;
            for (int side = 0; side <= Defs.SquadSpawnDef.MaxSideId; side++)
            {
                bool present = false;
                for (int s = 0; s < scenarioSquads.Count && !present; s++)
                {
                    present = scenarioSquads[s].SideId == side;
                }

                if (!present)
                {
                    continue;
                }

                for (int i = 0; i < fighters.Count; i++)
                {
                    Fighter man = fighters[i];
                    if (man.SideId == side && man.IsAlive && !man.IsRouting)
                    {
                        standing = side;
                        sides++;
                        break;
                    }
                }
            }

            if (sides <= 1 && scenarioSquads.Count > 0 && HasTwoSides(state))
            {
                state.Winner = sides == 1 ? standing : -1;
                state.AddEvent(new Events.BattleEnded(state.Tick, state.Winner));
            }
        }

        /// <summary>A battle needs two sides to be decided; a drill with one side never ends.</summary>
        private static bool HasTwoSides(BattleState state)
        {
            var squads = state.Scenario.Squads;
            for (int s = 1; s < squads.Count; s++)
            {
                if (squads[s].SideId != squads[0].SideId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
