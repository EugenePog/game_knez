#nullable enable
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Game.View
{
    /// <summary>The words the HUD shows (M2 step 6, M3): what a squad is doing, why an order was refused, kits, wounds,
    /// morale and what a man is doing. Plain C#.</summary>
    public static class HudText
    {
        /// <summary>A squad's state in a few words, most specific first.</summary>
        public static string Status(Squad squad)
        {
            if (squad.Members.Count == 0)
            {
                return "destroyed";
            }

            if (squad.Broken)
            {
                return "broken, running";
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
            "squad-broken" => "its men are running",
            _ => reason.Code ?? "refused",
        };

        /// <summary>"sword · round shield · padded · cap · experienced" (and the warrior type unless default).</summary>
        public static string Kit(FighterKit kit)
        {
            string shield = kit.Shield.Id == "none" ? "no shield" : kit.Shield.Id + " shield";
            string body = kit.BodyArmour.Id == "none" ? "no armour" : kit.BodyArmour.Id;
            string head = kit.HeadCover.Id == "none" ? "bare head" : kit.HeadCover.Id;
            string type = kit.Type.Id == "default" ? string.Empty : " · " + kit.Type.Id;
            return $"{kit.Weapon.Id} · {shield} · {body} · {head} · {kit.Experience.Id}{type}";
        }

        /// <summary>A wound in words.</summary>
        public static string Wound(WoundLevel wound) => wound switch
        {
            WoundLevel.Light => "lightly wounded",
            WoundLevel.Heavy => "heavily wounded",
            WoundLevel.Down => "fallen",
            _ => "unhurt",
        };

        /// <summary>A morale state in a word.</summary>
        public static string MoraleWord(MoraleState state) => state switch
        {
            MoraleState.Shaken => "shaken",
            MoraleState.Wavering => "wavering",
            MoraleState.Routing => "running",
            MoraleState.Rallying => "rallying",
            MoraleState.Fled => "fled",
            _ => "steady",
        };

        /// <summary>The morale word a squad's average stands for (the man-by-man thresholds, without hysteresis).</summary>
        public static string MoraleWord(MoraleDef rules, int morale) =>
            morale < rules.Routing ? "breaking" : morale < rules.Wavering ? "wavering" : morale < rules.Shaken ? "shaken" : "steady";

        /// <summary>What a man is doing: "winding up at #33 (Raider line), 0.4 s", "recovering, 0.6 s", "ready", …</summary>
        public static string Action(BattleState state, Fighter fighter)
        {
            if (fighter.Wound == WoundLevel.Down)
            {
                return "fallen";
            }

            if (fighter.MoraleState == MoraleState.Fled)
            {
                return "fled the field";
            }

            if (fighter.IsRouting)
            {
                return "running for his edge";
            }

            string seconds = $"{fighter.ActionTicks / (float)BattleState.TicksPerSecond:0.0} s";
            if (fighter.Action == FighterAction.Windup)
            {
                return $"winding up a blow at {Name(state, fighter.Target)}, {seconds}";
            }

            if (fighter.Action == FighterAction.Recovery)
            {
                return $"recovering from a blow, {seconds}";
            }

            return fighter.IsEngaged ? $"facing {Name(state, fighter.Target)}" : "ready";
        }

        /// <summary>"140‰ from the front, got through" — a blow in words.</summary>
        public static string Blow(BlowStruck blow) =>
            $"{blow.Chance}‰ from the {blow.Sector.ToString().ToLowerInvariant()}, {(blow.Landed ? "got through" : "turned aside")}";

        /// <summary>"#33 (Raider line)".</summary>
        public static string Name(BattleState state, EntityId fighterId)
        {
            Fighter? fighter = fighterId.IsNone ? null : state.FindFighter(fighterId);
            if (fighter == null)
            {
                return "no one";
            }

            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            return squad != null ? $"#{fighter.Id.Value} ({squad.Name})" : $"#{fighter.Id.Value}";
        }

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
