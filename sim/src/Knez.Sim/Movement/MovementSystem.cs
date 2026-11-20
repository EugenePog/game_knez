using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Tick stage 6: applies each fighter's velocity and turns it at its turn rate (battle-sim.md §10). A member in
    /// formation faces the squad's front, also while shuffling sideways or swinging round with a turning squad; one moving
    /// faster than walking pace relative to its own slot (running to catch up) looks where it runs. Then bodies that ended
    /// up overlapping are pushed apart and kept on the map (<see cref="Crowding.Resolve"/>); facing follows the intended
    /// velocity, not the shove.
    /// </summary>
    public sealed class MovementSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (!fighter.IsAlive)
                {
                    continue;
                }

                FixVec2 velocity = fighter.Velocity;
                fighter.Position += velocity;

                Squad? squad = fighter.SquadId.IsNone || fighter.SlotIndex < 0 ? null : state.FindSquad(fighter.SquadId);
                FixVec2 relative = squad == null ? velocity : velocity - SlotMotion(squad, fighter);
                Angle look;
                if (relative.Length > fighter.Def.WalkPerTick || (squad == null && velocity != FixVec2.Zero))
                {
                    look = FixMath.Atan2(velocity.Y, velocity.X);
                }
                else if (squad != null)
                {
                    look = squad.Facing;
                }
                else
                {
                    continue;
                }

                fighter.Facing = fighter.Facing.TurnToward(look, fighter.Def.TurnPerTick);
            }

            List<int> weights = context.FighterWeights;
            List<CrowdRole> roles = context.FighterRoles;
            Crowding.Fill(state, weights, roles);
            Crowding.Resolve(state, weights, roles);
        }

        /// <summary>The fighter's slot motion this tick: outer slots of a turning squad move faster than its anchor.</summary>
        private static FixVec2 SlotMotion(Squad squad, Fighter fighter)
        {
            FixVec2 offset = SlotTracking.Offset(squad, fighter);
            return SlotTracking.Motion(squad, offset, SlotTracking.Position(squad, offset));
        }
    }
}
