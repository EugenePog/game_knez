namespace Knez.Sim.Combat
{
    /// <summary>Which side of the defender a blow comes from (battle-sim.md §11, decided 2026-11-23: front within 60° of
    /// his facing, flank to 120°, rear beyond).</summary>
    public enum BlowSector : byte
    {
        /// <summary>From in front: his shield counts fully, and a shield wall more.</summary>
        Front = 0,

        /// <summary>From the side: part of his shield counts, and the attacker gains the flank bonus.</summary>
        Flank = 1,

        /// <summary>From behind: his shield counts little or nothing, and the attacker gains the rear bonus.</summary>
        Rear = 2,
    }
}
