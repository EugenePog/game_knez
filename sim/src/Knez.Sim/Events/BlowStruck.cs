using Knez.Sim.Combat;

namespace Knez.Sim.Events
{
    /// <summary>A blow was struck (battle-sim.md §11): who at whom, from which side, its chance and whether it got through.
    /// What a blow that got through does (a wound) arrives in M3 step 4.</summary>
    public sealed class BlowStruck : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public BlowStruck(int tick, EntityId attacker, EntityId defender, BlowSector sector, int chance, bool landed)
            : base(tick)
        {
            Attacker = attacker;
            Defender = defender;
            Sector = sector;
            Chance = chance;
            Landed = landed;
        }

        /// <summary>Who struck.</summary>
        public EntityId Attacker { get; }

        /// <summary>Who was struck at.</summary>
        public EntityId Defender { get; }

        /// <summary>Which side of the defender it came from.</summary>
        public BlowSector Sector { get; }

        /// <summary>The chance (‰) it had of getting through — the reason the player can read in the log.</summary>
        public int Chance { get; }

        /// <summary>True when it got through.</summary>
        public bool Landed { get; }
    }
}
