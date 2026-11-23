namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable experience level from the combat rules (battle-sim.md §11), e.g. new, experienced, professional, elite.
    /// As the attacker's it picks a column of <see cref="CombatDef.BaseHit"/>; as the defender's it lowers the chance.
    /// </summary>
    public sealed class ExperienceDef : IDefId
    {
        /// <summary>Creates a level; throws <see cref="System.ArgumentException"/> on invalid values.</summary>
        public ExperienceDef(string id, int defence, int morale = 750)
        {
            Id = DefChecks.Id(id, nameof(id));
            Defence = DefChecks.Permille(defence, nameof(defence));
            Morale = DefChecks.Permille(morale, nameof(morale));
        }

        /// <summary>Content id, e.g. "elite".</summary>
        public string Id { get; }

        /// <summary>Permille taken off the chance a blow at a man of this level gets through.</summary>
        public int Defence { get; }

        /// <summary>The morale (0..1000) a man of this level starts a battle with, and recovers up to (battle-sim.md §12,
        /// decided 2026-11-23: by experience).</summary>
        public int Morale { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Defence);
            hash.Add(Morale);
        }
    }
}
