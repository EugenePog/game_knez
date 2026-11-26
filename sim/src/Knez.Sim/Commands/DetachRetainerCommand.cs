using Knez.Sim.Formations;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// A Retainer leaves his squad and stands alone where he is (battle-sim.md §8, §13, M3 step 10). Rejected as
    /// unknown-entity, wrong-side, not-alive, routing or not-in-squad.
    /// </summary>
    public sealed class DetachRetainerCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns him.</summary>
        public DetachRetainerCommand(int sideId, EntityId retainerId)
        {
            SideId = sideId;
            RetainerId = retainerId;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The Retainer (a fighter id).</summary>
        public EntityId RetainerId { get; }

        /// <inheritdoc/>
        public RejectReason Validate(BattleState state)
        {
            RejectReason reason = RetainerCommands.Check(state, SideId, RetainerId, out Fighter? retainer);
            if (reason.IsRejected)
            {
                return reason;
            }

            return retainer!.SquadId.IsNone ? RejectReason.NotInSquad : RejectReason.None;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Fighter retainer = state.FindFighter(RetainerId)!;
            SquadMembership.Leave(state, state.FindSquad(retainer.SquadId)!, retainer);
        }
    }
}
