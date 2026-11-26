using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Set how a squad fights on its own (battle-sim.md §8, M4 step 1): Hold, Advance or Pursue. A standing instruction,
    /// so a broken squad takes it too (it acts once the squad re-forms); it doesn't touch the current order. Rejected as
    /// unknown-entity, wrong-side or not-alive (no living member).
    /// </summary>
    public sealed class SetStanceCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns the squad.</summary>
        public SetStanceCommand(int sideId, EntityId squadId, SquadStance stance)
        {
            SideId = sideId;
            SquadId = squadId;
            Stance = stance;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The squad.</summary>
        public EntityId SquadId { get; }

        /// <summary>The stance it takes.</summary>
        public SquadStance Stance { get; }

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

            return MoveSquadCommand.HasLivingMember(state, squad) ? RejectReason.None : RejectReason.NotAlive;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Squad squad = state.FindSquad(SquadId)!;
            squad.Stance = Stance;
            if (Stance != SquadStance.Pursue)
            {
                OrderSystem.EndChase(state, squad, reform: true); // called back from a chase: it re-forms where they stand
            }
        }
    }
}
