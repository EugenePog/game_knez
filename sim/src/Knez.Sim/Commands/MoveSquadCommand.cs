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
            squad.Order = SquadOrder.Move(Target, facing);
        }

        /// <inheritdoc/>
        public override string ToString() => $"MoveSquad {SquadId} to {Target}" + (Facing.HasValue ? $" facing {Facing.Value}" : string.Empty);

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
