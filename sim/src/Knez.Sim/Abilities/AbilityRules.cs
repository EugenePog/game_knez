using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Abilities
{
    /// <summary>
    /// Retainers' active abilities (battle-sim.md §13, M3 step 11; D-023, D-025): using one, and what a lasting one does
    /// while it acts. Stateless; the state is the Retainer's ready tick and his squad's <see cref="Squad.Effects"/>.
    /// </summary>
    public static class AbilityRules
    {
        /// <summary>
        /// <paramref name="retainer"/> uses his ability now (the command has checked he may): it cools down, an
        /// <see cref="AbilityUsed"/> event is raised, and it acts — a battle cry at once on the men within its radius, a
        /// lasting ability on his squad until its time is up (used again, it starts over).
        /// </summary>
        internal static void Use(BattleState state, Fighter retainer)
        {
            RetainerPart part = retainer.Retainer!.Value;
            AbilityDef ability = part.Trait.Ability;
            retainer.Retainer = part.WithAbilityReadyTick(state.Tick + ability.CooldownTicks);
            Squad? squad = retainer.SquadId.IsNone ? null : state.FindSquad(retainer.SquadId);
            state.AddEvent(new AbilityUsed(state.Tick, retainer.Id, ability.Id, squad != null ? squad.Id : EntityId.None));
            if (ability.Kind == AbilityKind.BattleCry)
            {
                Cry(state, retainer, ability);
            }
            else if (squad != null)
            {
                Lay(squad, new AbilityEffect(ability, state.Tick + ability.DurationTicks, retainer.Id));
            }
        }

        /// <summary>The lasting ability of <paramref name="kind"/> acting on <paramref name="squad"/> this tick, or null.</summary>
        public static AbilityDef? Active(BattleState state, Squad squad, AbilityKind kind)
        {
            List<AbilityEffect> effects = squad.EffectList;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Ability.Kind == kind && state.Tick < effects[i].Until)
                {
                    return effects[i].Ability;
                }
            }

            return null;
        }

        /// <summary>The lasting ability of <paramref name="kind"/> acting on <paramref name="man"/>'s squad, or null (alone: none).</summary>
        public static AbilityDef? ActiveFor(BattleState state, Fighter man, AbilityKind kind)
        {
            Squad? squad = man.SquadId.IsNone ? null : state.FindSquad(man.SquadId);
            return squad == null ? null : Active(state, squad, kind);
        }

        /// <summary>Close ranks: ‰ off a blow from the front at <paramref name="defender"/>; 0 when his squad hasn't closed ranks.</summary>
        internal static int FrontHitCut(BattleState state, Fighter defender)
        {
            AbilityDef? closed = ActiveFor(state, defender, AbilityKind.CloseRanks);
            return closed != null ? closed.FrontHit : 0;
        }

        /// <summary>Close ranks: the share of its pace <paramref name="squad"/> marches at; 1 otherwise.</summary>
        internal static Fix Pace(BattleState state, Squad squad)
        {
            AbilityDef? closed = Active(state, squad, AbilityKind.CloseRanks);
            return closed != null ? closed.Pace : Fix.One;
        }

        /// <summary>Stand to the death: whether <paramref name="man"/>'s squad's morale can't fall this tick.</summary>
        internal static bool StandsFast(BattleState state, Fighter man) => ActiveFor(state, man, AbilityKind.StandToTheDeath) != null;

        // Every man in the fight within the radius, centre to centre, in id order: his side gains, the enemy loses.
        private static void Cry(BattleState state, Fighter retainer, AbilityDef ability)
        {
            Fix reach = ability.Radius * ability.Radius;
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                if (!man.IsAlive || (man.Position - retainer.Position).LengthSquared > reach)
                {
                    continue;
                }

                if (man.SideId == retainer.SideId)
                {
                    MoraleSystem.Change(state, man, ability.OwnMorale, MoraleReason.BattleCry);
                }
                else
                {
                    MoraleSystem.Change(state, man, -ability.EnemyMorale, MoraleReason.EnemyBattleCry);
                }
            }
        }

        // One effect per kind: a new use replaces the old one in its place.
        private static void Lay(Squad squad, AbilityEffect effect)
        {
            List<AbilityEffect> effects = squad.EffectList;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Ability.Kind == effect.Ability.Kind)
                {
                    effects[i] = effect;
                    return;
                }
            }

            effects.Add(effect);
        }
    }
}
