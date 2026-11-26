using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// A Retainer standing alone goes at an enemy fighter and fights him (battle-sim.md §8, §13, M3 step 10); in reach of
    /// any enemy he fights whoever is nearest, like every man. The order ends when that enemy is down or gone. Rejected
    /// as unknown-entity (him, or the enemy), wrong-side, not-alive (him, or the enemy), routing, in-squad or
    /// not-an-enemy.
    /// </summary>
    public sealed class RetainerAttackCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns him.</summary>
        public RetainerAttackCommand(int sideId, EntityId retainerId, EntityId enemyId)
        {
            SideId = sideId;
            RetainerId = retainerId;
            EnemyId = enemyId;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The Retainer (a fighter id).</summary>
        public EntityId RetainerId { get; }

        /// <summary>The enemy fighter he goes at.</summary>
        public EntityId EnemyId { get; }

        /// <inheritdoc/>
        public RejectReason Validate(BattleState state)
        {
            RejectReason reason = RetainerCommands.Check(state, SideId, RetainerId, out Fighter? retainer);
            if (reason.IsRejected)
            {
                return reason;
            }

            if (!retainer!.SquadId.IsNone)
            {
                return RejectReason.InSquad;
            }

            Fighter? enemy = state.FindFighter(EnemyId);
            if (enemy == null)
            {
                return RejectReason.UnknownEntity;
            }

            if (enemy.SideId == SideId)
            {
                return RejectReason.NotAnEnemy;
            }

            return enemy.IsAlive ? RejectReason.None : RejectReason.NotAlive;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context) =>
            state.FindFighter(RetainerId)!.LoneOrder = LoneOrder.Attack(EnemyId);
    }
}
