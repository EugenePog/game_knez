using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// A Retainer standing alone walks to <see cref="Target"/> and then faces <see cref="Facing"/>, or keeps the way he
    /// walked (battle-sim.md §8, §13, M3 step 10). A target inside a wall becomes the nearest open ground. Rejected as
    /// unknown-entity, wrong-side, not-alive, routing, in-squad (move his squad instead, or detach him first) or off-map.
    /// </summary>
    public sealed class MoveRetainerCommand : ICommand
    {
        /// <summary>Creates the command for the side that owns him.</summary>
        public MoveRetainerCommand(int sideId, EntityId retainerId, FixVec2 target, Angle? facing = null)
        {
            SideId = sideId;
            RetainerId = retainerId;
            Target = target;
            Facing = facing;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The Retainer (a fighter id).</summary>
        public EntityId RetainerId { get; }

        /// <summary>Where he goes, metres; must be on the map.</summary>
        public FixVec2 Target { get; }

        /// <summary>Facing on arrival, or null for the way he walked.</summary>
        public Angle? Facing { get; }

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

            return state.Scenario.Map.Contains(Target) ? RejectReason.None : RejectReason.OffMap;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Fighter retainer = state.FindFighter(RetainerId)!;
            FixVec2 target = Terrain.Constrain(state.Scenario.Map, Target, retainer.Radius);
            retainer.LoneOrder = LoneOrder.Move(target, Facing);
        }
    }
}
