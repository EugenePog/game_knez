namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable warrior type from the combat rules (battle-sim.md §11), e.g. default, spearman, mounted. Lowers the
    /// chance a blow at him gets through; what else a type does (a spearman keeping the enemy at reach, riders) arrives
    /// with the systems that use it.
    /// </summary>
    public sealed class WarriorTypeDef : IDefId
    {
        /// <summary>Creates a type; throws <see cref="System.ArgumentException"/> on invalid values.</summary>
        public WarriorTypeDef(string id, int defence)
        {
            Id = DefChecks.Id(id, nameof(id));
            Defence = DefChecks.Permille(defence, nameof(defence));
        }

        /// <summary>Content id, e.g. "spearman".</summary>
        public string Id { get; }

        /// <summary>Permille taken off the chance a blow at a man of this type gets through.</summary>
        public int Defence { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Defence);
        }
    }
}
