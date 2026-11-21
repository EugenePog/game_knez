using System.Collections.Generic;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Orders
{
    /// <summary>
    /// Tick stage 3: carries out squad orders by moving and turning the anchor (battle-sim.md §10). A move order turns the
    /// squad in place until the target is within 30° of its front, then marches straight there at the walk speed of its
    /// slowest living member while finishing the turn, and on arrival turns to the order's facing at the formation's turn
    /// rate. The order ends only when the squad is formed up: every living member within 30 cm of his slot (decided
    /// 2026-11-21) — until then it is on the move. Since step 6 it marches its planned route (<see cref="Squad.Path"/>)
    /// leg by leg, turning the same way at each corner, and waits while the route is planned. Wheeling and slot
    /// reassignment come in step 7.
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
                if (squad.Order.Kind == SquadOrderKind.Move)
                {
                    Move(state, squad);
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

            if (squad.PathRequestTick >= 0)
            {
                return; // its route is planned later this tick; it marches from the next
            }

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

            while (squad.PathIndex < path.Count - 1 && (path[squad.PathIndex] - squad.Anchor).Length <= MovementRules.ArriveDistance)
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

                Turn(squad, order.Facing, turnRate);
                if (squad.Facing == order.Facing && FormedUp(state, squad))
                {
                    squad.Order = SquadOrder.None;
                    squad.ClearPath();
                }

                return;
            }

            Angle heading = FixMath.Atan2(delta.Y, delta.X);
            Turn(squad, heading, turnRate);
            int offHeading = squad.Facing.DeltaTo(heading);
            int tolerance = state.Scenario.Movement.PivotToleranceUnits;
            if (offHeading > tolerance || offHeading < -tolerance)
            {
                return; // still turning in place
            }

            FixVec2 step = distance <= speed ? delta : delta.WithLength(speed);
            squad.Anchor += step;
            squad.Velocity = step;
            if (!lastLeg && squad.Anchor == waypoint)
            {
                Reach(state, squad); // the rest of this tick's stride is dropped: at most one tick's march per corner
            }
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
                if (fighter == null || !fighter.IsAlive)
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

        /// <summary>Walk speed per tick of the slowest living member, or zero when none lives.</summary>
        private static Fix SlowestWalk(BattleState state, Squad squad)
        {
            Fix slowest = Fix.Zero;
            List<EntityId> members = squad.MemberList;
            for (int i = 0; i < members.Count; i++)
            {
                Fighter? fighter = state.FindFighter(members[i]);
                if (fighter == null || !fighter.IsAlive)
                {
                    continue;
                }

                Fix walk = fighter.Def.WalkPerTick;
                if (slowest == Fix.Zero || walk < slowest)
                {
                    slowest = walk;
                }
            }

            return slowest;
        }
    }
}
