using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// A Retainer joins a squad of his side, in its front rank (battle-sim.md §8, §13, M3 step 10). From another squad he
    /// moves straight across. Rejected as unknown-entity, wrong-side (him, or the squad), not-alive (him, or a squad with
    /// no one left), routing, squad-broken, squad-full (64 men) or in-squad (already in it).
    /// </summary>
    public sealed class AttachRetainerCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns both.</summary>
        public AttachRetainerCommand(int sideId, EntityId retainerId, EntityId squadId)
        {
            SideId = sideId;
            RetainerId = retainerId;
            SquadId = squadId;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The Retainer (a fighter id).</summary>
        public EntityId RetainerId { get; }

        /// <summary>The squad he joins.</summary>
        public EntityId SquadId { get; }

        /// <inheritdoc/>
        public RejectReason Validate(BattleState state)
        {
            RejectReason reason = RetainerCommands.Check(state, SideId, RetainerId, out Fighter? retainer);
            if (reason.IsRejected)
            {
                return reason;
            }

            Squad? squad = state.FindSquad(SquadId);
            if (squad == null)
            {
                return RejectReason.UnknownEntity;
            }

            if (squad.SideId != SideId)
            {
                return RejectReason.WrongSide;
            }

            if (retainer!.SquadId == SquadId)
            {
                return RejectReason.InSquad;
            }

            if (squad.MemberList.Count == 0)
            {
                return RejectReason.NotAlive;
            }

            if (squad.Broken)
            {
                return RejectReason.SquadBroken;
            }

            return squad.MemberList.Count >= SquadSpawnDef.MaxCount ? RejectReason.SquadFull : RejectReason.None;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Fighter retainer = state.FindFighter(RetainerId)!;
            Squad squad = state.FindSquad(SquadId)!;
            Squad? from = retainer.SquadId.IsNone ? null : state.FindSquad(retainer.SquadId);
            if (from != null)
            {
                SquadMembership.Leave(state, from, retainer);
            }

            SquadMembership.Join(state, squad, retainer, atSpawn: false);
        }
    }
}
