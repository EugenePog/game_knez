using Knez.Sim.Defs;

namespace Knez.Sim.Units
{
    /// <summary>
    /// A lasting ability on a squad (battle-sim.md §13, M3 step 11): which, until which tick (exclusive), and the Retainer
    /// who used it. It stays with the squad if he leaves (decided by the user 2026-11-26). A value: copied with the squad.
    /// </summary>
    public readonly struct AbilityEffect
    {
        /// <summary>Creates the effect.</summary>
        public AbilityEffect(AbilityDef ability, int until, EntityId by)
        {
            Ability = ability;
            Until = until;
            By = by;
        }

        /// <summary>The ability (its numbers).</summary>
        public AbilityDef Ability { get; }

        /// <summary>The first tick it no longer acts.</summary>
        public int Until { get; }

        /// <summary>The Retainer who used it.</summary>
        public EntityId By { get; }
    }
}
