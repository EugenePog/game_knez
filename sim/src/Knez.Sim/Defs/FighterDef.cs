using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable fighter type from content (battle-sim.md §7). Shared by every fighter of the type and by every clone
    /// of the state; never copied, never written. Equipment, experience and warrior type are its <see cref="Kit"/>.
    /// </summary>
    public sealed class FighterDef
    {
        /// <summary>Creates a def; throws <see cref="ArgumentException"/> on invalid values. Without a kit the fighter
        /// takes <see cref="CombatDef.Standard"/>'s default kit.</summary>
        public FighterDef(string id, Fix radius, Fix walkSpeed, Fix runSpeed, Fix turnRate, FighterKit? kit = null)
        {
            Kit = kit ?? CombatDef.Standard.DefaultKit;
            Id = DefChecks.Id(id, nameof(id));
            Radius = DefChecks.PositiveMetres(radius, MaxRadiusMetres, nameof(radius));
            WalkSpeed = DefChecks.Speed(walkSpeed, nameof(walkSpeed));
            RunSpeed = DefChecks.Speed(runSpeed, nameof(runSpeed));
            if (runSpeed < walkSpeed)
            {
                throw new ArgumentException($"runSpeed must be at least walkSpeed (got {runSpeed} < {walkSpeed}).");
            }

            // A body steps clean through a wall in one tick only if it moves more than its radius plus half the wall's
            // thickness: past the middle, the wall pushes it out on the far side.
            if (RunPerTick >= Radius + WallDef.MinThickness / 2)
            {
                throw new ArgumentException(
                    $"runSpeed {runSpeed} m/s is too fast for radius {radius} m: under (radius + {WallDef.MinThickness / 2} m) per tick, or the fighter could step through a wall.");
            }

            TurnRate = DefChecks.Positive(turnRate, MaxTurnRate, "°/s", nameof(turnRate));
        }

        /// <summary>Sanity bound for <see cref="Radius"/>, not a balance number.</summary>
        public const int MaxRadiusMetres = 2;

        /// <summary>Sanity bound for speeds (m/s), not a balance number. Also see the check against
        /// <see cref="WallDef.MinThickness"/> in the constructor.</summary>
        public const int MaxSpeed = 8;

        /// <summary>Slowest allowed speed: 0.1 m/s. Slower would round to (almost) nothing per tick and a squad would
        /// never arrive. A sanity bound, not a balance number.</summary>
        public static readonly Fix MinSpeed = Fix.Ratio(1, 10);

        /// <summary>Sanity bound for <see cref="TurnRate"/> (°/s), not a balance number.</summary>
        public const int MaxTurnRate = 3600;

        /// <summary>Content id, e.g. "warrior".</summary>
        public string Id { get; }

        /// <summary>Weapon, protection, experience and warrior type: entries of the battle's <see cref="CombatDef"/>.</summary>
        public FighterKit Kit { get; }

        /// <summary>Collision circle radius in metres.</summary>
        public Fix Radius { get; }

        /// <summary>Marching pace, m/s. A squad marches at the walk speed of its slowest member.</summary>
        public Fix WalkSpeed { get; }

        /// <summary>Top speed, m/s. For now only used to catch up with the formation slot.</summary>
        public Fix RunSpeed { get; }

        /// <summary>How fast the fighter turns, degrees per second.</summary>
        public Fix TurnRate { get; }

        /// <summary><see cref="WalkSpeed"/> as metres per tick.</summary>
        public Fix WalkPerTick => WalkSpeed / BattleState.TicksPerSecond;

        /// <summary><see cref="RunSpeed"/> as metres per tick.</summary>
        public Fix RunPerTick => RunSpeed / BattleState.TicksPerSecond;

        /// <summary><see cref="TurnRate"/> as <see cref="Angle"/> units per tick (at least 1).</summary>
        public int TurnPerTick => DefChecks.TurnUnitsPerTick(TurnRate);

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Radius.Raw);
            hash.Add(WalkSpeed.Raw);
            hash.Add(RunSpeed.Raw);
            hash.Add(TurnRate.Raw);
            Kit.AddTo(ref hash);
        }
    }
}
