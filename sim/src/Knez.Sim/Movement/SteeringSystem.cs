using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Tick stage 5: each fighter's velocity for this tick (battle-sim.md §10). A squad member follows its formation slot:
    /// it moves with the slot's own motion this tick (from the anchor's move and turn) plus a tenth of the lag it had
    /// before (a 10-tick time constant); anyone inside another man's personal space also eases away from him
    /// (<see cref="Crowding.Separation"/>). That is capped at run speed, and then the part heading into a neighbour is cut
    /// (<see cref="Crowding.Slide"/>): men slide along bodies instead of walking into them. When the squad stands still, nobody crowds the
    /// member and its gap is under 2 cm, it steps exactly onto the slot.
    /// </summary>
    public sealed class SteeringSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            List<int> weights = context.FighterWeights;
            Crowding.FillWeights(state, weights);
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (!fighter.IsAlive)
                {
                    fighter.Velocity = FixVec2.Zero;
                    continue;
                }

                // Cap first, then slide: the slide only removes speed, and cutting an uncapped request (a man 10 m behind
                // his slot asks for ~1 m/tick) would still leave more than run speed, straight into the body in front.
                FixVec2 separation = Crowding.Separation(state, weights, i);
                FixVec2 desired = Follow(state, fighter, separation).ClampLength(fighter.Def.RunPerTick);
                fighter.Velocity = Crowding.Slide(state, weights, i, desired);
            }
        }

        /// <summary>Velocity toward the formation slot, plus <paramref name="separation"/>.</summary>
        private static FixVec2 Follow(BattleState state, Fighter fighter, FixVec2 separation)
        {
            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            if (squad == null || fighter.SlotIndex < 0)
            {
                return separation; // individual orders (heroes) come later
            }

            // A slot past the map edge is reached at the edge (same rule as the collision clamp), so men don't lean into it.
            MapDef map = state.Scenario.Map;
            FixVec2 offset = SlotTracking.Offset(squad, fighter);
            FixVec2 slot = Crowding.Inside(map, SlotTracking.Position(squad, offset), fighter.Radius);
            FixVec2 gap = slot - fighter.Position;

            bool squadStill = squad.Velocity == FixVec2.Zero && squad.Spin == 0;
            if (squadStill && separation == FixVec2.Zero && gap.Length <= MovementRules.SnapDistance)
            {
                return gap;
            }

            // A member moves with its slot when the squad marches or turns.
            FixVec2 slotMotion = squadStill
                ? FixVec2.Zero
                : slot - Crowding.Inside(map, SlotTracking.PreviousPosition(squad, offset), fighter.Radius);
            FixVec2 lag = gap - slotMotion; // the gap already includes this tick's slot motion: don't count it twice
            return slotMotion + lag / MovementRules.SlotCatchUpTicks + separation;
        }
    }
}
