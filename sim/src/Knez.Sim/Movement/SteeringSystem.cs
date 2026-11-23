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
    /// member and its gap is under 2 cm, it steps exactly onto the slot. M3: a routing man runs for his edge; in a squad
    /// standing in melee whose formation doesn't keep its ranks, men close in on the enemy (battle-sim.md §11).
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
            MarkSquadsInMelee(state, context.SquadInMelee);
            List<FixVec2> intents = context.FighterIntents;
            intents.Clear();
            for (int i = 0; i < fighters.Count; i++)
            {
                intents.Add(fighters[i].IsAlive ? Follow(state, context, fighters[i], FixVec2.Zero) : FixVec2.Zero);
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
                FixVec2 desired = Follow(state, context, fighter, separation).ClampLength(run);
                FixVec2 slid = Crowding.Slide(state, weights, roles, i, desired);
                slid = Terrain.SlideAlongWalls(state.Scenario.Map, state.Scenario.Movement.PersonalSpace, fighter.Position, fighter.Radius, slid);
                fighter.Velocity = Crowding.Squeeze(state, roles, i, slid);
            }
        }

        /// <summary>Which squads have a man in melee (engaged, not running) this tick, by index in the squad list.</summary>
        private static void MarkSquadsInMelee(BattleState state, List<bool> inMelee)
        {
            List<Squad> squads = state.SquadList;
            inMelee.Clear();
            for (int s = 0; s < squads.Count; s++)
            {
                inMelee.Add(false);
            }

            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (!fighter.IsAlive || !fighter.IsEngaged || fighter.IsRouting || fighter.SquadId.IsNone)
                {
                    continue;
                }

                for (int s = 0; s < squads.Count; s++)
                {
                    if (squads[s].Id == fighter.SquadId)
                    {
                        inMelee[s] = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Closing in (battle-sim.md §11, asked for 2026-11-23): a man of a squad standing in melee who isn't fighting yet
        /// steps toward the nearest enemy in the fight (engaged, or of a squad in melee) and not running whose body is
        /// within <c>closeInRange</c> of his own, at walking pace, stopping half his reach short of touching — inside his
        /// reach, so he engages. Null when no enemy is that near.
        /// </summary>
        private static FixVec2? CloseIn(BattleState state, TickContext context, Fighter fighter)
        {
            Fix range = state.Scenario.Movement.CloseInRange;
            if (range <= Fix.Zero)
            {
                return null;
            }

            List<Fighter> fighters = state.FighterList;
            Fighter? nearest = null;
            Fix best = Fix.Zero;
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (!other.IsAlive || other.IsRouting || other.SideId == fighter.SideId
                    || !(other.IsEngaged || SquadInMelee(state, context, other.SquadId)))
                {
                    continue; // only enemies already in the fight: closing in never starts a second one
                }

                Fix bodies = fighter.Radius + other.Radius;
                Fix limit = bodies + range;
                FixVec2 to = other.Position - fighter.Position;
                if (FixMath.Abs(to.X) > limit || FixMath.Abs(to.Y) > limit)
                {
                    continue;
                }

                Fix gap = to.Length - bodies;
                if (gap <= range && (nearest == null || gap < best))
                {
                    nearest = other;
                    best = gap;
                }
            }

            if (nearest == null)
            {
                return null;
            }

            return StepIn(state, fighter, nearest);
        }

        /// <summary>A step at walking pace toward <paramref name="foe"/>, stopping half the man's reach short of touching;
        /// zero when he is already that close (or there is no foe).</summary>
        private static FixVec2 StepIn(BattleState state, Fighter fighter, Fighter? foe)
        {
            if (foe == null || !foe.IsAlive)
            {
                return FixVec2.Zero;
            }

            FixVec2 toward = foe.Position - fighter.Position;
            Fix gap = toward.Length - fighter.Radius - foe.Radius;
            Fix step = FixMath.Min(fighter.Def.WalkPerTick, gap - (fighter.Def.Kit.Weapon.Reach / 2));
            return step <= Fix.Zero || toward == FixVec2.Zero ? FixVec2.Zero : toward.WithLength(step);
        }

        private static bool SquadInMelee(BattleState state, TickContext context, EntityId squadId)
        {
            if (squadId.IsNone)
            {
                return false;
            }

            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                if (squads[s].Id == squadId)
                {
                    return s < context.SquadInMelee.Count && context.SquadInMelee[s];
                }
            }

            return false;
        }

        private static bool InMelee(BattleState state, TickContext context, Squad squad)
        {
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                if (ReferenceEquals(squads[s], squad))
                {
                    return s < context.SquadInMelee.Count && context.SquadInMelee[s];
                }
            }

            return false;
        }

        /// <summary>
        /// Velocity toward the formation slot, plus <paramref name="separation"/> — or, in a squad standing in melee whose
        /// formation doesn't keep its ranks, a step toward an enemy (closing in, or settling well inside reach).
        /// </summary>
        private static FixVec2 Follow(BattleState state, TickContext context, Fighter fighter, FixVec2 separation)
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
            Fix closeInRange = state.Scenario.Movement.CloseInRange;
            if (squadStill && closeInRange > Fix.Zero && !squad.Formation.KeepsRanks && gap.Length <= closeInRange)
            {
                // Both on a leash: never more than closeInRange from his place, so nobody chases a withdrawing enemy.
                if (fighter.IsEngaged)
                {
                    // Settle well inside his reach, not on its edge, where the press would push him in and out of it
                    // every tick.
                    FixVec2 press = StepIn(state, fighter, state.FindFighter(fighter.Target));
                    if (press != FixVec2.Zero)
                    {
                        return press + separation;
                    }
                }
                else if (InMelee(state, context, squad))
                {
                    FixVec2? close = CloseIn(state, context, fighter);
                    if (close.HasValue)
                    {
                        return close.Value + separation;
                    }
                }
            }

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
