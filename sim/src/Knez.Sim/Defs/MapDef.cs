using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable battle map: a rectangle from (0, 0) to (<see cref="Width"/>, <see cref="Height"/>) in metres, X east,
    /// Y north, with <see cref="Walls"/> no body can enter. Height, cover and line of sight come with combat.
    /// </summary>
    public sealed class MapDef
    {
        /// <summary>Creates a map; both sides positive and at most <see cref="MaxSideMetres"/>; walls lie on the map.</summary>
        public MapDef(Fix width, Fix height, IReadOnlyList<WallDef>? walls = null)
        {
            Width = DefChecks.PositiveMetres(width, MaxSideMetres, nameof(width));
            Height = DefChecks.PositiveMetres(height, MaxSideMetres, nameof(height));
            walls ??= Array.Empty<WallDef>();
            if (walls.Count > MaxWalls)
            {
                throw new ArgumentException($"at most {MaxWalls} walls (got {walls.Count}).");
            }

            var copy = new WallDef[walls.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                WallDef wall = walls[i] ?? throw new ArgumentNullException(nameof(walls), $"walls[{i}] is null.");
                if (!Contains(wall.Min) || !Contains(wall.Max))
                {
                    throw new ArgumentException($"walls[{i}]: {wall} reaches past the {Width} × {Height} m map.");
                }

                copy[i] = wall;
            }

            Walls = new ReadOnlyCollection<WallDef>(copy);
        }

        /// <summary>Sanity bound for <see cref="Walls"/>: every body checks every wall several times a tick, so tick time
        /// grows with it (no grid until pathfinding). A sanity bound, not a design number.</summary>
        public const int MaxWalls = 64;

        /// <summary>Walls in file order.</summary>
        public IReadOnlyList<WallDef> Walls { get; }

        /// <summary>Largest map side: 250 m, 500 route cells (a 250 × 250 m map has 250 000). A sanity bound for skirmish
        /// maps; it also keeps squared distances well inside <see cref="Fix"/> range.</summary>
        public const int MaxSideMetres = 250;

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
            hash.Add(Walls.Count);
            for (int i = 0; i < Walls.Count; i++)
            {
                Walls[i].AddTo(ref hash);
            }
        }

        /// <summary>"60 × 40 m"-style text for messages.</summary>
        public override string ToString() => Width + " × " + Height + " m";
    }
}
