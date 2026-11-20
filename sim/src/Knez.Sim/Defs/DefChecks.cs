using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Argument checks shared by the def constructors. Content loaders turn the exceptions into located content errors,
    /// so messages are for people editing JSON and carry no parameter name (Mono and CoreCLR format that differently).
    /// </summary>
    internal static class DefChecks
    {
        public static string Id(string? id, string parameter)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException($"{parameter} must be a non-empty string.");
            }

            return id!;
        }

        public static Fix PositiveMetres(Fix value, int maxMetres, string parameter) => Positive(value, maxMetres, "m", parameter);

        public static Fix Positive(Fix value, int max, string unit, string parameter)
        {
            if (value <= Fix.Zero || value > Fix.FromInt(max))
            {
                throw new ArgumentException($"{parameter} must be > 0 and <= {max} {unit} (got {value}).");
            }

            return value;
        }

        public static Fix Speed(Fix value, string parameter)
        {
            if (value < FighterDef.MinSpeed || value > Fix.FromInt(FighterDef.MaxSpeed))
            {
                throw new ArgumentException($"{parameter} must be 0.1..{FighterDef.MaxSpeed} m/s (got {value}).");
            }

            return value;
        }

        /// <summary>Degrees per second → <see cref="Angle"/> units per tick, rounded, at least 1.</summary>
        public static int TurnUnitsPerTick(Fix degreesPerSecond)
        {
            // units/tick = deg/s × 65536/360 / 20 = Raw / 7200 (Raw = deg × 65536); round half up, positive input.
            long perTick = (degreesPerSecond.Raw + 3600) / (360L * BattleState.TicksPerSecond);
            return perTick < 1 ? 1 : (int)perTick;
        }

        /// <summary>A fraction in (0, 1].</summary>
        public static Fix Fraction(Fix value, string parameter)
        {
            if (value <= Fix.Zero || value > Fix.One)
            {
                throw new ArgumentException($"{parameter} must be > 0 and <= 1 (got {value}).");
            }

            return value;
        }

        public static int Range(int value, int min, int max, string parameter)
        {
            if (value < min || value > max)
            {
                throw new ArgumentException($"{parameter} must be {min}..{max} (got {value}).");
            }

            return value;
        }
    }
}
