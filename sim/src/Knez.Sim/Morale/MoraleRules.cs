using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Morale
{
    /// <summary>The morale rules that don't need the whole tick (battle-sim.md §12): states, costs, safety, retreat.</summary>
    internal static class MoraleRules
    {
        /// <summary>
        /// The state a man in <paramref name="current"/> (steady, shaken or wavering) moves to at <paramref name="morale"/>:
        /// down at once when he drops below a threshold, up only once he is the hysteresis above it.
        /// </summary>
        public static MoraleState Next(MoraleDef rules, MoraleState current, int morale)
        {
            if (morale < rules.Routing)
            {
                return MoraleState.Routing;
            }

            MoraleState natural = morale >= rules.Shaken ? MoraleState.Steady : morale >= rules.Wavering ? MoraleState.Shaken : MoraleState.Wavering;
            if (Rank(natural) > Rank(current))
            {
                return natural;
            }

            MoraleState up = morale >= rules.Shaken + rules.Hysteresis ? MoraleState.Steady
                : morale >= rules.Wavering + rules.Hysteresis ? MoraleState.Shaken : MoraleState.Wavering;
            return Rank(up) < Rank(current) ? up : current;
        }

        /// <summary>What a fallen squadmate costs each man of a squad that started with <paramref name="startCount"/> men.</summary>
        public static int LossCost(MoraleDef rules, int startCount) =>
            startCount <= 0 ? 0 : (int)((rules.LossWeight.Raw * Whole / startCount) >> Fix.FractionBits);

        /// <summary>True when no living, non-routing enemy is within <see cref="MoraleDef.SafeDistance"/> of
        /// <paramref name="fighter"/> (edge to edge).</summary>
        public static bool Safe(BattleState state, Fighter fighter)
        {
            Fix safe = state.Scenario.Morale.SafeDistance;
            var fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter other = fighters[i];
                if (!other.IsAlive || other.IsRouting || other.SideId == fighter.SideId)
                {
                    continue;
                }

                Fix limit = safe + fighter.Radius + other.Radius;
                FixVec2 to = other.Position - fighter.Position;
                if (FixMath.Abs(to.X) <= limit && FixMath.Abs(to.Y) <= limit && to.Length <= limit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The point on his side's map edge a routing man runs for: straight across to the edge away from the enemy
        /// (<see cref="Edge"/>).
        /// </summary>
        public static FixVec2 RetreatPoint(BattleState state, Fighter fighter)
        {
            MapDef map = state.Scenario.Map;
            switch (Edge(state.Scenario, fighter.SideId))
            {
                case 0: return new FixVec2(fighter.Position.X, Fix.Zero);
                case 1: return new FixVec2(fighter.Position.X, map.Height);
                case 2: return new FixVec2(Fix.Zero, fighter.Position.Y);
                default: return new FixVec2(map.Width, fighter.Position.Y);
            }
        }

        /// <summary>
        /// Where a routing man heads this tick: his <see cref="RetreatPoint"/>, unless a wall stands between him and his
        /// edge — then he runs sideways round its nearer end (an end against the map edge doesn't count), and straight on
        /// once past it. Walls are axis-aligned boxes and edges axis-aligned lines, so "between" is a span test.
        /// </summary>
        public static FixVec2 FleeTarget(BattleState state, Fighter fighter)
        {
            MapDef map = state.Scenario.Map;
            int edge = Edge(state.Scenario, fighter.SideId);
            bool vertical = edge <= 1; // fleeing along y (south or north)
            bool down = edge == 0 || edge == 2; // toward 0
            Fix along = vertical ? fighter.Position.Y : fighter.Position.X;
            Fix across = vertical ? fighter.Position.X : fighter.Position.Y;
            Fix size = vertical ? map.Width : map.Height;
            Fix pad = fighter.Radius + state.Scenario.Movement.PersonalSpace;
            WallDef? block = null;
            Fix nearest = Fix.MaxValue;
            for (int i = 0; i < map.Walls.Count; i++)
            {
                WallDef wall = map.Walls[i];
                Fix lo = vertical ? wall.Min.X : wall.Min.Y;
                Fix hi = vertical ? wall.Max.X : wall.Max.Y;
                if (across + pad <= lo || across - pad >= hi)
                {
                    continue; // not across his path
                }

                Fix distance = down ? along - (vertical ? wall.Max.Y : wall.Max.X) : (vertical ? wall.Min.Y : wall.Min.X) - along;
                if (distance >= Fix.Zero && distance < nearest)
                {
                    nearest = distance;
                    block = wall;
                }
            }

            if (block == null)
            {
                return RetreatPoint(state, fighter);
            }

            Fix blockLo = vertical ? block.Min.X : block.Min.Y;
            Fix blockHi = vertical ? block.Max.X : block.Max.Y;
            Fix left = blockLo - pad - RoundSlack;
            Fix right = blockHi + pad + RoundSlack;
            bool leftOk = left >= fighter.Radius;
            bool rightOk = right <= size - fighter.Radius;
            if (!leftOk && !rightOk)
            {
                return RetreatPoint(state, fighter); // the wall closes the way: nothing better to do
            }

            Fix side = !rightOk || (leftOk && across - left <= right - across) ? left : right;
            return vertical ? new FixVec2(side, along) : new FixVec2(along, side);
        }

        /// <summary>True when a routing man has reached his side's map edge (as close as his body lets him).</summary>
        public static bool AtEdge(BattleState state, Fighter fighter) =>
            (RetreatPoint(state, fighter) - fighter.Position).Length <= fighter.Radius + EdgeSlack;

        /// <summary>
        /// A side's retreat edge — 0 south (y = 0), 1 north, 2 west (x = 0), 3 east: the edge away from the enemy, in the
        /// main direction from the enemies' start (the average of their squads' spawn points) to its own (ties: along y).
        /// With no enemy in the scenario, the edge nearest its own start.
        /// </summary>
        public static int Edge(ScenarioDef scenario, int side)
        {
            long ownX = 0, ownY = 0, enemyX = 0, enemyY = 0;
            int own = 0, enemies = 0;
            for (int i = 0; i < scenario.Squads.Count; i++)
            {
                SquadSpawnDef squad = scenario.Squads[i];
                if (squad.SideId == side)
                {
                    ownX += squad.Position.X.Raw;
                    ownY += squad.Position.Y.Raw;
                    own++;
                }
                else
                {
                    enemyX += squad.Position.X.Raw;
                    enemyY += squad.Position.Y.Raw;
                    enemies++;
                }
            }

            if (own == 0)
            {
                return 0;
            }

            Fix cx = Fix.FromRaw(ownX / own);
            Fix cy = Fix.FromRaw(ownY / own);
            if (enemies > 0)
            {
                Fix dx = cx - Fix.FromRaw(enemyX / enemies);
                Fix dy = cy - Fix.FromRaw(enemyY / enemies);
                if (dx != Fix.Zero || dy != Fix.Zero)
                {
                    if (FixMath.Abs(dy) >= FixMath.Abs(dx))
                    {
                        return dy < Fix.Zero ? 0 : 1;
                    }

                    return dx < Fix.Zero ? 2 : 3;
                }
            }

            Fix south = cy;
            Fix north = scenario.Map.Height - cy;
            Fix west = cx;
            Fix east = scenario.Map.Width - cx;
            Fix best = FixMath.Min(FixMath.Min(south, north), FixMath.Min(west, east));
            return best == south ? 0 : best == north ? 1 : best == west ? 2 : 3;
        }

        private const int Whole = 1000;

        private static readonly Fix EdgeSlack = Fix.Ratio(1, 10);

        private static readonly Fix RoundSlack = Fix.Ratio(1, 20);

        private static int Rank(MoraleState state) =>
            state == MoraleState.Steady ? 0 : state == MoraleState.Shaken ? 1 : state == MoraleState.Wavering ? 2 : 3;
    }
}
