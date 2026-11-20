using System;
using System.Globalization;

namespace Knez.Sim.Numerics
{
    /// <summary>
    /// A direction as a 16-bit binary angle: 65536 units per turn, wrapping naturally (battle-sim.md §5).
    /// 0 = +X (east), counter-clockwise, so 90° = +Y (north).
    /// </summary>
    public readonly struct Angle : IEquatable<Angle>
    {
        /// <summary>Units in a full turn.</summary>
        public const int UnitsPerTurn = 65536;

        /// <summary>0° (east).</summary>
        public static readonly Angle Zero = new Angle(0);

        /// <summary>The binary angle, 0..65535.</summary>
        public readonly ushort Raw;

        private Angle(ushort raw)
        {
            Raw = raw;
        }

        /// <summary>Wraps any integer into a turn.</summary>
        public static Angle FromRaw(int raw) => new Angle(unchecked((ushort)raw));

        /// <summary>Whole degrees, rounded to the nearest unit (ties away from zero), wrapped.</summary>
        public static Angle FromDegrees(int degrees) =>
            // Whole turns drop out first (360° is exactly 65536 units), so any int stays in range; the remainder keeps
            // the input's sign, so rounding stays symmetric.
            FromRaw((int)RoundDiv((long)(degrees % 360) * UnitsPerTurn, 360));

        /// <summary>Fractional degrees, rounded to the nearest unit (ties away from zero), wrapped. Integer math only.</summary>
        public static Angle FromDegrees(Fix degrees)
        {
            // units = degrees × 65536 / 360 = Raw / 360, since Raw = degrees × 65536. Whole turns drop out (360° is
            // exactly 65536 units), and the remainder keeps the sign of the input, so rounding stays symmetric.
            long withinTurn = degrees.Raw % (360L * Fix.OneRaw);
            return FromRaw((int)RoundDiv(withinTurn, 360));
        }

        /// <summary>Unit vector pointing along this angle.</summary>
        public FixVec2 Forward => new FixVec2(FixMath.Cos(this), FixMath.Sin(this));

        /// <summary>
        /// The shortest signed turn from this angle to <paramref name="target"/>, in units: −32768..32767, positive =
        /// counter-clockwise. Exactly half a turn comes out as −32768 (clockwise), so the choice is deterministic.
        /// </summary>
        public int DeltaTo(Angle target) => unchecked((short)(target.Raw - Raw));

        /// <summary>This angle turned toward <paramref name="target"/> by at most <paramref name="maxStep"/> units the short way.</summary>
        public Angle TurnToward(Angle target, int maxStep)
        {
            int delta = DeltaTo(target);
            if (delta > maxStep)
            {
                delta = maxStep;
            }
            else if (delta < -maxStep)
            {
                delta = -maxStep;
            }

            return FromRaw(Raw + delta);
        }

        /// <summary>Sum, wrapped.</summary>
        public static Angle operator +(Angle a, Angle b) => FromRaw(a.Raw + b.Raw);

        /// <summary>Difference, wrapped.</summary>
        public static Angle operator -(Angle a, Angle b) => FromRaw(a.Raw - b.Raw);

        /// <summary>The opposite rotation.</summary>
        public static Angle operator -(Angle a) => FromRaw(-a.Raw);

        /// <summary>Equality.</summary>
        public static bool operator ==(Angle a, Angle b) => a.Raw == b.Raw;

        /// <summary>Inequality.</summary>
        public static bool operator !=(Angle a, Angle b) => a.Raw != b.Raw;

        /// <inheritdoc/>
        public bool Equals(Angle other) => Raw == other.Raw;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Angle other && Raw == other.Raw;

        /// <inheritdoc/>
        public override int GetHashCode() => Raw;

        /// <summary>Degrees with two decimals (truncated), computed with integers only, e.g. "22.50°".</summary>
        public override string ToString()
        {
            long hundredths = (long)Raw * 36000 / UnitsPerTurn;
            return (hundredths / 100).ToString(CultureInfo.InvariantCulture) + "." +
                   (hundredths % 100).ToString("D2", CultureInfo.InvariantCulture) + "°";
        }

        /// <summary>value / divisor rounded to nearest, ties away from zero; divisor &gt; 0.</summary>
        private static long RoundDiv(long value, long divisor) =>
            value >= 0 ? (value + divisor / 2) / divisor : -((-value + divisor / 2) / divisor);
    }
}
