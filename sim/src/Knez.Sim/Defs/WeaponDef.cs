using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable weapon type from the combat rules (<c>content/defs/combat.json</c>, battle-sim.md §11): how far it
    /// reaches, how long a blow takes, and how a blow that gets through lands. Its hit chance is a row of
    /// <see cref="CombatDef.BaseHit"/>.
    /// </summary>
    public sealed class WeaponDef : IDefId
    {
        /// <summary>Creates a weapon; throws <see cref="ArgumentException"/> on invalid values. Times are seconds, rounded
        /// to whole ticks; the wound row is permille and must add up to 1000.</summary>
        public WeaponDef(string id, Fix reach, Fix windupTime, Fix recoveryTime, int lightWound, int heavyWound, int downWound,
            int penetration, int defence)
        {
            Id = DefChecks.Id(id, nameof(id));
            Reach = DefChecks.PositiveMetres(reach, MaxReachMetres, nameof(reach));
            WindupTicks = DefChecks.Ticks(windupTime, MaxActionSeconds, "windup");
            RecoveryTicks = DefChecks.Ticks(recoveryTime, MaxActionSeconds, "recovery");
            LightWound = DefChecks.Permille(lightWound, "wounds.light");
            HeavyWound = DefChecks.Permille(heavyWound, "wounds.heavy");
            DownWound = DefChecks.Permille(downWound, "wounds.down");
            Penetration = DefChecks.Permille(penetration, nameof(penetration));
            Defence = DefChecks.Permille(defence, nameof(defence));
            int sum = lightWound + heavyWound + downWound;
            if (sum != DefChecks.Whole)
            {
                throw new ArgumentException($"wounds: light + heavy + down must be 1000 (got {sum}).");
            }
        }

        /// <summary>Sanity bound for <see cref="Reach"/>, not a balance number.</summary>
        public const int MaxReachMetres = 4;

        /// <summary>Sanity bound for the windup and recovery, not a balance number.</summary>
        public const int MaxActionSeconds = 5;

        /// <summary>Content id, e.g. "sword".</summary>
        public string Id { get; }

        /// <summary>The gap between two bodies (edge to edge) the weapon fights across, metres.</summary>
        public Fix Reach { get; }

        /// <summary>Ticks from starting a blow to striking.</summary>
        public int WindupTicks { get; }

        /// <summary>Ticks after the strike before the next blow can start.</summary>
        public int RecoveryTicks { get; }

        /// <summary>Of the blows that get through, how many in 1000 leave a light wound.</summary>
        public int LightWound { get; }

        /// <summary>Of the blows that get through, how many in 1000 leave a heavy wound.</summary>
        public int HeavyWound { get; }

        /// <summary>Of the blows that get through, how many in 1000 put the man down at once.</summary>
        public int DownWound { get; }

        /// <summary>Share (‰) of the defender's body armour and head cover this weapon's blows ignore (M3 step 9; shields
        /// aren't affected).</summary>
        public int Penetration { get; }

        /// <summary>‰ off blows at the man who carries it (the spear's reach keeps an enemy off; M3 step 9, formerly the
        /// spearman warrior type's).</summary>
        public int Defence { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Reach.Raw);
            hash.Add(WindupTicks);
            hash.Add(RecoveryTicks);
            hash.Add(LightWound);
            hash.Add(HeavyWound);
            hash.Add(DownWound);
            hash.Add(Penetration);
            hash.Add(Defence);
        }
    }
}
