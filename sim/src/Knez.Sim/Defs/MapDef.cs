using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable battle map: a rectangle from (0, 0) to (<see cref="Width"/>, <see cref="Height"/>) in metres, X east,
    /// Y north. Terrain (walkable grid, height, cover, line of sight) arrives in M1 step 5.
    /// </summary>
    public sealed class MapDef
    {
        /// <summary>Creates a map; both sides positive and at most <see cref="MaxSideMetres"/>.</summary>
        public MapDef(Fix width, Fix height)
        {
            Width = DefChecks.PositiveMetres(width, MaxSideMetres, nameof(width));
            Height = DefChecks.PositiveMetres(height, MaxSideMetres, nameof(height));
        }

        /// <summary>Largest map side; keeps squared distances well inside <see cref="Fix"/> range.</summary>
        public const int MaxSideMetres = 2000;

        /// <summary>Extent along X (east), metres.</summary>
        public Fix Width { get; }

        /// <summary>Extent along Y (north), metres.</summary>
        public Fix Height { get; }

        /// <summary>True when <paramref name="point"/> lies on the map (edges included).</summary>
        public bool Contains(FixVec2 point) =>
            point.X >= Fix.Zero && point.Y >= Fix.Zero && point.X <= Width && point.Y <= Height;

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Width.Raw);
            hash.Add(Height.Raw);
        }

        /// <summary>"60 × 40 m"-style text for messages.</summary>
        public override string ToString() => Width + " × " + Height + " m";
    }
}
