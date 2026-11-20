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
    /// 2026-11-21) — until then it is on the move. Paths, wheeling and slot reassignment come in later M1 steps.
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
                return;
            }

            SquadOrder order = squad.Order;
            int turnRate = squad.Formation.TurnPerTick;
            FixVec2 delta = order.Target - squad.Anchor;
            Fix distance = delta.Length;

            if (distance <= MovementRules.ArriveDistance)
            {
                squad.Velocity = delta;
                squad.Anchor = order.Target;
                Turn(squad, order.Facing, turnRate);
                if (squad.Facing == order.Facing && FormedUp(state, squad))
                {
                    squad.Order = SquadOrder.None;
                }

                return;
            }

            Angle heading = FixMath.Atan2(delta.Y, delta.X);
            Turn(squad, heading, turnRate);
            int offHeading = squad.Facing.DeltaTo(heading);
            if (offHeading > MovementRules.PivotToleranceUnits || offHeading < -MovementRules.PivotToleranceUnits)
            {
                return; // still turning in place
            }

            FixVec2 step = distance <= speed ? delta : delta.WithLength(speed);
            squad.Anchor += step;
            squad.Velocity = step;
        }

        private static void Turn(Squad squad, Angle toward, int maxStep)
        {
            Angle turned = squad.Facing.TurnToward(toward, maxStep);
            squad.Spin = squad.Facing.DeltaTo(turned);
            squad.Facing = turned;
        }

        /// <summary>
        /// Every living member whose slot lies on the map stands within <see cref="MovementRules.FormedUpReach"/> of it.
        /// Slots past the edge are skipped: several of them can be pulled onto the same edge spot, where not every man fits.
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
                if (state.Scenario.Map.Contains(slot) && Crowding.DistanceToSlot(state, squad, fighter) > MovementRules.FormedUpReach)
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
