using System;
using Knez.Sim.Defs;

namespace Knez.Sim.Units
{
    /// <summary>
    /// What makes a fighter a Retainer (дружинник, battle-sim.md §13, M3 step 10): his name, his trait and (M3 step 11)
    /// the tick his ability is ready again. Values only — <see cref="Fighter"/> is copied member by member for snapshots,
    /// so nothing mutable may hide here; a lasting effect lives on the squad (<see cref="AbilityEffect"/>).
    /// </summary>
    public readonly struct RetainerPart
    {
        /// <summary>Creates the part.</summary>
        public RetainerPart(string name, TraitDef trait, int abilityReadyTick = 0)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Trait = trait ?? throw new ArgumentNullException(nameof(trait));
            AbilityReadyTick = abilityReadyTick;
        }

        /// <summary>His name, unique among the battle's Retainers.</summary>
        public string Name { get; }

        /// <summary>His trait (D-025).</summary>
        public TraitDef Trait { get; }

        /// <summary>The first tick he can use his ability (again); 0 at the start: ready.</summary>
        public int AbilityReadyTick { get; }

        /// <summary>The same part, ready again at <paramref name="tick"/>.</summary>
        public RetainerPart WithAbilityReadyTick(int tick) => new RetainerPart(Name, Trait, tick);
    }
}
