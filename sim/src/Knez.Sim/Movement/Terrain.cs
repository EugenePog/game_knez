using System;
using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Bodies against the map edge and walls (battle-sim.md §10). Walls are axis-aligned rectangles; a body is a circle,
    /// so it slides round wall corners rather than catching on them. All checks go over every wall in file order.
    /// </summary>
    internal static class Terrain
    {
        /// <summary>Rounds of pushing out of walls; corners between touching walls can need a second or third.</summary>
        private const int WallIterations = 3;

        /// <summary>
        /// The nearest point to <paramref name="point"/> where a body of <paramref name="radius"/> fits: inside the map and
        /// clear of every wall. Used for bodies and for order targets inside a wall (moved to the nearest open ground,
        /// decided 2026-11-21).
        /// </summary>
        public static FixVec2 Constrain(MapDef map, FixVec2 point, Fix radius) =>
            Fit(map, point, radius, hasToward: false, toward: FixVec2.Zero);

        /// <summary>
        /// Like <see cref="Constrain"/>, but a point inside a wall leaves by the side on the shortest way to
        /// <paramref name="toward"/> (the squad's anchor). Used for slots: a man whose slot falls inside a wall stands as
        /// close as he can (decided 2026-11-21) on his squad's side of it, not on the far side of a palisade.
        /// </summary>
        public static FixVec2 ConstrainToward(MapDef map, FixVec2 point, Fix radius, FixVec2 toward) =>
            Fit(map, point, radius, hasToward: true, toward: toward);

        /// <summary>
        /// Exact distance from <paramref name="point"/> to the nearest wall or map edge, metres; zero inside a wall, on
        /// its outline or off the map. Route planning measures room with it (<see cref="NavGrid"/>).
        /// </summary>
        public static Fix Clearance(MapDef map, FixVec2 point)
        {
            Fix best = FixMath.Min(FixMath.Min(point.X, point.Y), FixMath.Min(map.Width - point.X, map.Height - point.Y));
            if (best <= Fix.Zero)
            {
                return Fix.Zero;
            }

            IReadOnlyList<WallDef> walls = map.Walls;
            for (int w = 0; w < walls.Count; w++)
            {
                FixVec2 away = point - walls[w].Closest(point);
                if (away == FixVec2.Zero)
                {
                    return Fix.Zero;
                }

                if (FixMath.Abs(away.X) >= best || FixMath.Abs(away.Y) >= best)
                {
                    continue;
                }

                best = FixMath.Min(best, away.Length);
            }

            return best;
        }

        /// <summary>
        /// True when every point of the segment <paramref name="from"/>–<paramref name="to"/> sampled every
        /// <see cref="MovementRules.RouteSampleStep"/> has at least <paramref name="clearance"/> of room. Clearance changes
        /// by at most the distance moved, so between samples the room can only be half a step less than this.
        /// </summary>
        public static bool SegmentClear(MapDef map, FixVec2 from, FixVec2 to, Fix clearance)
        {
            FixVec2 delta = to - from;
            long samples = delta.Length.Raw / MovementRules.RouteSampleStep.Raw + 1;
            for (long i = 0; i <= samples; i++)
            {
                var point = new FixVec2(
                    from.X + Fix.FromRaw(delta.X.Raw * i / samples),
                    from.Y + Fix.FromRaw(delta.Y.Raw * i / samples));
                if (Clearance(map, point) < clearance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// How far from <paramref name="point"/> in grid direction (<paramref name="dx"/>, <paramref name="dy"/>) — each
        /// −1, 0 or 1 — the first wall or the map edge is, metres, at most <paramref name="cap"/>; 0 inside a wall. Exact
        /// on raw values; a ray running along a face isn't stopped by it.
        /// </summary>
        public static Fix Ray(MapDef map, FixVec2 point, int dx, int dy, Fix cap)
        {
            long px = point.X.Raw;
            long py = point.Y.Raw;
            long t = long.MaxValue; // along the unnormalised step (dx, dy)
            if (dx != 0)
            {
                t = Math.Min(t, dx > 0 ? map.Width.Raw - px : px);
            }

            if (dy != 0)
            {
                t = Math.Min(t, dy > 0 ? map.Height.Raw - py : py);
            }

            IReadOnlyList<WallDef> walls = map.Walls;
            for (int w = 0; w < walls.Count && t > 0; w++)
            {
                WallDef wall = walls[w];
                if (!RaySlab(px, dx, wall.Min.X.Raw, wall.Max.X.Raw, out long loX, out long hiX)
                    || !RaySlab(py, dy, wall.Min.Y.Raw, wall.Max.Y.Raw, out long loY, out long hiY))
                {
                    continue;
                }

                long enter = Math.Max(loX, loY);
                long exit = Math.Min(hiX, hiY);
                if (enter < exit && exit > 0)
                {
                    t = Math.Min(t, Math.Max(enter, 0));
                }
            }

            Fix distance = Fix.FromRaw(t < 0 ? 0 : Math.Min(t, cap.Raw));
            if (dx != 0 && dy != 0)
            {
                distance = FixMath.Min(distance * Sqrt2, cap); // a diagonal step is √2 long
            }

            return distance;
        }

        private static readonly Fix Sqrt2 = Fix.FromRaw(92682);

        /// <summary>The part of a ray p + t·d (d = −1, 0 or 1) strictly inside [min, max] on one axis; false if none.</summary>
        private static bool RaySlab(long p, int d, long min, long max, out long lo, out long hi)
        {
            if (d == 0)
            {
                lo = long.MinValue;
                hi = long.MaxValue;
                return p > min && p < max;
            }

            lo = d > 0 ? min - p : p - max;
            hi = d > 0 ? max - p : p - min;
            return true;
        }

        /// <summary>
        /// True when the segment <paramref name="from"/>–<paramref name="to"/> passes through the inside of a wall. Exact
        /// (slab test on raw values, no division); running along a face or touching a corner doesn't count.
        /// </summary>
        public static bool CrossesWall(MapDef map, FixVec2 from, FixVec2 to)
        {
            IReadOnlyList<WallDef> walls = map.Walls;
            for (int w = 0; w < walls.Count; w++)
            {
                WallDef wall = walls[w];
                long enterN = 0, enterD = 1, exitN = 1, exitD = 1; // the part of the segment inside, as t = n / d in [0, 1]
                if (Slab(from.X.Raw, to.X.Raw - from.X.Raw, wall.Min.X.Raw, wall.Max.X.Raw, ref enterN, ref enterD, ref exitN, ref exitD)
                    && Slab(from.Y.Raw, to.Y.Raw - from.Y.Raw, wall.Min.Y.Raw, wall.Max.Y.Raw, ref enterN, ref enterD, ref exitN, ref exitD)
                    && enterN * exitD < exitN * enterD)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>How deep a body of <paramref name="radius"/> at <paramref name="point"/> is inside the deepest wall (0 if clear).</summary>
        public static Fix WallDepth(MapDef map, FixVec2 point, Fix radius)
        {
            Fix deepest = Fix.Zero;
            IReadOnlyList<WallDef> walls = map.Walls;
            for (int w = 0; w < walls.Count; w++)
            {
                WallDef wall = walls[w];
                if (wall.Contains(point))
                {
                    return radius + Fix.One; // centre inside: deeper than any edge contact
                }

                FixVec2 away = point - wall.Closest(point);
                if (FixMath.Abs(away.X) >= radius || FixMath.Abs(away.Y) >= radius)
                {
                    continue;
                }

                deepest = FixMath.Max(deepest, radius - away.Length);
            }

            return deepest;
        }

        /// <summary>
        /// <paramref name="velocity"/> with the part heading into any wall within personal space removed: men slide along
        /// walls instead of walking into them (walls never give way).
        /// </summary>
        public static FixVec2 SlideAlongWalls(MapDef map, Fix personalSpace, FixVec2 position, Fix radius, FixVec2 velocity)
        {
            IReadOnlyList<WallDef> walls = map.Walls;
            Fix reach = radius + personalSpace;
            for (int w = 0; w < walls.Count; w++)
            {
                WallDef wall = walls[w];
                FixVec2 away = position - wall.Closest(position); // from the wall toward the body
                if (FixMath.Abs(away.X) >= reach || FixMath.Abs(away.Y) >= reach)
                {
                    continue;
                }

                Fix distance = away.Length;
                if (distance >= reach || distance == Fix.Zero)
                {
                    continue; // far enough, or the centre is inside the wall: collision pushes him out
                }

                Fix into = -FixVec2.Dot(velocity, away) / distance;
                if (into > Fix.Zero)
                {
                    velocity += away.WithLength(into);
                }
            }

            return velocity;
        }

        private static FixVec2 Fit(MapDef map, FixVec2 point, Fix radius, bool hasToward, FixVec2 toward)
        {
            FixVec2 p = OnMap(map, point, radius);
            IReadOnlyList<WallDef> walls = map.Walls;
            for (int round = 0; round < WallIterations; round++)
            {
                bool moved = false;
                for (int w = 0; w < walls.Count; w++)
                {
                    // Inside or exactly on the outline, there is no "away from the wall" to push along: pick a side.
                    FixVec2 pushed = walls[w].Closest(p) == p
                        ? LeaveWall(map, w, p, radius, hasToward, toward)
                        : PushOut(walls[w], p, radius);
                    if (pushed != p)
                    {
                        p = pushed;
                        moved = true;
                    }
                }

                if (!moved)
                {
                    break;
                }

                p = OnMap(map, p, radius);
            }

            return p;
        }

        /// <summary>
        /// Narrows [enter, exit] to where p + t·d lies strictly between <paramref name="min"/> and <paramref name="max"/>
        /// on one axis; false when it never does. Fractions keep positive denominators; products stay far below 2^63
        /// on maps of at most 250 m.
        /// </summary>
        private static bool Slab(long p, long d, long min, long max, ref long enterN, ref long enterD, ref long exitN, ref long exitD)
        {
            if (d == 0)
            {
                return p > min && p < max;
            }

            long lo = min - p;
            long hi = max - p;
            if (d < 0)
            {
                long swap = -lo;
                lo = -hi;
                hi = swap;
                d = -d;
            }

            if (lo * enterD > enterN * d)
            {
                enterN = lo;
                enterD = d;
            }

            if (hi * exitD < exitN * d)
            {
                exitN = hi;
                exitD = d;
            }

            return true;
        }

        private static FixVec2 OnMap(MapDef map, FixVec2 p, Fix r) =>
            new FixVec2(FixMath.Clamp(p.X, r, map.Width - r), FixMath.Clamp(p.Y, r, map.Height - r));

        /// <summary>
        /// A centre <paramref name="p"/> inside wall <paramref name="index"/> or on its outline leaves by one of its four sides, to
        /// just clear of it. Only exits where a body fits on the map and whose centre is inside no other wall count: the
        /// nearest side can be flush with the map edge or with a neighbouring wall. Of those, the nearest, or with
        /// <paramref name="hasToward"/> the one on the shortest way to <paramref name="toward"/>; ties go west, east,
        /// south, north. With no such exit, the nearest side anyway, and the caller's next round deals with what's there.
        /// </summary>
        private static FixVec2 LeaveWall(MapDef map, int index, FixVec2 p, Fix r, bool hasToward, FixVec2 toward)
        {
            WallDef wall = map.Walls[index];
            FixVec2 nearest = p;
            Fix nearestDepth = Fix.MaxValue;
            FixVec2 best = p;
            Fix bestScore = Fix.MaxValue;
            bool found = false;
            for (int side = 0; side < 4; side++)
            {
                FixVec2 exit = Exit(wall, p, r, side, out Fix depth);
                if (depth < nearestDepth)
                {
                    nearest = exit;
                    nearestDepth = depth;
                }

                if (!Open(map, index, exit, r))
                {
                    continue;
                }

                // The way out is depth + r long on every side; r is common to all four, so it drops out.
                Fix score = hasToward ? depth + (toward - exit).Length : depth;
                if (score < bestScore)
                {
                    best = exit;
                    bestScore = score;
                    found = true;
                }
            }

            return found ? best : nearest;
        }

        /// <summary>The point just clear of <paramref name="wall"/>'s side <paramref name="side"/> (0 west, 1 east,
        /// 2 south, 3 north) straight out from <paramref name="p"/>, and how deep <paramref name="p"/> lies behind that side.</summary>
        private static FixVec2 Exit(WallDef wall, FixVec2 p, Fix r, int side, out Fix depth)
        {
            switch (side)
            {
                case 0:
                    depth = p.X - wall.Min.X;
                    return new FixVec2(wall.Min.X - r, p.Y);
                case 1:
                    depth = wall.Max.X - p.X;
                    return new FixVec2(wall.Max.X + r, p.Y);
                case 2:
                    depth = p.Y - wall.Min.Y;
                    return new FixVec2(p.X, wall.Min.Y - r);
                default:
                    depth = wall.Max.Y - p.Y;
                    return new FixVec2(p.X, wall.Max.Y + r);
            }
        }

        /// <summary>True when a body of radius <paramref name="r"/> at <paramref name="p"/> fits on the map and its centre
        /// is inside no wall other than wall <paramref name="except"/>.</summary>
        private static bool Open(MapDef map, int except, FixVec2 p, Fix r)
        {
            if (p.X < r || p.Y < r || p.X > map.Width - r || p.Y > map.Height - r)
            {
                return false;
            }

            IReadOnlyList<WallDef> walls = map.Walls;
            for (int w = 0; w < walls.Count; w++)
            {
                if (w != except && walls[w].Contains(p))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Moves a body of radius <paramref name="r"/> at <paramref name="p"/>, its centre off
        /// <paramref name="wall"/> (neither inside nor on the outline), straight away from the wall to just clear of it.</summary>
        private static FixVec2 PushOut(WallDef wall, FixVec2 p, Fix r)
        {
            FixVec2 closest = wall.Closest(p);
            FixVec2 away = p - closest;
            if (FixMath.Abs(away.X) >= r || FixMath.Abs(away.Y) >= r)
            {
                return p;
            }

            // away is non-zero here, so its length is too (Length keeps sub-millimetre vectors non-zero).
            return away.Length >= r ? p : closest + away.WithLength(r);
        }
    }
}
