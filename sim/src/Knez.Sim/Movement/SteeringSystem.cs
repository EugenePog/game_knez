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
    /// (<see cref="Crowding.Slide"/>): men slide along bodies instead of walking into them, and along walls
    /// (<see cref="Terrain.SlideAlongWalls"/>). Passing through allied ranks is slow (<see cref="Crowding.Squeeze"/>). When the squad stands still, nobody crowds the
    /// member and its gap is under 2 cm, it steps exactly onto the slot.
    /// </summary>
    public sealed class SteeringSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Fighter> fighters = state.FighterList;
            List<int> weights = context.FighterWeights;
            List<CrowdRole> roles = context.FighterRoles;
            Crowding.Fill(state, weights, roles);

            // Where each man means to go (formation-following alone) decides whether he walks into allied ranks.
            List<FixVec2> intents = context.FighterIntents;
            intents.Clear();
            for (int i = 0; i < fighters.Count; i++)
            {
                intents.Add(fighters[i].IsAlive ? Follow(state, fighters[i], FixVec2.Zero) : FixVec2.Zero);
            }

            Crowding.UpdatePassing(state, roles, intents);
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
                FixVec2 separation = Crowding.Separation(state, weights, roles, i);
                Fix run = fighter.Def.RunPerTick * Combat.Wounds.Pace(state.Scenario.Combat, fighter.Wound); // wounds slow him
                FixVec2 desired = Follow(state, fighter, separation).ClampLength(run);
                FixVec2 slid = Crowding.Slide(state, weights, roles, i, desired);
                slid = Terrain.SlideAlongWalls(state.Scenario.Map, state.Scenario.Movement.PersonalSpace, fighter.Position, fighter.Radius, slid);
                fighter.Velocity = Crowding.Squeeze(state, roles, i, slid);
            }
        }

        /// <summary>Velocity toward the formation slot, plus <paramref name="separation"/>.</summary>
        private static FixVec2 Follow(BattleState state, Fighter fighter, FixVec2 separation)
        {
            if (fighter.IsRouting)
            {
                // Routing (battle-sim.md §12): run for his side's map edge, ignoring his squad and its orders.
                FixVec2 away = Morale.MoraleRules.FleeTarget(state, fighter) - fighter.Position;
                return (away == FixVec2.Zero ? FixVec2.Zero : away.WithLength(fighter.Def.RunPerTick)) + separation;
            }

            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            if (squad == null || fighter.SlotIndex < 0)
            {
                return separation; // individual orders (heroes) come later
            }

            if (squad.Broken)
            {
                return separation; // a man who rallied while his squad is broken stands where he stopped, until it re-forms
            }

            // A slot past the map edge or inside a wall is reached as close as a body fits, on the squad's side of the
            // wall, so men stand at the edge or against the wall instead of leaning into it (decided 2026-11-21).
            MapDef map = state.Scenario.Map;
            FixVec2 offset = SlotTracking.Offset(squad, fighter);
            FixVec2 slot = Terrain.ConstrainToward(map, SlotTracking.Position(squad, offset), fighter.Radius, squad.Anchor);
            FixVec2 gap = slot - fighter.Position;

            bool squadStill = squad.Velocity == FixVec2.Zero && squad.Spin == 0;
            if (squadStill && fighter.IsEngaged)
            {
                // The melee lock (battle-sim.md §11): a man in reach of his enemy holds his ground; he only eases out of
                // others' personal space. He follows his slot again when his squad steps back out of contact.
                return separation;
            }
            if (squadStill && separation == FixVec2.Zero && gap.Length <= MovementRules.SnapDistance)
            {
                return gap;
            }

            // A member moves with its slot when the squad marches or turns.
            FixVec2 slotMotion = squadStill
                ? FixVec2.Zero
                : slot - Terrain.ConstrainToward(map, SlotTracking.PreviousPosition(squad, offset), fighter.Radius, squad.Anchor - squad.Velocity);
            FixVec2 lag = gap - slotMotion; // the gap already includes this tick's slot motion: don't count it twice
            return slotMotion + lag / state.Scenario.Movement.SlotCatchUpTicks + separation;
        }
    }
}
