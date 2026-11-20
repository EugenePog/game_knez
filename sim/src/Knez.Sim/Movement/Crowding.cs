using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Bodies don't overlap (battle-sim.md §10). Every living fighter is solid to every other, allies included. When two
    /// press into each other they share the push by weight: a man holding his ground — squad without orders, him within
    /// <see cref="MovementRules.HoldingReach"/> of his slot — weighs his formation's hold weight, anyone on the move weighs
    /// 1 (agreed 2026-11-20: standing gives way less). Pairs are visited in id order
    /// — all pairs, no spatial index: phase 1 battles have at most ~60 fighters, a few thousand checks per tick.
    /// </summary>
    internal static class Crowding
    {
        /// <summary>How hard <paramref name="fighter"/> is to push this tick.</summary>
        public static int Weight(BattleState state, Fighter fighter)
        {
            if (!fighter.IsAlive)
            {
                return 1; // never pushed, and a dead man's slot may no longer exist once squads are compacted (M3)
            }

            Squad? squad = fighter.SquadId.IsNone || fighter.SlotIndex < 0 ? null : state.FindSquad(fighter.SquadId);
            if (squad == null || squad.Order.Kind != SquadOrderKind.None)
            {
                return 1;
            }

            FixVec2 slot = Inside(state.Scenario.Map, SlotTracking.Position(squad, SlotTracking.Offset(squad, fighter)), fighter.Radius);
            return (slot - fighter.Position).Length <= MovementRules.HoldingReach ? squad.Formation.HoldWeight : 1;
        }

        /// <summary>
        /// <paramref name="point"/> moved inside the map far enough for a body of <paramref name="radius"/>. Slots past the
        /// edge (a line ordered against it) are reached here instead, so nobody leans into the edge forever.
        /// </summary>
        public static FixVec2 Inside(MapDef map, FixVec2 point, Fix radius) =>
            new FixVec2(FixMath.Clamp(point.X, radius, map.Width - radius), FixMath.Clamp(point.Y, radius, map.Height - radius));

        /// <summary>Fills <paramref name="weights"/> (reused) with each fighter's weight, by list index.</summary>
        public static void FillWeights(BattleState state, List<int> weights)
        {
            weights.Clear();
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                weights.Add(Weight(state, fighters[i]));
            }
        }

        /// <summary>
        /// Steering away from neighbours inside personal space: per neighbour, a share of the intrusion over
        /// <see cref="MovementRules.SeparationTicks"/>; the lighter (moving) man takes the larger share.
        /// </summary>
        public static FixVec2 Separation(BattleState state, List<int> weights, int index)
        {
            List<Fighter> fighters = state.FighterList;
            Fighter self = fighters[index];
            FixVec2 sum = FixVec2.Zero;
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (j == index || !other.IsAlive)
                {
                    continue;
                }

                Fix reach = self.Radius + other.Radius + MovementRules.PersonalSpace;
                FixVec2 away = self.Position - other.Position;
                if (FixMath.Abs(away.X) >= reach || FixMath.Abs(away.Y) >= reach)
                {
                    continue;
                }

                Fix distance = away.Length;
                if (distance >= reach)
                {
                    continue;
                }

                // Equal weights → a full share (2·w/2w = 1); holding 3 vs moving 1 → ½ for the holder, 1½ for the mover.
                int total = weights[index] + weights[j];
                Fix push = (reach - distance) * (2 * weights[j]) / (total * MovementRules.SeparationTicks);
                sum += Apart(away, distance, push, negativeX: index < j); // same spot: the lower id goes west
            }

            return sum;
        }

        /// <summary>
        /// Men don't walk into bodies on purpose: for each neighbour inside personal space, the part of
        /// <paramref name="velocity"/> heading into him is cut by his share of their weights — half between equals (they
        /// jostle), three quarters for a mover against a holder (he mostly slides along the line, pushing with a quarter).
        /// Without this, sustained pressure leaves bodies pressed together that relaxation can't fully part.
        /// </summary>
        public static FixVec2 Slide(BattleState state, List<int> weights, int index, FixVec2 velocity)
        {
            List<Fighter> fighters = state.FighterList;
            Fighter self = fighters[index];
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (j == index || !other.IsAlive)
                {
                    continue;
                }

                Fix reach = self.Radius + other.Radius + MovementRules.PersonalSpace;
                FixVec2 toward = other.Position - self.Position;
                if (FixMath.Abs(toward.X) >= reach || FixMath.Abs(toward.Y) >= reach)
                {
                    continue;
                }

                Fix distance = toward.Length;
                if (distance >= reach || distance == Fix.Zero)
                {
                    continue;
                }

                Fix inward = FixVec2.Dot(velocity, toward) / distance;
                if (inward <= Fix.Zero)
                {
                    continue;
                }

                Fix cut = inward * weights[j] / (weights[index] + weights[j]);
                velocity -= toward.WithLength(cut);
            }

            return velocity;
        }

        /// <summary>
        /// Pushes overlapping living fighters apart until no pair overlaps (up to <see cref="MovementRules.CollisionPasses"/>
        /// passes over all pairs in id order). Both bodies of a pushed pair are put back inside the map at once — clamping
        /// only at the end would push men at the edge back into their neighbours — and finally every body is.
        /// </summary>
        public static void Resolve(BattleState state, List<int> weights)
        {
            MapDef map = state.Scenario.Map;
            List<Fighter> fighters = state.FighterList;
            for (int pass = 0; pass < MovementRules.CollisionPasses; pass++)
            {
                bool moved = false;
                for (int i = 0; i < fighters.Count; i++)
                {
                    Fighter a = fighters[i];
                    if (!a.IsAlive)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < fighters.Count; j++)
                    {
                        Fighter b = fighters[j];
                        if (b.IsAlive && Separate(map, a, b, weights[i], weights[j]))
                        {
                            moved = true;
                        }
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (fighter.IsAlive)
                {
                    fighter.Position = Inside(map, fighter.Position, fighter.Radius);
                }
            }
        }

        /// <summary>
        /// Pushes <paramref name="a"/> and <paramref name="b"/> apart by their overlap and keeps both on the map; true if
        /// they overlapped.
        /// </summary>
        private static bool Separate(MapDef map, Fighter a, Fighter b, int weightA, int weightB)
        {
            Fix minimum = a.Radius + b.Radius;
            FixVec2 apart = b.Position - a.Position; // from a toward b
            if (FixMath.Abs(apart.X) >= minimum || FixMath.Abs(apart.Y) >= minimum)
            {
                return false;
            }

            Fix distance = apart.Length;
            if (distance >= minimum)
            {
                return false;
            }

            Fix overlap = minimum - distance;
            Fix shareA = overlap * weightB / (weightA + weightB); // the heavier one moves less
            Fix shareB = overlap - shareA;
            // On the same spot Apart gives +X; the -= sends a (the lower id) west and the += sends b east.
            a.Position = Inside(map, a.Position - Apart(apart, distance, shareA, negativeX: false), a.Radius);
            b.Position = Inside(map, b.Position + Apart(apart, distance, shareB, negativeX: false), b.Radius);
            return true;
        }

        /// <summary>
        /// <paramref name="direction"/> scaled to <paramref name="length"/>. Two bodies on exactly the same spot have no
        /// direction: they part along X — this returns −X when <paramref name="negativeX"/>, else +X, and callers arrange
        /// that the lower id goes west (deterministic, and the two sides stay opposite).
        /// </summary>
        private static FixVec2 Apart(FixVec2 direction, Fix distance, Fix length, bool negativeX)
        {
            if (distance == Fix.Zero)
            {
                return new FixVec2(negativeX ? -length : length, Fix.Zero);
            }

            return direction.WithLength(length);
        }
    }
}
