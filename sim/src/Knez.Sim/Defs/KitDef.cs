using System;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// A named kit from the combat rules (battle-sim.md §7, M3 step 9; ADR-0005): what a man carries — weapon, shield,
    /// body armour, head cover — named by what it holds (<c>axe-shield</c>, <c>sword-shield-mail</c>), never by who
    /// carries it. How well he fights is his <see cref="ExperienceDef"/>, apart. Immutable, shared.
    /// </summary>
    public sealed class KitDef : IDefId
    {
        /// <summary>Creates a kit; every part is required.</summary>
        public KitDef(string id, WeaponDef weapon, ProtectionDef shield, ProtectionDef bodyArmour, ProtectionDef headCover)
        {
            Id = DefChecks.Id(id, nameof(id));
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Shield = shield ?? throw new ArgumentNullException(nameof(shield));
            BodyArmour = bodyArmour ?? throw new ArgumentNullException(nameof(bodyArmour));
            HeadCover = headCover ?? throw new ArgumentNullException(nameof(headCover));
        }

        /// <summary>Content id, e.g. "sword-shield-padded".</summary>
        public string Id { get; }

        /// <summary>What he fights with.</summary>
        public WeaponDef Weapon { get; }

        /// <summary>His shield ("none" for none).</summary>
        public ProtectionDef Shield { get; }

        /// <summary>His body armour ("none" for none).</summary>
        public ProtectionDef BodyArmour { get; }

        /// <summary>His head cover ("none" for none).</summary>
        public ProtectionDef HeadCover { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Weapon.Id);
            hash.Add(Shield.Id);
            hash.Add(BodyArmour.Id);
            hash.Add(HeadCover.Id);
        }
    }
}
