using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Order a squad to march its anchor (front-rank centre) to <see cref="Target"/> and then face <see cref="Facing"/>, or
    /// the direction of the march when no facing is given (battle-sim.md §8, §10). Replaces any current order.
    /// </summary>
    public sealed class MoveSquadCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns the squad.</summary>
        public MoveSquadCommand(int sideId, EntityId squadId, FixVec2 target, Angle? facing = null)
        {
            SideId = sideId;
            SquadId = squadId;
            Target = target;
            Facing = facing;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The squad to move.</summary>
        public EntityId SquadId { get; }

        /// <summary>Where the anchor goes, metres; must be on the map.</summary>
        public FixVec2 Target { get; }

        /// <summary>Facing on arrival, or null for the direction of the march.</summary>
        public Angle? Facing { get; }

        /// <inheritdoc/>
        public RejectReason Validate(BattleState state)
        {
            Squad? squad = state.FindSquad(SquadId);
            if (squad == null)
            {
                return RejectReason.UnknownEntity;
            }

            if (squad.SideId != SideId)
            {
                return RejectReason.WrongSide;
            }

            if (!HasLivingMember(state, squad))
            {
                return RejectReason.NotAlive;
            }

            return state.Scenario.Map.Contains(Target) ? RejectReason.None : RejectReason.OffMap;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Squad squad = state.FindSquad(SquadId)!;
            FixVec2 delta = Target - squad.Anchor;
            // A target within arrival distance is no march: keep the facing rather than turn toward a sub-centimetre
            // offset, whose direction is noise (view clicks converted from float land there).
            Angle facing = Facing ?? (delta.Length <= MovementRules.ArriveDistance ? squad.Facing : FixMath.Atan2(delta.Y, delta.X));
            // Past the edge: shift so the formation fits. Inside a wall: the nearest open ground for the anchor's man
            // (decided 2026-11-21); slots that still fall in walls are stood next to.
            FixVec2 target = FitInside(state, squad, Target, facing);
            Fighter? any = state.FindFighter(squad.MemberList[0]);
            target = Terrain.Constrain(state.Scenario.Map, target, any != null ? any.Radius : Fix.Zero);
            squad.Order = SquadOrder.Move(target, facing);
        }

        /// <inheritdoc/>
        public override string ToString() => $"MoveSquad {SquadId} to {Target}" + (Facing.HasValue ? $" facing {Facing.Value}" : string.Empty);

        /// <summary>
        /// <paramref name="target"/> moved inward just enough that the formation, facing <paramref name="facing"/> there,
        /// stands wholly on the map with room for its bodies. Without it, slots past an edge get pulled onto the same edge
        /// spot, the men can't all reach them, and the order never ends. A formation wider than the map is centred.
        /// </summary>
        private static FixVec2 FitInside(BattleState state, Squad squad, FixVec2 target, Angle facing)
        {
            int count = squad.MemberList.Count;
            Fighter? any = state.FindFighter(squad.MemberList[0]);
            Fix r = any != null ? any.Radius : Fix.Zero;
            Fix minX = Fix.MaxValue, maxX = Fix.MinValue, minY = Fix.MaxValue, maxY = Fix.MinValue;
            for (int slot = 0; slot < count; slot++)
            {
                FixVec2 p = FormationLayout.SlotPosition(squad.Formation, count, slot, target, facing);
                minX = FixMath.Min(minX, p.X);
                maxX = FixMath.Max(maxX, p.X);
                minY = FixMath.Min(minY, p.Y);
                maxY = FixMath.Max(maxY, p.Y);
            }

            Defs.MapDef map = state.Scenario.Map;
            return new FixVec2(target.X + Shift(minX, maxX, r, map.Width), target.Y + Shift(minY, maxY, r, map.Height));
        }

        /// <summary>How far to move a span [lo, hi] so it lies within [r, size − r] (centred when it can't fit).</summary>
        private static Fix Shift(Fix lo, Fix hi, Fix r, Fix size)
        {
            if (hi - lo > size - r - r)
            {
                return (size - (lo + hi)) / 2;
            }

            if (lo < r)
            {
                return r - lo;
            }

            return hi > size - r ? size - r - hi : Fix.Zero;
        }

        private static bool HasLivingMember(BattleState state, Squad squad)
        {
            for (int i = 0; i < squad.MemberList.Count; i++)
            {
                if (state.FindFighter(squad.MemberList[i])?.IsAlive == true)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
