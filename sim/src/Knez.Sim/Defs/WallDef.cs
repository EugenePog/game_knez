using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable wall: an axis-aligned rectangle no body can enter (palisade, building, fence; battle-sim.md §10), and what
    /// it stands for (<see cref="WallKind"/>: a house or an obstacle). Line of sight and cover will read the same
    /// rectangles when combat needs them.
    /// </summary>
    public sealed class WallDef
    {
        /// <summary>A wall between two opposite corners, given in any order; it must have some width and depth.</summary>
        public WallDef(FixVec2 from, FixVec2 to, WallKind kind = WallKind.Obstacle)
        {
            Kind = kind;
            Min = new FixVec2(FixMath.Min(from.X, to.X), FixMath.Min(from.Y, to.Y));
            Max = new FixVec2(FixMath.Max(from.X, to.X), FixMath.Max(from.Y, to.Y));
            if (Max.X - Min.X < MinThickness || Max.Y - Min.Y < MinThickness)
            {
                throw new ArgumentException($"a wall must be at least 0.1 m thick on both axes (got {from} to {to}).");
            }
        }

        /// <summary>Thinnest wall accepted: 10 cm. A body could step through a wall only by moving more than its radius
        /// plus half this per tick, which <see cref="BodyDef"/> refuses. A sanity bound.</summary>
        public static readonly Fix MinThickness = Fix.Ratio(1, 10);

        /// <summary>What it stands for: drawn by kind, the same to every rule today.</summary>
        public WallKind Kind { get; }

        /// <summary>South-west corner, metres.</summary>
        public FixVec2 Min { get; }

        /// <summary>North-east corner, metres.</summary>
        public FixVec2 Max { get; }

        /// <summary>True when <paramref name="point"/> lies strictly inside the wall.</summary>
        public bool Contains(FixVec2 point) => point.X > Min.X && point.X < Max.X && point.Y > Min.Y && point.Y < Max.Y;

        /// <summary>The point of the wall nearest to <paramref name="point"/> (the point itself when it is inside).</summary>
        public FixVec2 Closest(FixVec2 point) =>
            new FixVec2(FixMath.Clamp(point.X, Min.X, Max.X), FixMath.Clamp(point.Y, Min.Y, Max.Y));

        // Kind is left out on purpose, like a scenario's description: nothing in the battle reads it yet. The first rule that
        // does (cover, line of sight) adds it here — and re-records the goldens.
        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Min.X.Raw);
            hash.Add(Min.Y.Raw);
            hash.Add(Max.X.Raw);
            hash.Add(Max.Y.Raw);
        }

        /// <inheritdoc/>
        public override string ToString() => "wall " + Min + "–" + Max;
    }
}
