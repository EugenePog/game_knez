#nullable enable
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Game.View
{
    /// <summary>The words the HUD shows (M2 step 6): what a squad is doing, and why an order was refused. Plain C#.</summary>
    public static class HudText
    {
        /// <summary>A squad's state in a few words, most specific first.</summary>
        public static string Status(Squad squad)
        {
            if (squad.Members.Count == 0)
            {
                return "destroyed";
            }

            if (squad.Order.Kind == SquadOrderKind.Move)
            {
                if (squad.PathRequestTick >= 0)
                {
                    return "planning a route";
                }

                if (squad.RegroupSince >= 0)
                {
                    return $"regrouping into {squad.Files} files";
                }

                if (squad.Reforming)
                {
                    return "re-forming on the march";
                }

                return squad.Files > 0 ? $"marching in {squad.Files} files" : "marching";
            }

            return squad.Reforming ? "re-forming" : "holding";
        }

        /// <summary>Why an order was refused, for the player.</summary>
        public static string Refusal(RejectReason reason) => reason.Code switch
        {
            "unknown-entity" => "that squad is gone",
            "wrong-side" => "not your squad",
            "not-alive" => "no one is left in it",
            "off-map" => "that point is off the map",
            "unknown-formation" => "that formation isn't available here",
            _ => reason.Code ?? "refused",
        };

        /// <summary>"Hird line: that point is off the map" — the squad's name when the command names one.</summary>
        public static string Refusal(BattleState state, ICommand command, RejectReason reason)
        {
            EntityId squadId = command switch
            {
                MoveSquadCommand move => move.SquadId,
                SetFormationCommand formation => formation.SquadId,
                _ => EntityId.None,
            };
            Squad? squad = squadId.IsNone ? null : state.FindSquad(squadId);
            return (squad != null ? squad.Name + ": " : "Order refused: ") + Refusal(reason);
        }
    }
}
