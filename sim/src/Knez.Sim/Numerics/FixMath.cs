using System;

namespace Knez.Sim.Numerics
{
    /// <summary>Deterministic math on <see cref="Fix"/> — integer algorithms only, no floating point (ADR-0002).</summary>
    public static class FixMath
    {
        /// <summary>Absolute value.</summary>
        public static Fix Abs(Fix value) => value.Raw < 0 ? -value : value;

        /// <summary>Smaller of two values.</summary>
        public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;

        /// <summary>Larger of two values.</summary>
        public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;

        /// <summary><paramref name="value"/> limited to [<paramref name="min"/>, <paramref name="max"/>].</summary>
        public static Fix Clamp(Fix value, Fix min, Fix max) => value.Raw < min.Raw ? min : value.Raw > max.Raw ? max : value;

        /// <summary>Largest raw value <see cref="Sqrt"/> accepts (value 2^32): beyond it the internal shift overflows.</summary>
        public const long MaxSqrtRaw = (1L << 48) - 1;

        /// <summary>Square root, rounded down. Throws for negative input or value ≥ 2^32.</summary>
        public static Fix Sqrt(Fix value)
        {
            if (value.Raw < 0 || value.Raw > MaxSqrtRaw)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Sqrt needs 0 <= value < 2^32.");
            }

            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16)
            return Fix.FromRaw((long)IntegerSqrt((ulong)value.Raw << Fix.FractionBits));
        }

        /// <summary>
        /// Sine from the committed quarter-wave table with linear interpolation; within 2/65536 of the true value,
        /// exact at multiples of 90°, and exactly odd and mirror-symmetric (battle-sim.md §5).
        /// </summary>
        public static Fix Sin(Angle angle)
        {
            int quadrant = angle.Raw >> 14;
            int within = angle.Raw & (SineTable.QuarterTurn - 1);
            int magnitude = (quadrant & 1) == 0
                ? SineTable.QuarterSin(within)
                : SineTable.QuarterSin(SineTable.QuarterTurn - within);
            return Fix.FromRaw(quadrant < 2 ? magnitude : -magnitude);
        }

        /// <summary>Cosine: the sine a quarter turn later.</summary>
        public static Fix Cos(Angle angle) => Sin(Angle.FromRaw(angle.Raw + SineTable.QuarterTurn));

        /// <summary>Integer square root of a 64-bit value, rounded down (bit-by-bit method).</summary>
        public static ulong IntegerSqrt(ulong n)
        {
            ulong result = 0;
            ulong bit = 1UL << 62;
            while (bit > n)
            {
                bit >>= 2;
            }

            while (bit != 0)
            {
                if (n >= result + bit)
                {
                    n -= result + bit;
                    result = (result >> 1) + bit;
                }
                else
                {
                    result >>= 1;
                }

                bit >>= 2;
            }

            return result;
        }
    }
}
