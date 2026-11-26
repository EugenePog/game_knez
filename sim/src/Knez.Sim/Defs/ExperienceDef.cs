namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable experience level from the combat rules (battle-sim.md §11), e.g. new, experienced, professional, elite.
    /// As the attacker's it picks a column of <see cref="CombatDef.BaseHit"/>; as the defender's it lowers the chance. A
    /// Retainer's level also sets his leadership (M3 step 10, D-023); professional and elite are for Retainers only (D-019).
    /// </summary>
    public sealed class ExperienceDef : IDefId
    {
        /// <summary>Creates a level; throws <see cref="System.ArgumentException"/> on invalid values.</summary>
        public ExperienceDef(string id, int defence, int morale = 750, int leadership = 0, bool retainerOnly = false)
        {
            Id = DefChecks.Id(id, nameof(id));
            Defence = DefChecks.Permille(defence, nameof(defence));
            Morale = DefChecks.Permille(morale, nameof(morale));
            Leadership = DefChecks.Range(leadership, 0, MaxLeadership, nameof(leadership));
            RetainerOnly = retainerOnly;
        }

        /// <summary>Sanity bound for <see cref="Leadership"/> (‰): never a whole immunity to losses.</summary>
        public const int MaxLeadership = 900;

        /// <summary>Content id, e.g. "elite".</summary>
        public string Id { get; }

        /// <summary>Permille taken off the chance a blow at a man of this level gets through.</summary>
        public int Defence { get; }

        /// <summary>The morale (0..1000) a man of this level starts a battle with, and recovers up to (battle-sim.md §12,
        /// decided 2026-11-23: by experience).</summary>
        public int Morale { get; }

        /// <summary>A Retainer of this level standing in a squad cuts every morale loss of its men by this share (‰), and
        /// speeds their recovery by it; his own too (battle-sim.md §13, decided 2026-11-26).</summary>
        public int Leadership { get; }

        /// <summary>Only a Retainer may be of this level (D-019: professional and elite); a squad's men may not.</summary>
        public bool RetainerOnly { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Defence);
            hash.Add(Morale);
            hash.Add(Leadership);
            hash.Add(RetainerOnly);
        }
    }
}
