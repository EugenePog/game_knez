using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Combat
{
    /// <summary>The wound rules (battle-sim.md §11): how a blow lands, how wounds add up, and what they cost.</summary>
    internal static class Wounds
    {
        /// <summary>How a blow that got through lands, from a roll 0..999 against the weapon's light / heavy / down row.</summary>
        public static WoundLevel Severity(WeaponDef weapon, int roll)
        {
            if (roll < weapon.LightWound)
            {
                return WoundLevel.Light;
            }

            return roll < weapon.LightWound + weapon.HeavyWound ? WoundLevel.Heavy : WoundLevel.Down;
        }

        /// <summary>
        /// A man at <paramref name="before"/> takes a wound of <paramref name="blow"/>: wounds add up — a second light wound
        /// makes him heavily wounded, a heavy one on a light one leaves him heavily wounded, and any wound on a heavy one
        /// puts him down.
        /// </summary>
        public static WoundLevel Worsen(WoundLevel before, WoundLevel blow)
        {
            if (blow == WoundLevel.Down || before == WoundLevel.Heavy || before == WoundLevel.Down)
            {
                return WoundLevel.Down;
            }

            if (before == WoundLevel.Light)
            {
                return WoundLevel.Heavy;
            }

            return blow;
        }

        /// <summary>Permille taken off the chance of a man's own blows for his wound.</summary>
        public static int HitPenalty(CombatDef rules, WoundLevel wound) =>
            wound == WoundLevel.Light ? rules.LightWoundPenalty : wound == WoundLevel.Heavy ? rules.HeavyWoundPenalty : 0;

        /// <summary>The fraction of his pace a man keeps with his wound (1 unhurt).</summary>
        public static Fix Pace(CombatDef rules, WoundLevel wound) =>
            wound == WoundLevel.Light ? rules.LightWoundPace : wound == WoundLevel.Heavy ? rules.HeavyWoundPace : Fix.One;
    }
}
