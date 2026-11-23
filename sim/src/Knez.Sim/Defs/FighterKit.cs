using System;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable kit of a fighter type (battle-sim.md §11): weapon, the three pieces of protection, experience and
    /// warrior type, all entries of one <see cref="CombatDef"/>.
    /// </summary>
    public sealed class FighterKit
    {
        /// <summary>Creates a kit; throws <see cref="ArgumentNullException"/> for a missing part.</summary>
        public FighterKit(WeaponDef weapon, ProtectionDef shield, ProtectionDef bodyArmour, ProtectionDef headCover,
            ExperienceDef experience, WarriorTypeDef type)
        {
            Weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            Shield = shield ?? throw new ArgumentNullException(nameof(shield));
            BodyArmour = bodyArmour ?? throw new ArgumentNullException(nameof(bodyArmour));
            HeadCover = headCover ?? throw new ArgumentNullException(nameof(headCover));
            Experience = experience ?? throw new ArgumentNullException(nameof(experience));
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }

        /// <summary>The weapon he fights with.</summary>
        public WeaponDef Weapon { get; }

        /// <summary>His shield ("none" for none).</summary>
        public ProtectionDef Shield { get; }

        /// <summary>His body armour ("none" for none).</summary>
        public ProtectionDef BodyArmour { get; }

        /// <summary>His head cover ("none" for none).</summary>
        public ProtectionDef HeadCover { get; }

        /// <summary>His experience level.</summary>
        public ExperienceDef Experience { get; }

        /// <summary>His warrior type.</summary>
        public WarriorTypeDef Type { get; }

        /// <summary>Hashed by id: the catalogue itself is in <see cref="CombatDef.Fingerprint"/>.</summary>
        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Weapon.Id);
            hash.Add(Shield.Id);
            hash.Add(BodyArmour.Id);
            hash.Add(HeadCover.Id);
            hash.Add(Experience.Id);
            hash.Add(Type.Id);
        }
    }
}
