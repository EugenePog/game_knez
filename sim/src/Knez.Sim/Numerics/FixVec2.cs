using System;

namespace Knez.Sim.Numerics
{
    /// <summary>2D vector of <see cref="Fix"/> components; positions and velocities in metres on the battle map.</summary>
    public readonly struct FixVec2 : IEquatable<FixVec2>
    {
        /// <summary>(0, 0).</summary>
        public static readonly FixVec2 Zero = new FixVec2(Fix.Zero, Fix.Zero);

        /// <summary>X component.</summary>
        public readonly Fix X;

        /// <summary>Y component.</summary>
        public readonly Fix Y;

        /// <summary>Creates a vector.</summary>
        public FixVec2(Fix x, Fix y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Exact conversion from integer components.</summary>
        public static FixVec2 FromInts(int x, int y) => new FixVec2(Fix.FromInt(x), Fix.FromInt(y));

        /// <summary>Squared length (no square root). Each product is truncated to 1/65536, so prefer it for comparisons
        /// of distances well above a millimetre.</summary>
        public Fix LengthSquared => X * X + Y * Y;

        /// <summary>
        /// Exact length, rounded down to 1/65536. Computed from the raw squares (full precision), so even sub-millimetre
        /// vectors have a non-zero length. Valid while each component is below ~46 000.
        /// </summary>
        public Fix Length => Fix.FromRaw((long)FixMath.IntegerSqrt((ulong)(X.Raw * X.Raw) + (ulong)(Y.Raw * Y.Raw)));

        /// <summary>
        /// This vector shortened to at most <paramref name="maxLength"/> (direction kept), or unchanged when it is shorter.
        /// </summary>
        public FixVec2 ClampLength(Fix maxLength) => Length <= maxLength ? this : WithLength(maxLength);

        /// <summary>
        /// A vector in this direction with length <paramref name="length"/> (each component truncated toward zero; the zero
        /// vector stays zero). Scales with full 64-bit precision instead of through a rounded ratio, so a squad marching
        /// at 0.07 m/tick covers exactly that. Valid while |component| × length stays below ~2^63 raw.
        /// </summary>
        public FixVec2 WithLength(Fix length)
        {
            long current = Length.Raw;
            if (current == 0)
            {
                return Zero;
            }

            return new FixVec2(Fix.FromRaw(X.Raw * length.Raw / current), Fix.FromRaw(Y.Raw * length.Raw / current));
        }

        /// <summary>Dot product.</summary>
        public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Sum.</summary>
        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);

        /// <summary>Difference.</summary>
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);

        /// <summary>Negation.</summary>
        public static FixVec2 operator -(FixVec2 a) => new FixVec2(-a.X, -a.Y);

        /// <summary>Divide each component by an integer, truncating toward zero. Throws on division by zero.</summary>
        public static FixVec2 operator /(FixVec2 a, int divisor) => new FixVec2(a.X / divisor, a.Y / divisor);

        /// <summary>Scale by a scalar.</summary>
        public static FixVec2 operator *(FixVec2 a, Fix s) => new FixVec2(a.X * s, a.Y * s);

        /// <summary>Equality.</summary>
        public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;

        /// <summary>Inequality.</summary>
        public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

        /// <inheritdoc/>
        public bool Equals(FixVec2 other) => this == other;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is FixVec2 other && this == other;

        /// <inheritdoc/>
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397) ^ Y.GetHashCode(); // sim-purity: allow Fix.GetHashCode is a pure function of Raw

        /// <inheritdoc/>
        public override string ToString() => "(" + X.ToString() + ", " + Y.ToString() + ")";
    }
}
