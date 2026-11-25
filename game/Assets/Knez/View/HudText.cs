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

        /// <summary>"sword · round shield · padded · cap" — what a kit holds (M3 step 9).</summary>
        public static string Kit(KitDef kit)
        {
            string shield = kit.Shield.Id == "none" ? "no shield" : kit.Shield.Id + " shield";
            string body = kit.BodyArmour.Id == "none" ? "no armour" : kit.BodyArmour.Id;
            string head = kit.HeadCover.Id == "none" ? "bare head" : kit.HeadCover.Id;
            return $"{kit.Weapon.Id} · {shield} · {body} · {head}";
        }

        /// <summary>"sword · round shield · mail · helm · elite" — a man's kit and his experience.</summary>
        public static string Kit(KitDef kit, ExperienceDef experience) => Kit(kit) + " · " + experience.Id;

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

        /// <summary>Why a man's morale moved, in a few words (battle-sim.md §12 reason codes).</summary>
        public static string Reason(MoraleReason reason) => reason switch
        {
            MoraleReason.SquadmateFell => "mates fell",
            MoraleReason.Wounded => "own wounds",
            MoraleReason.SquadmateHeavilyWounded => "mates badly hurt",
            MoraleReason.StruckFromFlank => "struck from the flank",
            MoraleReason.StruckFromRear => "struck from behind",
            MoraleReason.SquadFlanked => "squad hit in the flank",
            MoraleReason.SquadTakenInRear => "squad hit from behind",
            MoraleReason.SquadmateRouted => "mates ran",
            MoraleReason.AlliedSquadBroke => "an allied squad broke",
            MoraleReason.EnemySquadBroke => "an enemy squad broke",
            _ => reason.ToString(),
        };

        /// <summary>"24.0 s" (a point, whatever the machine's locale).</summary>
        public static string Seconds(int tick) => Clock(tick) + " s";

        /// <summary>"24.0": seconds since the battle began, one decimal, a point whatever the machine's locale.</summary>
        public static string Clock(int tick) =>
            (tick / (float)BattleState.TicksPerSecond).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>"+100", "−250" (a true minus sign), "0".</summary>
        public static string Signed(int value) => value > 0 ? "+" + value : value < 0 ? "\u2212" + (-value) : "0";

        /// <summary>
        /// "750 → 170: mates fell −500, mates ran −60, own wounds −38, recovered +12" — an account in words: where morale
        /// started and is, then up to <paramref name="most"/> reasons that moved it by at least 5 a man, largest first.
        /// </summary>
        public static string Account(MoraleAccount account, int most = 4)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var (reason, average) in account.Reasons)
            {
                if (parts.Count < most && System.Math.Abs(average) >= 5)
                {
                    parts.Add($"{Reason(reason)} {Signed(average)}");
                }
            }

            if (account.Recovered >= 5)
            {
                parts.Add($"recovered out of reach {Signed(account.Recovered)}");
            }

            string moves = parts.Count > 0 ? ": " + string.Join(", ", parts) : string.Empty;
            return $"{account.Start} \u2192 {account.Now}{moves}";
        }

        /// <summary>"8 experienced: sword · round shield · padded · cap" — one group of a squad's men, same experience and
        /// kit (M3 step 9); "(5 left)" once some are out of the fight.</summary>
        public static string Group(Fighter sample, int count, int inFight) =>
            $"{count} {sample.Experience.Id}{(inFight < count ? $" ({inFight} left)" : string.Empty)}: {Kit(sample.Kit)}";

        /// <summary>
        /// Why a squad broke, in words (M3 step 8): "5 of 7 men in the fight were running — more than half — so the other 2
        /// ran with them. 1 of 8 down. Morale 750 → 170: mates fell −250, …", or "All 6 men left in the fight were
        /// running. …" when nobody was swept along. The counts are the sim's own (<see cref="SquadBroke"/>); the account is
        /// the ledger's for the men still in the fight.
        /// </summary>
        public static string WhyBroke(MoraleDef rules, SquadBroke broke, int down, int started, MoraleAccount account)
        {
            int swept = broke.InFight - broke.Running;
            string running;
            if (swept > 0)
            {
                string share = rules.BreakShare == 500 ? "more than half"
                    : "more than " + (rules.BreakShare / 10.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
                running = $"{broke.Running} of {broke.InFight} men in the fight were running \u2014 {share} \u2014 so the other {swept} ran with them.";
            }
            else
            {
                running = broke.InFight == 1 ? "Its last man in the fight was running." : $"All {broke.InFight} men left in the fight were running.";
            }

            string lost = down > 0 ? $" {down} of {started} down." : string.Empty;
            return $"{running}{lost} Morale {Account(account)}.";
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
