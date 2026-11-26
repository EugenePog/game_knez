using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Order a squad to attack an enemy squad (battle-sim.md §8, §10, M4 step 1): it marches at that squad, re-aimed once a
    /// second as it moves, and fights it; the order ends when the target breaks or is gone, and the squad's stance says
    /// what's next (decided by the user 2026-11-26). Replaces any current order. Rejected as unknown-entity (either squad),
    /// wrong-side (his squad isn't yours), not-alive (no living member), squad-broken, not-an-enemy (a squad of your side),
    /// target-broken (it is running already) or not-alive for a target with nobody left in the fight.
    /// </summary>
    public sealed class AttackSquadCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns the squad.</summary>
        public AttackSquadCommand(int sideId, EntityId squadId, EntityId targetId)
        {
            SideId = sideId;
            SquadId = squadId;
            TargetId = targetId;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The squad that attacks.</summary>
        public EntityId SquadId { get; }

        /// <summary>The enemy squad it attacks.</summary>
        public EntityId TargetId { get; }

        /// <inheritdoc/>
        public RejectReason Validate(BattleState state)
        {
            Squad? squad = state.FindSquad(SquadId);
            Squad? target = state.FindSquad(TargetId);
            if (squad == null || target == null)
            {
                return RejectReason.UnknownEntity;
            }

            if (squad.SideId != SideId)
            {
                return RejectReason.WrongSide;
            }

            if (!MoveSquadCommand.HasLivingMember(state, squad))
            {
                return RejectReason.NotAlive;
            }

            if (squad.Broken)
            {
                return RejectReason.SquadBroken;
            }

            if (target.SideId == SideId)
            {
                return RejectReason.NotAnEnemy;
            }

            if (target.Broken)
            {
                return RejectReason.TargetBroken;
            }

            return OrderSystem.Standing(state, target) ? RejectReason.None : RejectReason.NotAlive;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Squad squad = state.FindSquad(SquadId)!;
            OrderSystem.EndChase(state, squad, reform: false); // the march gathers the chasers
            OrderSystem.Attack(state, squad, state.FindSquad(TargetId)!);
        }
    }
}
