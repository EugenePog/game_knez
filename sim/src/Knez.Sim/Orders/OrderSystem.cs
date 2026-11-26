using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Orders
{
    /// <summary>
    /// Tick stage 3: carries out squad orders (battle-sim.md §10). A move order marches the anchor along its planned route
    /// (<see cref="Squad.Path"/>) at the walk speed of the slowest living member (× close ranks' pace, M3 step 11) — at
    /// <c>reformPace</c> while re-forming —
    /// and waits while the route is planned or the squad regroups for a gap. Turns of up to 30° are finished about the
    /// anchor on the march; 30°–90° the squad wheels round its inner flank (from just before a corner, so it comes out on
    /// the next leg); more than a quarter turn it faces about, the rear rank becoming the front (decided 2026-11-21) —
    /// each only on clear ground, else it pivots in place. On arrival it turns to the order's facing; a face-about there
    /// was planned for (the route runs a depth past the target) or is followed by a step back, so the front rank ends on
    /// the ordered point. The order ends when the squad is formed up: every living member within 30 cm of his slot.
    /// M4 step 1, before the march: attacks (a march aimed at the target squad, re-aimed), stances (Advance and Pursue go
    /// at nearby enemies on their own) and chases (see <c>Engage</c>).
    /// </summary>
    public sealed class OrderSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
            List<Squad> squads = state.SquadList;
            for (int i = 0; i < squads.Count; i++)
            {
                Squad squad = squads[i];
                squad.Velocity = FixVec2.Zero;
                squad.Spin = 0;
                if (squad.Reforming && FormedUp(state, squad))
                {
                    squad.Reforming = false; // in its new formation: back to full pace
                }

                Engage(state, squad);
                if (squad.Order.Kind == SquadOrderKind.Move)
                {
                    Move(state, squad);
                }
            }

            EndLoneOrders(state);
        }

        /// <summary>An attack re-aims when its target's anchor has moved this far from the order's point (metres).</summary>
        private static readonly Fix Reaim = Fix.FromInt(2);

        /// <summary>
        /// Attacks, stances and pursuit (battle-sim.md §10, M4 step 1; decided by the user 2026-11-26), before the march:
        /// <list type="bullet">
        /// <item>A chase ends when its time is up or no runner is left within the pursuit range of where it began; the
        /// squad re-forms where its men stand.</item>
        /// <item>An attack ends when its target breaks or has nobody left in the fight; the squad stops where it is, and
        /// its stance decides at once what's next. Otherwise, once a second and out of melee, the march is re-aimed when
        /// the target's anchor has moved more than 2 m.</item>
        /// <item>Once a second (staggered by id), a squad in Advance or Pursue with no order and not in melee: in Pursue,
        /// with runners of the enemy within the advance range of its men, it starts a chase (not again within a chase's
        /// length of the last one ending); else it attacks the nearest enemy squad within the advance range.</item>
        /// </list>
        /// </summary>
        private static void Engage(BattleState state, Squad squad)
        {
            if (squad.Broken)
            {
                return; // running: no orders, no stance (morale cleared its attack and chase when it broke)
            }

            if (!Standing(state, squad))
            {
                // Nobody left to fight with (the last man fell, or its only Retainer left): no attack, no chase.
                if (!squad.AttackTarget.IsNone)
                {
                    squad.AttackTarget = EntityId.None;
                    squad.Order = SquadOrder.None;
                    squad.ClearPath();
                }

                EndChase(state, squad, reform: false);
                return;
            }

            MovementDef rules = state.Scenario.Movement;
            if (squad.Pursuing)
            {
                if (state.Tick >= squad.PursuitUntil || !RunnerWithin(state, squad, squad.PursuitFrom, rules.PursuitRange))
                {
                    EndChase(state, squad, reform: true);
                }

                return;
            }

            bool decide = (state.Tick + squad.Id.Value) % BattleState.TicksPerSecond == 0;
            if (!squad.AttackTarget.IsNone)
            {
                Squad? target = state.FindSquad(squad.AttackTarget);
                if (target == null || Fleeing(state, target) || !Standing(state, target))
                {
                    squad.AttackTarget = EntityId.None; // done (broken, or leaving with most of it running): the stance says what's next, now
                    squad.Order = SquadOrder.None;
                    squad.ClearPath();
                    decide = true;
                }
                else
                {
                    // Not while it steps back out of a melee or regroups for a gap: a new march would cancel either.
                    if (decide && !InMelee(state, squad) && !squad.Withdrawing && squad.RegroupSince < 0 && squad.PathRequestTick < 0
                        && Stale(state, squad, target))
                    {
                        Aim(state, squad, target);
                    }

                    return;
                }
            }

            if (!decide || squad.Stance == SquadStance.Hold || squad.Order.Kind != SquadOrderKind.None || InMelee(state, squad))
            {
                return;
            }

            bool rested = squad.PursuitUntil < 0 || state.Tick >= squad.PursuitUntil + rules.PursuitTicks;
            if (squad.Stance == SquadStance.Pursue && rested && RunnerNearMen(state, squad, rules.AdvanceRange))
            {
                squad.Pursuing = true;
                squad.PursuitUntil = state.Tick + rules.PursuitTicks;
                squad.PursuitFrom = squad.Anchor;
                squad.Order = SquadOrder.None;
                squad.ClearPath();
                return;
            }

            Squad? nearest = NearestEnemy(state, squad, rules.AdvanceRange);
            if (nearest != null)
            {
                Attack(state, squad, nearest);
            }
        }

        /// <summary>Orders <paramref name="squad"/> to attack <paramref name="target"/>: it marches at it (M4 step 1).</summary>
        internal static void Attack(BattleState state, Squad squad, Squad target)
        {
            squad.AttackTarget = target.Id;
            Aim(state, squad, target);
        }

        /// <summary>A chase ends (time up, no runner left, or another order): with <paramref name="reform"/> the squad
        /// re-forms where its men stand; else the new order's march gathers them.</summary>
        internal static void EndChase(BattleState state, Squad squad, bool reform)
        {
            if (!squad.Pursuing)
            {
                return;
            }

            squad.Pursuing = false;
            squad.PursuitUntil = state.Tick; // when it ended: no new chase for a chase's length
            if (reform)
            {
                Formations.SlotAssignment.ReformAtCentre(state, squad);
            }
        }

        /// <summary>Whether <paramref name="squad"/> has a living man who isn't running (someone left in the fight).</summary>
        internal static bool Standing(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting)
                {
                    return true;
                }
            }

            return false;
        }

        // The march aimed at the target, facing it from where the squad stands.
        private static void Aim(BattleState state, Squad squad, Squad target)
        {
            FixVec2 aim = AimPoint(state, target);
            Commands.MoveSquadCommand.March(state, squad, aim, FacingTo(squad, aim));
        }

        // Whether the march no longer goes where an aim now would (more than 2 m off), or has ended away from it.
        private static bool Stale(BattleState state, Squad squad, Squad target)
        {
            FixVec2 aim = AimPoint(state, target);
            FixVec2 fitted = Commands.MoveSquadCommand.Fit(state, squad, aim, FacingTo(squad, aim), out _);
            FixVec2 from = squad.Order.Kind == SquadOrderKind.Move ? squad.Order.Target : squad.Anchor;
            return (fitted - from).Length > Reaim;
        }

        private static Angle? FacingTo(Squad squad, FixVec2 aim)
        {
            FixVec2 to = aim - squad.Anchor;
            return to.Length > MovementRules.ArriveDistance ? FixMath.Atan2(to.Y, to.X) : (Angle?)null;
        }

        // The target's anchor; while it chases, the centre of its men who aren't running (its anchor stays where the
        // chase began, up to 30 m from them).
        internal static FixVec2 AimPoint(BattleState state, Squad target)
        {
            if (!target.Pursuing)
            {
                return target.Anchor;
            }

            long x = 0;
            long y = 0;
            int n = 0;
            List<EntityId> members = target.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive && !man.IsRouting)
                {
                    x += man.Position.X.Raw;
                    y += man.Position.Y.Raw;
                    n++;
                }
            }

            return n == 0 ? target.Anchor : new FixVec2(Fix.FromRaw(x / n), Fix.FromRaw(y / n));
        }

        /// <summary>
        /// Whether <paramref name="man"/> is a runner a chase goes after (M4 step 1): an enemy running from a squad that
        /// broke — the user's rule is "when the enemy it fights breaks", so one man running from a squad that holds
        /// doesn't draw a chase (nor a Retainer running alone).
        /// </summary>
        internal static bool Chaseable(BattleState state, Fighter man, int side)
        {
            if (!man.IsAlive || !man.IsRouting || man.SideId == side || man.SquadId.IsNone)
            {
                return false;
            }

            Squad? squad = state.FindSquad(man.SquadId);
            return squad != null && Fleeing(state, squad);
        }

        /// <summary>
        /// Whether <paramref name="squad"/>'s runners are fair game for a chase: it broke — or, leaving the field (M4 step 2:
        /// a leaving squad doesn't break), more of its men in the fight are running than would break it.
        /// </summary>
        internal static bool Fleeing(BattleState state, Squad squad)
        {
            if (squad.Broken)
            {
                return true;
            }

            if (!squad.Leaving)
            {
                return false;
            }

            int inFight = 0;
            int routing = 0;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man != null && man.IsAlive)
                {
                    inFight++;
                    routing += man.IsRouting ? 1 : 0;
                }
            }

            return inFight > 0 && routing * 1000L > (long)state.Scenario.Morale.BreakShare * inFight;
        }

        /// <summary>In melee: one of its men in the fight is engaged with an enemy who isn't running — runners in reach are
        /// struck at, but don't hold a squad. From the fighters' own state, not a scratch buffer, so a restored snapshot
        /// decides the same.</summary>
        internal static bool InMelee(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting || !man.IsEngaged)
                {
                    continue;
                }

                Fighter? enemy = state.FindFighter(man.Target);
                if (enemy != null && !enemy.IsRouting)
                {
                    return true;
                }
            }

            return false;
        }

        // The nearest enemy squad, not broken, with a man in the fight within range (edge to edge) of one of the squad's
        // men in the fight; ties to the lower id.
        private static Squad? NearestEnemy(BattleState state, Squad squad, Fix range)
        {
            Squad? best = null;
            Fix bestGap = Fix.Zero;
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                Squad other = squads[s];
                if (other.SideId == squad.SideId || other.Broken)
                {
                    continue;
                }

                Fix gap = Gap(state, squad, other.MemberList, range, runners: false);
                if (gap <= range && (best == null || gap < bestGap))
                {
                    best = other;
                    bestGap = gap;
                }
            }

            return best;
        }

        // Whether a runner of a broken enemy squad is within range (edge to edge) of one of the squad's men in the fight.
        private static bool RunnerNearMen(BattleState state, Squad squad, Fix range)
        {
            List<Squad> squads = state.SquadList;
            for (int s = 0; s < squads.Count; s++)
            {
                if (squads[s].SideId != squad.SideId && Fleeing(state, squads[s]) && Gap(state, squad, squads[s].MemberList, range, runners: true) <= range)
                {
                    return true;
                }
            }

            return false;
        }

        // Whether a runner worth chasing stands within range of a point (centre to centre).
        private static bool RunnerWithin(BattleState state, Squad squad, FixVec2 point, Fix range)
        {
            Fix reach = range * range;
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                if (Chaseable(state, man, squad.SideId) && (man.Position - point).LengthSquared <= reach)
                {
                    return true;
                }
            }

            return false;
        }

        // The smallest edge-to-edge gap between the squad's men in the fight and the given enemies (in the fight, or
        // running when runners); past range counts as range + 1 m, so no square root for the far ones.
        private static Fix Gap(BattleState state, Squad squad, List<EntityId> enemies, Fix range, bool runners)
        {
            Fix best = range + Fix.One;
            List<EntityId> members = squad.MemberList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting)
                {
                    continue;
                }

                for (int e = 0; e < enemies.Count; e++)
                {
                    Fighter? enemy = state.FindFighter(enemies[e]);
                    if (enemy == null || !enemy.IsAlive || enemy.IsRouting != runners)
                    {
                        continue;
                    }

                    Fix bodies = man.Radius + enemy.Radius;
                    Fix limit = best + bodies;
                    FixVec2 to = enemy.Position - man.Position;
                    if (FixMath.Abs(to.X) > limit || FixMath.Abs(to.Y) > limit)
                    {
                        continue;
                    }

                    Fix gap = to.Length - bodies;
                    best = gap < best ? gap : best;
                }
            }

            return best;
        }

        /// <summary>
        /// A Retainer standing alone (battle-sim.md §13, M3 step 10) is done with a move once he stands at its point
        /// (within 1 cm) facing its way exactly (movement turns him onto it, stage 6), and with an attack once that enemy
        /// is down or gone. A man out of the fight has no order.
        /// </summary>
        private static void EndLoneOrders(BattleState state)
        {
            List<Fighter> fighters = state.FighterList;
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter man = fighters[i];
                LoneOrder order = man.LoneOrder;
                if (order.Kind != LoneOrderKind.None && !man.IsAlive)
                {
                    man.LoneOrder = LoneOrder.None;
                }
                else if (order.Kind == LoneOrderKind.Move)
                {
                    bool facing = !order.HasFacing || man.Facing == order.Facing;
                    if ((order.Target - man.Position).Length <= MovementRules.ArriveDistance && facing)
                    {
                        man.LoneOrder = LoneOrder.None;
                    }
                }
                else if (order.Kind == LoneOrderKind.Attack)
                {
                    Fighter? enemy = state.FindFighter(order.Enemy);
                    if (enemy == null || !enemy.IsAlive)
                    {
                        man.LoneOrder = LoneOrder.None;
                    }
                }
            }
        }

        private static void Move(BattleState state, Squad squad)
        {
            Fix speed = SlowestWalk(state, squad);
            if (speed == Fix.Zero)
            {
                squad.Order = SquadOrder.None; // nobody left to march
                squad.ClearPath();
                return;
            }

            if (squad.Reforming)
            {
                speed = speed * state.Scenario.Movement.ReformPace; // re-forming on the move (decided 2026-11-21)
            }

            if (squad.PathRequestTick >= 0)
            {
                return; // its route is planned later this tick; it marches from the next
            }

            if (Engaged(state, squad) || (squad.Withdrawing && !ClearOfEnemies(state, squad)))
            {
                if (squad.RegroupSince >= 0)
                {
                    squad.RegroupSince = state.Tick; // the wait to regroup starts over once out of contact
                }

                StepBackOutOfContact(state, squad, speed * state.Scenario.Movement.StepBackPace);
                return;
            }

            squad.Withdrawing = false; // clear: march on as usual (it may face about now)

            SquadOrder order = squad.Order;
            int turnRate = squad.Formation.TurnPerTick;
            List<FixVec2> path = squad.PathList;
            if (squad.RegroupSince >= 0)
            {
                // Regrouping before a narrow gap: face the way on (the gap) while the men fall in, then go. Past the
                // longest wait it goes anyway, still turning first like any march.
                FixVec2 on = (squad.PathIndex < path.Count ? path[squad.PathIndex] : order.Target) - squad.Anchor;
                bool facing = true;
                if (on.Length > MovementRules.ArriveDistance)
                {
                    Angle onward = FixMath.Atan2(on.Y, on.X);
                    Turn(squad, onward, turnRate);
                    int off = squad.Facing.DeltaTo(onward);
                    int pivot = state.Scenario.Movement.PivotToleranceUnits;
                    facing = off <= pivot && off >= -pivot;
                }

                if (state.Tick - squad.RegroupSince < state.Scenario.Movement.RegroupMaxWaitTicks && !(facing && FormedUp(state, squad)))
                {
                    return;
                }

                squad.RegroupSince = -1;
            }

            while (squad.PathIndex < path.Count - 1
                && ((path[squad.PathIndex] - squad.Anchor).Length <= MovementRules.ArriveDistance || PassedByFacingAbout(squad, path[squad.PathIndex])))
            {
                if (Reach(state, squad))
                {
                    return;
                }
            }

            bool lastLeg = squad.PathIndex >= path.Count - 1;
            FixVec2 waypoint = lastLeg ? order.Target : path[squad.PathIndex];
            FixVec2 delta = waypoint - squad.Anchor;
            Fix distance = delta.Length;

            if (lastLeg && distance <= MovementRules.ArriveDistance)
            {
                squad.Velocity = delta;
                squad.Anchor = order.Target;
                if (squad.PathIndex < path.Count && squad.PathFilesList[squad.PathIndex] == 0)
                {
                    squad.Files = 0; // widen back to its own formation at the target
                }

                if (IsAbout(squad.Facing.DeltaTo(order.Facing)) && FaceAboutHelps(state, squad, order.Target, lastLeg: true))
                {
                    // No room was left for an approach past the target: face about here; the new front stands one depth
                    // past the target, so the next ticks step back onto it (the front lands on the ordered point).
                    FaceAbout(state, squad);
                    return;
                }

                Turn(squad, order.Facing, turnRate);
                if (squad.Facing == order.Facing && FormedUp(state, squad))
                {
                    squad.Order = SquadOrder.None;
                    squad.ClearPath();
                }

                return;
            }

            int tolerance = state.Scenario.Movement.PivotToleranceUnits;
            if (!lastLeg && WheelsHere(squad, waypoint, distance, tolerance, out Angle nextHeading)
                && WheelClear(state, squad, nextHeading, squad.PathIndex + 1 < path.Count ? path[squad.PathIndex + 1] : order.Target))
            {
                // Wheel through the corner on an arc tangent to both legs, so the squad comes out on the next leg instead
                // of half a frontage off it.
                Reach(state, squad);
                Wheel(state, squad, nextHeading, turnRate);
                return;
            }

            Angle heading = FixMath.Atan2(delta.Y, delta.X);
            int offHeading = squad.Facing.DeltaTo(heading);
            if (IsAbout(offHeading))
            {
                if (lastLeg && !IsAbout(squad.Facing.DeltaTo(order.Facing)) && distance <= SlotTracking.Depth(squad) + NavGrid.CellSize)
                {
                    // The target is just behind and the squad already faces the way it should end: step back onto it.
                    FixVec2 back = distance <= speed * state.Scenario.Movement.StepBackPace ? delta : delta.WithLength(speed * state.Scenario.Movement.StepBackPace);
                    squad.Anchor += back;
                    squad.Velocity = back;
                    return;
                }

                if (FaceAboutHelps(state, squad, waypoint, lastLeg))
                {
                    FaceAbout(state, squad); // more than a quarter turn: face about, the rear rank leads (decided 2026-11-21)
                    return;
                }
            }

            if (offHeading > tolerance || offHeading < -tolerance)
            {
                if (!IsAbout(offHeading) && distance > SlotTracking.FrontWidth(squad) && WheelClear(state, squad, heading, waypoint))
                {
                    Wheel(state, squad, heading, turnRate); // wheel round the inner flank (decided 2026-11-21)
                    return;
                }

                // A leg shorter than the front (a wheel would overshoot it), one a face-about would leave behind, or
                // ground where a wheel or face-about would carry the squad into a wall: pivot in place as before.
                Turn(squad, heading, turnRate);
                int left = squad.Facing.DeltaTo(heading);
                if (left > tolerance || left < -tolerance)
                {
                    return;
                }
            }
            else
            {
                Turn(squad, heading, turnRate); // the last few degrees about the anchor, while marching
            }

            FixVec2 step = distance <= speed ? delta : delta.WithLength(speed);
            squad.Anchor += step;
            squad.Velocity = step;
            if (!lastLeg && squad.Anchor == waypoint)
            {
                Reach(state, squad); // the rest of this tick's stride is dropped: at most one tick's march per corner
            }
        }

        /// <summary>True when a living member is in melee (<see cref="Fighter.IsEngaged"/>, chosen at the end of last tick).</summary>
        private static bool Engaged(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter != null && fighter.IsAlive && fighter.IsEngaged && !fighter.IsRouting)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when no living enemy's body is within any living member's weapon reach plus
        /// <see cref="Defs.MovementDef.WithdrawClearance"/> of his own (edge to edge).
        /// </summary>
        private static bool ClearOfEnemies(BattleState state, Squad squad)
        {
            Fix clearance = state.Scenario.Movement.WithdrawClearance;
            List<EntityId> members = squad.MemberList;
            List<Fighter> fighters = state.FighterList;
            for (int m = 0; m < members.Count; m++)
            {
                Fighter? man = state.FindFighter(members[m]);
                if (man == null || !man.IsAlive || man.IsRouting)
                {
                    continue;
                }

                Fix reach = man.Kit.Weapon.Reach + clearance;
                for (int j = 0; j < fighters.Count; j++)
                {
                    Fighter enemy = fighters[j];
                    if (!enemy.IsAlive || enemy.IsRouting || enemy.SideId == squad.SideId) // a running enemy is no threat
                    {
                        continue;
                    }

                    Fix limit = man.Radius + enemy.Radius + reach;
                    FixVec2 to = enemy.Position - man.Position;
                    if (FixMath.Abs(to.X) <= limit && FixMath.Abs(to.Y) <= limit && to.Length <= limit)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// The melee lock (battle-sim.md §11, decided 2026-11-22): a squad in contact doesn't march, turn or regroup; its
        /// order waits until contact ends. The one way out is backward — when the way on (the current waypoint, or the
        /// target) lies more than a quarter turn behind its front, it steps straight toward it at
        /// <paramref name="pace"/>, still facing the enemy, and is then <see cref="Squad.Withdrawing"/> until no enemy is
        /// within reach + <c>withdrawClearance</c>. A waypoint passed on the way narrows or widens the squad as the route
        /// says, without stopping to regroup; the target itself is where it stops.
        /// </summary>
        private static void StepBackOutOfContact(BattleState state, Squad squad, Fix pace)
        {
            List<FixVec2> path = squad.PathList;
            SquadOrder order = squad.Order;
            FixVec2 toTarget = order.Target - squad.Anchor;
            if (squad.PathIndex < path.Count - 1 && toTarget.Length > MovementRules.ArriveDistance
                && IsAbout(squad.Facing.DeltaTo(FixMath.Atan2(toTarget.Y, toTarget.X)))
                && Terrain.SegmentClear(state.Scenario.Map, squad.Anchor, order.Target, MovementRules.RouteSampleStep))
            {
                // The target is behind with a clear way back: step straight to it. The route may run past it to leave room
                // for facing about, which a squad facing the enemy doesn't do.
                squad.PathIndex = path.Count - 1;
            }

            bool lastLeg = squad.PathIndex >= path.Count - 1;
            FixVec2 waypoint = lastLeg ? order.Target : path[squad.PathIndex];
            FixVec2 delta = waypoint - squad.Anchor;
            Fix distance = delta.Length;
            if (distance <= MovementRules.ArriveDistance)
            {
                if (lastLeg)
                {
                    squad.Velocity = delta;
                    squad.Anchor = order.Target;
                    if (squad.Facing == order.Facing && FormedUp(state, squad))
                    {
                        squad.Order = SquadOrder.None; // arrived facing the way it should: done, no turning in contact
                        squad.ClearPath();
                    }
                }
                else
                {
                    int files = squad.PathFilesList[squad.PathIndex];
                    squad.PathIndex++;
                    if (files >= 0)
                    {
                        squad.Files = files; // narrow or widen as the route says, without stopping in contact
                    }
                }

                return;
            }

            if (!IsAbout(squad.Facing.DeltaTo(FixMath.Atan2(delta.Y, delta.X))))
            {
                return; // ahead or sideways: wait for contact to end
            }

            FixVec2 step = distance <= pace ? delta : delta.WithLength(pace);
            squad.Anchor += step;
            squad.Velocity = step;
            squad.Withdrawing = true;
        }

        /// <summary>
        /// The anchor is at its current waypoint: does what the route says there and heads for the next one. Returns true
        /// when the squad must stop there to regroup into fewer files before a narrow gap.
        /// </summary>
        private static bool Reach(BattleState state, Squad squad)
        {
            int files = squad.PathFilesList[squad.PathIndex];
            squad.PathIndex++;
            if (files > 0)
            {
                squad.Files = files;
                squad.RegroupSince = state.Tick;
                return true;
            }

            if (files == 0)
            {
                squad.Files = 0; // the rear is through: back to the formation's own width
            }

            return false;
        }

        private static bool IsAbout(int delta) => delta > Angle.UnitsPerTurn / 4 || delta < -(Angle.UnitsPerTurn / 4);

        /// <summary>
        /// Whether the squad, marching on <paramref name="waypoint"/> (<paramref name="distance"/> away, an unmarked corner),
        /// should start wheeling onto the next leg now: the turn there is more than the pivot tolerance and at most a
        /// quarter turn, and the corner is within half·tan(θ/2) — where a wheel round the inner flank (radius half the
        /// front) leaves the first leg and meets the next.
        /// </summary>
        private static bool WheelsHere(Squad squad, FixVec2 waypoint, Fix distance, int tolerance, out Angle nextHeading)
        {
            nextHeading = squad.Facing;
            List<FixVec2> path = squad.PathList;
            if (squad.PathFilesList[squad.PathIndex] != -1)
            {
                return false; // regroup and widen points are reached, not cut
            }

            FixVec2 next = squad.PathIndex + 1 < path.Count ? path[squad.PathIndex + 1] : squad.Order.Target;
            FixVec2 onward = next - waypoint;
            Fix half = SlotTracking.FrontWidth(squad) / 2;
            if (half <= Fix.Zero || onward.Length <= MovementRules.ArriveDistance)
            {
                return false;
            }

            nextHeading = FixMath.Atan2(onward.Y, onward.X);
            int turn = squad.Facing.DeltaTo(nextHeading);
            int size = turn < 0 ? -turn : turn;
            if (size <= tolerance || IsAbout(turn))
            {
                return false;
            }

            Angle angle = Angle.FromRaw(size);
            Fix lead = half * FixMath.Sin(angle) / (Fix.One + FixMath.Cos(angle));
            return distance <= lead && onward.Length > lead;
        }

        /// <summary>
        /// An unmarked waypoint more than a quarter turn behind the squad and closer than the squad is deep: facing about
        /// carries the squad past it, so it is skipped (routes start at cell centres, which can lie just behind the anchor).
        /// </summary>
        private static bool PassedByFacingAbout(Squad squad, FixVec2 waypoint)
        {
            if (squad.PathFilesList[squad.PathIndex] != -1)
            {
                return false;
            }

            FixVec2 to = waypoint - squad.Anchor;
            Fix reach = FixMath.Max(SlotTracking.Depth(squad), NavGrid.CellSize); // a single rank has no depth: half a cell
            return to.Length <= reach && IsAbout(squad.Facing.DeltaTo(FixMath.Atan2(to.Y, to.X)));
        }

        /// <summary>
        /// Whether facing about now serves the squad marching on <paramref name="waypoint"/>: the new anchor (where the rear
        /// rank's centre is) stands clear of walls, and the waypoint then lies on it, ahead of the new front with a clear way
        /// there, or — on the last leg — just behind it, within a step back. Otherwise facing about would have to be
        /// undone, or would carry the squad into a wall, and it pivots instead.
        /// </summary>
        private static bool FaceAboutHelps(BattleState state, Squad squad, FixVec2 waypoint, bool lastLeg)
        {
            Defs.MapDef map = state.Scenario.Map;
            Fix depth = SlotTracking.Depth(squad);
            FixVec2 anchor = FormationLayout.ToWorld(squad.Anchor, squad.Facing, new FixVec2(Fix.Zero, -depth));
            if (Terrain.Clearance(map, anchor) < MovementRules.RouteSampleStep || Terrain.CrossesWall(map, squad.Anchor, anchor))
            {
                return false;
            }

            FixVec2 on = waypoint - anchor;
            if (on.Length <= NavGrid.CellSize)
            {
                return true; // lands on it
            }

            Angle about = Angle.FromRaw(squad.Facing.Raw + (Angle.UnitsPerTurn / 2));
            if (!IsAbout(about.DeltaTo(FixMath.Atan2(on.Y, on.X))))
            {
                return Terrain.SegmentClear(map, anchor, waypoint, MovementRules.RouteSampleStep);
            }

            return lastLeg && on.Length <= depth + NavGrid.CellSize;
        }

        /// <summary>
        /// Whether a wheel toward <paramref name="toward"/> leaves the squad on clear ground: where the anchor ends after
        /// the whole turn (round the current inner flank) is clear of walls, the arc doesn't cross one, and the way on to
        /// <paramref name="waypoint"/> is open. Near walls a wheel can carry the anchor half a front off the planned leg.
        /// </summary>
        private static bool WheelClear(BattleState state, Squad squad, Angle toward, FixVec2 waypoint)
        {
            Defs.MapDef map = state.Scenario.Map;
            int turn = squad.Facing.DeltaTo(toward);
            Fix half = SlotTracking.FrontWidth(squad) / 2;
            Fix side = turn > 0 ? -half : half;
            FixVec2 pivot = FormationLayout.ToWorld(squad.Anchor, squad.Facing, new FixVec2(side, Fix.Zero));
            FixVec2 halfway = FormationLayout.ToWorld(pivot, Angle.FromRaw(squad.Facing.Raw + (turn / 2)), new FixVec2(-side, Fix.Zero));
            FixVec2 end = FormationLayout.ToWorld(pivot, toward, new FixVec2(-side, Fix.Zero));
            return Terrain.Clearance(map, end) >= MovementRules.RouteSampleStep
                && !Terrain.CrossesWall(map, squad.Anchor, halfway) && !Terrain.CrossesWall(map, halfway, end)
                && Terrain.SegmentClear(map, end, waypoint, MovementRules.RouteSampleStep);
        }

        /// <summary>
        /// Faces the squad about: every man turns round and the rear rank becomes the front, so nobody walks far — the
        /// anchor (front-rank centre) moves to where the rear rank's centre was, and each new place goes to the nearest
        /// man. The slots jump rather than sweep, so this tick carries no slot motion.
        /// </summary>
        private static void FaceAbout(BattleState state, Squad squad)
        {
            squad.Anchor = FormationLayout.ToWorld(squad.Anchor, squad.Facing, new FixVec2(Fix.Zero, -SlotTracking.Depth(squad)));
            squad.Facing = Angle.FromRaw(squad.Facing.Raw + (Angle.UnitsPerTurn / 2));
            SlotAssignment.Nearest(state, squad);
        }

        /// <summary>
        /// Turns the squad toward <paramref name="toward"/> by wheeling round the inner end of its front rank: that man marks
        /// time, the outer flank swings round, and the anchor moves with the wheel. The wheel is no faster than the
        /// formation's turn rate, nor than the outer flank can run.
        /// </summary>
        private static void Wheel(BattleState state, Squad squad, Angle toward, int turnRate)
        {
            Fix half = SlotTracking.FrontWidth(squad) / 2; // SlotTracking.Wheeled recognises the wheel by the same pivot
            if (half <= Fix.Zero)
            {
                Turn(squad, toward, turnRate); // a single file pivots
                return;
            }

            // Outer flank speed = 2·half·ω ≤ run: ω ≤ run / (2·half) radians per tick; 10 430 angle units per radian.
            long runCap = SlowestRun(state, squad).Raw * 10430 / (half.Raw * 2);
            int step = (int)(runCap < turnRate ? (runCap < 1 ? 1 : runCap) : turnRate);
            Angle turned = squad.Facing.TurnToward(toward, step);
            int spin = squad.Facing.DeltaTo(turned);
            Fix side = spin > 0 ? -half : half; // turning left: pivot on the left end
            FixVec2 pivot = FormationLayout.ToWorld(squad.Anchor, squad.Facing, new FixVec2(side, Fix.Zero));
            FixVec2 anchor = FormationLayout.ToWorld(pivot, turned, new FixVec2(-side, Fix.Zero));
            squad.Velocity = anchor - squad.Anchor;
            squad.Anchor = anchor;
            squad.Spin = spin;
            squad.Facing = turned;
        }

        private static void Turn(Squad squad, Angle toward, int maxStep)
        {
            Angle turned = squad.Facing.TurnToward(toward, maxStep);
            squad.Spin = squad.Facing.DeltaTo(turned);
            squad.Facing = turned;
        }

        /// <summary>
        /// Every living member whose slot has room for his body stands within <see cref="Defs.MovementDef.FormedUpReach"/> of
        /// it. Slots that don't (past the edge, in or against a wall, in a gap narrower than a body) are skipped: those men
        /// stand as close as they can (decided 2026-11-21), and several of them may be pulled onto the same spot, where
        /// not every man fits. The test is the same <see cref="Terrain.Constrain"/> bodies obey, so a slot counted here
        /// is one a man can actually stand on.
        /// </summary>
        private static bool FormedUp(BattleState state, Squad squad)
        {
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter == null || !fighter.IsAlive || fighter.IsRouting) // a running man is no longer the squad's
                {
                    continue;
                }

                FixVec2 slot = SlotTracking.Position(squad, SlotTracking.Offset(squad, fighter));
                if (Terrain.Constrain(state.Scenario.Map, slot, fighter.Radius) == slot
                    && (slot - fighter.Position).Length > state.Scenario.Movement.FormedUpReach)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Run speed per tick of the slowest living member, or zero when none lives.</summary>
        private static Fix SlowestRun(BattleState state, Squad squad)
        {
            Fix slowest = Fix.Zero;
            bool any = false;
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter == null || !fighter.IsAlive || fighter.IsRouting) // a running man is no longer the squad's
                {
                    continue;
                }

                Fix run = fighter.Body.RunPerTick * Combat.Wounds.Pace(state.Scenario.Combat, fighter.Wound);
                if (!any || run < slowest)
                {
                    slowest = run;
                    any = true;
                }
            }

            return slowest;
        }

        /// <summary>Walk speed per tick of the slowest living member, times close ranks' pace while the squad has closed
        /// ranks (M3 step 11), or zero when none lives.</summary>
        private static Fix SlowestWalk(BattleState state, Squad squad)
        {
            Fix slowest = Fix.Zero;
            bool any = false;
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter == null || !fighter.IsAlive || fighter.IsRouting) // a running man is no longer the squad's
                {
                    continue;
                }

                Fix walk = fighter.Body.WalkPerTick * Combat.Wounds.Pace(state.Scenario.Combat, fighter.Wound); // wounded men slow the squad
                if (!any || walk < slowest)
                {
                    slowest = walk;
                    any = true;
                }
            }

            slowest = any ? slowest * Abilities.AbilityRules.Pace(state, squad) : slowest; // closed ranks march slower (M3 step 11)

            // At least one raw unit while anyone lives: zero means "nobody left" to the caller.
            return any && slowest <= Fix.Zero ? Fix.FromRaw(1) : slowest;
        }
    }
}
