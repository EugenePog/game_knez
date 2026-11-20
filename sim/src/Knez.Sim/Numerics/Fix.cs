using System;
using System.Globalization;

namespace Knez.Sim.Numerics
{
    /// <summary>
    /// Q48.16 fixed-point number over a <see cref="long"/>: the sim's only non-integer number type (ADR-0002).
    /// Resolution 1/65536 (≈ 0.015 mm when used for metres). Results are bit-identical on every CPU and runtime.
    /// </summary>
    /// <remarks>
    /// Multiplication uses a plain 64-bit product, valid while |a × b| stays below ~2^31 (|a|, |b| below ~46 000);
    /// keep game values in that range. Debug builds throw on overflow. Multiplication and division both truncate
    /// toward zero, so results are symmetric around zero (battle-sim.md §5): a mirrored scenario gives mirrored
    /// numbers, and a damped negative value decays to 0 just like a positive one.
    /// Division by zero always throws, on every runtime (IL2CPP does not check integer division by itself).
    /// </remarks>
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        /// <summary>Number of fractional bits.</summary>
        public const int FractionBits = 16;

        /// <summary>Raw value of 1.0.</summary>
        public const long OneRaw = 1L << FractionBits;

        /// <summary>0.</summary>
        public static readonly Fix Zero = new Fix(0);

        /// <summary>1.</summary>
        public static readonly Fix One = new Fix(OneRaw);

        /// <summary>0.5.</summary>
        public static readonly Fix Half = new Fix(OneRaw / 2);

        /// <summary>Largest representable value.</summary>
        public static readonly Fix MaxValue = new Fix(long.MaxValue);

        /// <summary>Smallest representable value.</summary>
        public static readonly Fix MinValue = new Fix(long.MinValue);

        /// <summary>The underlying scaled integer: value × 65536.</summary>
        public readonly long Raw;

        private Fix(long raw)
        {
            Raw = raw;
        }

        /// <summary>Wraps an already-scaled raw value.</summary>
        public static Fix FromRaw(long raw) => new Fix(raw);

        /// <summary>Exact conversion from an integer.</summary>
        public static Fix FromInt(int value) => new Fix((long)value << FractionBits);

        /// <summary><paramref name="numerator"/> / <paramref name="denominator"/>, truncated toward zero. Ratio(3, 2) = 1.5.</summary>
        public static Fix Ratio(long numerator, long denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException();
            }

            return new Fix((numerator << FractionBits) / denominator);
        }

        /// <summary>Largest integer not greater than this value.</summary>
        public int FloorToInt() => (int)(Raw >> FractionBits);

        /// <summary>Sum.</summary>
        public static Fix operator +(Fix a, Fix b) => new Fix(a.Raw + b.Raw);

        /// <summary>Difference.</summary>
        public static Fix operator -(Fix a, Fix b) => new Fix(a.Raw - b.Raw);

        /// <summary>Negation.</summary>
        public static Fix operator -(Fix a) => new Fix(-a.Raw);

        /// <summary>Product, truncated toward zero.</summary>
        public static Fix operator *(Fix a, Fix b)
        {
            long product = a.Raw * b.Raw;
            return new Fix(product >= 0 ? product >> FractionBits : -((-product) >> FractionBits));
        }

        /// <summary>Product with an integer (exact).</summary>
        public static Fix operator *(Fix a, int b) => new Fix(a.Raw * b);

        /// <summary>Quotient, truncated toward zero. Throws on division by zero.</summary>
        public static Fix operator /(Fix a, Fix b)
        {
            if (b.Raw == 0)
            {
                throw new DivideByZeroException();
            }

            return new Fix((a.Raw << FractionBits) / b.Raw);
        }

        /// <summary>Quotient by an integer, truncated toward zero. Throws on division by zero.</summary>
        public static Fix operator /(Fix a, int b)
        {
            if (b == 0)
            {
                throw new DivideByZeroException();
            }

            return new Fix(a.Raw / b);
        }

        /// <summary>Equality.</summary>
        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;

        /// <summary>Inequality.</summary>
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;

        /// <summary>Less than.</summary>
        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;

        /// <summary>Greater than.</summary>
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;

        /// <summary>Less than or equal.</summary>
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;

        /// <summary>Greater than or equal.</summary>
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

        /// <inheritdoc/>
        public bool Equals(Fix other) => Raw == other.Raw;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Fix other && Raw == other.Raw;

        /// <inheritdoc/>
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked((int)Raw ^ (int)(Raw >> 32));

        /// <summary>Decimal text with 5 fractional digits (truncated), computed with integers only.</summary>
        public override string ToString()
        {
            ulong magnitude = Raw < 0 ? unchecked((ulong)-Raw) : (ulong)Raw;
            ulong whole = magnitude >> FractionBits;
            ulong fraction = ((magnitude & (OneRaw - 1)) * 100000UL) >> FractionBits;
            string sign = Raw < 0 ? "-" : string.Empty;
            return sign + whole.ToString(CultureInfo.InvariantCulture) + "." + fraction.ToString("D5", CultureInfo.InvariantCulture);
        }
    }
}
