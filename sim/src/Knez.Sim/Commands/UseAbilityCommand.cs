using Knez.Sim.Abilities;
using Knez.Sim.Defs;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// A Retainer uses his trait's active ability (battle-sim.md §8, §13, M3 step 11). No target: a battle cry acts round
    /// him, the lasting abilities on his squad. Rejected as unknown-entity, wrong-side, not-alive, routing, not-built
    /// (promise loot, front-rank rush, no quarter), cooling-down, not-in-squad (a squad ability while he stands alone) or
    /// squad-broken (a squad ability on a broken squad, which takes no orders until it re-forms, §12).
    /// </summary>
    public sealed class UseAbilityCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns him.</summary>
        public UseAbilityCommand(int sideId, EntityId retainerId)
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

            RetainerPart part = retainer!.Retainer!.Value;
            AbilityDef ability = part.Trait.Ability;
            if (!ability.Built)
            {
                return RejectReason.NotBuilt;
            }

            if (state.Tick < part.AbilityReadyTick)
            {
                return RejectReason.CoolingDown;
            }

            if (!ability.OnSquad)
            {
                return RejectReason.None;
            }

            Squad? squad = retainer.SquadId.IsNone ? null : state.FindSquad(retainer.SquadId);
            return squad == null ? RejectReason.NotInSquad : squad.Broken ? RejectReason.SquadBroken : RejectReason.None;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context) => AbilityRules.Use(state, state.FindFighter(RetainerId)!);
    }
}
