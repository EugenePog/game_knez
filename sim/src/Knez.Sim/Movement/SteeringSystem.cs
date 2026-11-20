using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Tick stage 5: each fighter's velocity for this tick (battle-sim.md §10). A squad member follows its formation slot:
    /// it moves with the slot's own motion this tick (from the anchor's move and turn) plus a tenth of the lag it had
    /// before (a 10-tick time constant), capped at its run speed; when the squad stands still and the gap is under 2 cm it
    /// steps exactly onto the slot. Separation and collisions arrive in M1 step 4.
    /// </summary>
    public sealed class SteeringSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                fighter.Velocity = fighter.IsAlive ? Desired(state, fighter) : FixVec2.Zero;
            }
        }

        private static FixVec2 Desired(BattleState state, Fighter fighter)
        {
            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            if (squad == null || fighter.SlotIndex < 0)
            {
                return FixVec2.Zero; // individual orders (heroes) come later
            }

            FixVec2 offset = SlotTracking.Offset(squad, fighter);
            FixVec2 slot = SlotTracking.Position(squad, offset);
            FixVec2 gap = slot - fighter.Position;

            bool squadStill = squad.Velocity == FixVec2.Zero && squad.Spin == 0;
            if (squadStill && gap.Length <= MovementRules.SnapDistance)
            {
                return gap;
            }

            // A member moves with its slot when the squad marches or turns.
            FixVec2 slotMotion = SlotTracking.Motion(squad, offset, slot);
            FixVec2 lag = gap - slotMotion; // the gap already includes this tick's slot motion: don't count it twice
            FixVec2 desired = slotMotion + lag / MovementRules.SlotCatchUpTicks;
            return desired.ClampLength(fighter.Def.RunPerTick);
        }
    }
}
