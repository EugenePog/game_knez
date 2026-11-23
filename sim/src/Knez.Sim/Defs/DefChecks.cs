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
        /// <summary>A whole in permille.</summary>
        public const int Whole = 1000;

        /// <summary>A permille value, 0..1000.</summary>
        public static int Permille(int value, string parameter) => Range(value, 0, Whole, parameter);

        /// <summary>A positive decimal fraction rounded to the nearest raw unit, as the content reader rounds decimals
        /// (for code copies of content values).</summary>
        public static Fix Decimal(long numerator, long denominator) =>
            Fix.FromRaw(((numerator * Fix.OneRaw * 2) + denominator) / (2 * denominator));

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

        public static Fix NonNegative(Fix value, int max, string unit, string parameter)
        {
            if (value < Fix.Zero || value > Fix.FromInt(max))
            {
                throw new ArgumentException($"{parameter} must be 0..{max}{(unit.Length > 0 ? " " + unit : string.Empty)} (got {value}).");
            }

            return value;
        }

        /// <summary>Seconds → whole ticks (rounded to the nearest), at least 1 and at most <paramref name="maxSeconds"/>.</summary>
        public static int Ticks(Fix seconds, int maxSeconds, string parameter)
        {
            long ticks = ((seconds.Raw * BattleState.TicksPerSecond) + (Fix.OneRaw / 2)) >> Fix.FractionBits;
            if (seconds <= Fix.Zero || ticks < 1 || seconds > Fix.FromInt(maxSeconds))
            {
                throw new ArgumentException($"{parameter} must be 0.05..{maxSeconds} s, rounded to whole 0.05 s ticks (got {seconds}).");
            }

            return (int)ticks;
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
