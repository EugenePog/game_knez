namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable piece of protection from the combat rules (battle-sim.md §11): a shield, a body armour or a head cover.
    /// Each lowers the chance that a blow at its wearer gets through, counted separately.
    /// </summary>
    public sealed class ProtectionDef : IDefId
    {
        /// <summary>Creates a piece; throws <see cref="System.ArgumentException"/> on invalid values.</summary>
        public ProtectionDef(string id, int protection)
        {
            Id = DefChecks.Id(id, nameof(id));
            Protection = DefChecks.Permille(protection, nameof(protection));
        }

        /// <summary>Content id, e.g. "round" or "mail".</summary>
        public string Id { get; }

        /// <summary>Permille taken off the chance a blow gets through (a shield: from the front; see
        /// <see cref="CombatDef.ShieldFromFlank"/>).</summary>
        public int Protection { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Protection);
        }
    }
}
