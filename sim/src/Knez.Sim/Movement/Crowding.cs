using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Bodies don't overlap (battle-sim.md §10). Every living fighter is solid to every other — enemies always, allies
    /// too, with one exception (decided 2026-11-21): a man holding his ground opens his ranks to a man of another allied
    /// squad who is passing through on orders (<see cref="Fighter.PassingThrough"/>) — walking to his slot, or closing in on an
    /// enemy (battle-sim.md §11). That pair ignores each other — no
    /// separation, no slide, no collision — and while their bodies overlap the passing man squeezes through at most at the
    /// holder formation's <see cref="FormationDef.AllyPassSpeed"/> of his walking pace. Passing starts only when he walks
    /// into a holder who stands between him and his slot, so men shoved backwards, or off their slots without orders,
    /// stay solid. Otherwise, when two press into each other they share the push by weight: a man holding his ground weighs
    /// his formation's hold weight, anyone else weighs 1 (agreed 2026-11-20: standing gives way less). Men of different
    /// allied squads more than 25 cm inside each other (they were passing through until an order changed) drift apart
    /// ~10 cm a tick instead of jumping. Pairs are visited in id order — all pairs, no spatial index: phase
    /// 1 battles have at most ~60 fighters, a few thousand checks per tick.
    /// </summary>
    internal static class Crowding
    {
        /// <summary>
        /// <paramref name="fighter"/>'s crowding role this tick: <see cref="CrowdRole.Ordered"/> while his squad has a move
        /// order; <see cref="CrowdRole.Holding"/> when it has none and he stands within
        /// <see cref="Defs.MovementDef.HoldingReach"/> of his slot; otherwise <see cref="CrowdRole.Loose"/>.
        /// </summary>
        public static CrowdRole Role(BattleState state, Fighter fighter)
        {
            if (!fighter.IsAlive || fighter.IsRouting)
            {
                // The running are on nobody's orders; the fallen are never pushed, and their slots may be gone.
                return CrowdRole.Loose;
            }

            Squad? squad = fighter.SquadId.IsNone || fighter.SlotIndex < 0 ? null : state.FindSquad(fighter.SquadId);
            if (squad == null)
            {
                return CrowdRole.Loose;
            }

            if (squad.Order.Kind != SquadOrderKind.None)
            {
                return CrowdRole.Ordered;
            }

            return DistanceToSlot(state, squad, fighter) <= state.Scenario.Movement.HoldingReach ? CrowdRole.Holding : CrowdRole.Loose;
        }

        /// <summary>True when <paramref name="fighter"/> holds his ground this tick (see <see cref="Role"/>).</summary>
        public static bool Holding(BattleState state, Fighter fighter) => Role(state, fighter) == CrowdRole.Holding;

        /// <summary>How far <paramref name="fighter"/> stands from his slot (the slot moved to where a body fits: on the
        /// map, clear of walls, on the squad's side of a wall; see <see cref="Terrain.ConstrainToward"/>).</summary>
        public static Fix DistanceToSlot(BattleState state, Squad squad, Fighter fighter)
        {
            FixVec2 raw = SlotTracking.Position(squad, SlotTracking.Offset(squad, fighter));
            FixVec2 slot = Terrain.ConstrainToward(state.Scenario.Map, raw, fighter.Radius, squad.Anchor);
            return (slot - fighter.Position).Length;
        }

        /// <summary>
        /// True when fighters <paramref name="i"/> and <paramref name="j"/> pass through each other: same side, different
        /// squads, one holding his ground and the other on orders and passing through (<see cref="Fighter.PassingThrough"/>).
        /// Enemies never do; neither do men of one squad, men without orders, or ordered men shoved backwards into allies.
        /// </summary>
        public static bool PassThrough(List<Fighter> fighters, List<CrowdRole> roles, int i, int j)
        {
            CrowdRole a = roles[i];
            CrowdRole b = roles[j];
            bool pair = (a == CrowdRole.Holding && b == CrowdRole.Ordered && fighters[j].PassingThrough)
                || (a == CrowdRole.Ordered && b == CrowdRole.Holding && fighters[i].PassingThrough);
            return pair && AlliedOtherSquad(fighters[i], fighters[j]);
        }

        private static bool AlliedOtherSquad(Fighter a, Fighter b) => a.SideId == b.SideId && a.SquadId != b.SquadId;

        /// <summary>
        /// Starts, keeps or ends each fighter's <see cref="Fighter.PassingThrough"/> for this tick, before steering uses it.
        /// An ordered man starts passing when he is within personal space of an allied holder (another squad) and his
        /// <paramref name="intents"/> — where following his slot would take him this tick — heads into that holder: he
        /// walks into him on purpose. He keeps passing while his body overlaps any allied holder or he walks into another
        /// one; otherwise, and for anyone not on orders, it ends. A man shoved backwards into allies is trying to walk the
        /// other way, so it never starts.
        /// </summary>
        public static void UpdatePassing(BattleState state, List<CrowdRole> roles, List<FixVec2> intents)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter self = fighters[i];
                if (roles[i] != CrowdRole.Ordered)
                {
                    self.PassingThrough = false;
                    continue;
                }

                FixVec2 intent = intents[i];
                bool inside = false;
                bool walkingInto = false;
                for (int j = 0; j < fighters.Count && !(inside && walkingInto); j++)
                {
                    Fighter other = fighters[j];
                    if (j == i || roles[j] != CrowdRole.Holding || !AlliedOtherSquad(self, other))
                    {
                        continue;
                    }

                    Fix touching = self.Radius + other.Radius;
                    Fix reach = touching + state.Scenario.Movement.PersonalSpace;
                    FixVec2 toOther = other.Position - self.Position;
                    if (FixMath.Abs(toOther.X) >= reach || FixMath.Abs(toOther.Y) >= reach)
                    {
                        continue;
                    }

                    Fix distance = toOther.Length;
                    inside |= distance < touching;
                    walkingInto |= distance < reach && FixVec2.Dot(intent, toOther) > Fix.Zero;
                }

                self.PassingThrough = walkingInto || (self.PassingThrough && inside);
            }
        }

        /// <summary>Fills <paramref name="weights"/> and <paramref name="roles"/> (both reused) per fighter, by list index.</summary>
        public static void Fill(BattleState state, List<int> weights, List<CrowdRole> roles)
        {
            weights.Clear();
            roles.Clear();
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                CrowdRole role = Role(state, fighters[i]);
                roles.Add(role);
                // Men holding their ground, and men in melee (battle-sim.md §11), weigh the formation's hold weight.
                Squad? squad = role == CrowdRole.Holding || fighters[i].IsEngaged ? state.FindSquad(fighters[i].SquadId) : null;
                weights.Add(squad != null ? squad.Formation.HoldWeight : 1);
            }
        }

        /// <summary>
        /// Steering away from neighbours inside personal space: per neighbour, a share of the intrusion over
        /// <see cref="Defs.MovementDef.SeparationTicks"/>; the lighter (moving) man takes the larger share.
        /// </summary>
        public static FixVec2 Separation(BattleState state, List<int> weights, List<CrowdRole> roles, int index)
        {
            List<Fighter> fighters = state.FighterList;
            Fighter self = fighters[index];
            FixVec2 sum = FixVec2.Zero;
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (j == index || !other.IsAlive || PassThrough(fighters, roles, index, j))
                {
                    continue;
                }

                Fix reach = self.Radius + other.Radius + state.Scenario.Movement.PersonalSpace;
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

                if (self.Radius + other.Radius - distance > state.Scenario.Movement.AllyPartingDepth && AlliedOtherSquad(self, other))
                {
                    continue; // allied squads that deep inside each other are parted gently by Resolve; don't shove
                }

                // Equal weights → a full share (2·w/2w = 1); holding 3 vs moving 1 → ½ for the holder, 1½ for the mover.
                int total = weights[index] + weights[j];
                Fix push = (reach - distance) * (2 * weights[j]) / (total * state.Scenario.Movement.SeparationTicks);
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
        public static FixVec2 Slide(BattleState state, List<int> weights, List<CrowdRole> roles, int index, FixVec2 velocity)
        {
            List<Fighter> fighters = state.FighterList;
            Fighter self = fighters[index];
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (j == index || !other.IsAlive || PassThrough(fighters, roles, index, j))
                {
                    continue;
                }

                Fix reach = self.Radius + other.Radius + state.Scenario.Movement.PersonalSpace;
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
        /// A man on orders squeezing through the ranks of an allied squad that holds its ground: while his body overlaps
        /// any of theirs, his own stride is capped at that holder formation's <see cref="FormationDef.AllyPassSpeed"/> of
        /// his walking pace (the slowest such cap wins). Walking alongside without touching costs nothing; holders are
        /// unaffected. It caps his velocity, not pushes: his own solid rank behind him can still carry him a little faster.
        /// </summary>
        public static FixVec2 Squeeze(BattleState state, List<CrowdRole> roles, int index, FixVec2 velocity)
        {
            if (roles[index] != CrowdRole.Ordered)
            {
                return velocity;
            }

            List<Fighter> fighters = state.FighterList;
            Fighter self = fighters[index];
            Fix cap = Fix.MaxValue;
            for (int j = 0; j < fighters.Count; j++)
            {
                Fighter other = fighters[j];
                if (j == index || !other.IsAlive || !PassThrough(fighters, roles, index, j))
                {
                    continue;
                }

                Fix reach = self.Radius + other.Radius; // bodies overlap: he is inside their ranks
                FixVec2 apart = other.Position - self.Position;
                if (FixMath.Abs(apart.X) >= reach || FixMath.Abs(apart.Y) >= reach || apart.Length >= reach)
                {
                    continue;
                }

                // A holder always has a squad (see Holding).
                Fix holderCap = self.Def.WalkPerTick * Combat.Wounds.Pace(state.Scenario.Combat, self.Wound)
                    * state.FindSquad(other.SquadId)!.Formation.AllyPassSpeed;
                cap = FixMath.Min(cap, holderCap);
            }

            return cap == Fix.MaxValue ? velocity : velocity.ClampLength(cap);
        }

        /// <summary>
        /// Pushes overlapping living fighters apart until no pair overlaps (up to <see cref="Defs.MovementDef.CollisionPasses"/>
        /// passes over all pairs in id order). Both bodies of a pushed pair are put back where they fit — on the map, out of
        /// walls — at once (constraining only at the end would push men at an edge or wall back into their neighbours), and
        /// finally every body is.
        /// </summary>
        public static void Resolve(BattleState state, List<int> weights, List<CrowdRole> roles)
        {
            MapDef map = state.Scenario.Map;
            List<Fighter> fighters = state.FighterList;
            for (int pass = 0; pass < state.Scenario.Movement.CollisionPasses; pass++)
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
                        if (b.IsAlive && !PassThrough(fighters, roles, i, j) && Separate(map, state.Scenario.Movement, a, b, weights[i], weights[j]))
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
                    fighter.Position = Terrain.Constrain(map, fighter.Position, fighter.Radius);
                }
            }
        }

        /// <summary>
        /// Pushes <paramref name="a"/> and <paramref name="b"/> apart by their overlap and keeps both on the map; true if
        /// they overlapped.
        /// </summary>
        private static bool Separate(MapDef map, MovementDef rules, Fighter a, Fighter b, int weightA, int weightB)
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
            if (overlap > rules.AllyPartingDepth && AlliedOtherSquad(a, b))
            {
                // Men of different allied squads this deep inside each other were passing through until now (an order
                // given or finished mid-squeeze): they drift apart ~10 cm a tick instead of jumping. Ordinary overlaps
                // between allied squads (crossings, pushes: ≤ ~7 cm measured) stay below the threshold and are cleared at
                // once; capping them too breaks crossings. Men of one squad never pass through each other, so any overlap
                // between them is a push and is cleared at once. Known: once a deep pair gets under the threshold, the
                // rest clears in one tick (≤ ~0.3 m measured) — bench item.
                overlap = rules.PersonalSpace / rules.CollisionPasses; // personal space spread over all passes (~1.7 cm)
            }

            Fix shareA = overlap * weightB / (weightA + weightB); // the heavier one moves less
            Fix shareB = overlap - shareA;
            // On the same spot Apart gives +X; the -= sends a (the lower id) west and the += sends b east.
            a.Position = Terrain.Constrain(map, a.Position - Apart(apart, distance, shareA, negativeX: false), a.Radius);
            b.Position = Terrain.Constrain(map, b.Position + Apart(apart, distance, shareB, negativeX: false), b.Radius);
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
