using Knez.Sim.Defs;
using Knez.Sim.Formations;
using Knez.Sim.Movement;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>
    /// Order a squad into another formation (line, shield wall, column, …) from the scenario's catalogue
    /// (battle-sim.md §8, §10). It re-forms on the move (decided 2026-11-21): its front is set for its living men, each new
    /// place goes to the nearest man, and it keeps its order, marching at <see cref="MovementDef.ReformPace"/> until
    /// formed up. A move order is re-planned for the new shape.
    /// </summary>
    public sealed class SetFormationCommand : ICommand
    {
        /// <summary>The reason given for a formation id the scenario's catalogue doesn't have.</summary>
        public static readonly RejectReason UnknownFormation = RejectReason.Because("unknown-formation");

        /// <summary>Creates the command for the side that owns the squad.</summary>
        public SetFormationCommand(int sideId, EntityId squadId, string formationId)
        {
            SideId = sideId;
            SquadId = squadId;
            FormationId = formationId;
        }

        /// <inheritdoc/>
        public int SideId { get; }

        /// <summary>The squad to re-form.</summary>
        public EntityId SquadId { get; }

        /// <summary>The formation's content id.</summary>
        public string FormationId { get; }

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

            if (squad.MemberList.Count == 0)
            {
                return RejectReason.NotAlive;
            }

            if (squad.Broken)
            {
                return RejectReason.SquadBroken; // its men are running (battle-sim.md §12)
            }

            return state.Scenario.FindFormation(FormationId) == null ? UnknownFormation : RejectReason.None;
        }

        /// <inheritdoc/>
        public void Apply(BattleState state, TickContext context)
        {
            Squad squad = state.FindSquad(SquadId)!;
            FormationDef formation = state.Scenario.FindFormation(FormationId)!;
            int count = squad.MemberList.Count;
            squad.Formation = formation;
            squad.Frontage = FormationLayout.Width(formation, count);
            if (squad.Files >= squad.Frontage)
            {
                squad.Files = 0; // narrower than the gap needs anyway
            }

            SlotAssignment.Nearest(state, squad);
            squad.Reforming = true;
            if (squad.Order.Kind == SquadOrderKind.Move)
            {
                PathPlanner.Request(state, squad); // the route was planned for the old shape
            }
        }

        /// <inheritdoc/>
        public override string ToString() => $"SetFormation {SquadId} to {FormationId}";
    }
}
