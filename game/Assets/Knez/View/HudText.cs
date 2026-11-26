#nullable enable
using Knez.Sim;
using Knez.Sim.Ai;
using Knez.Sim.Combat;
using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;
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

        /// <summary>A stance in a word (M4 step 1).</summary>
        public static string Stance(SquadStance stance) => stance switch
        {
            SquadStance.Advance => "advance",
            SquadStance.Pursue => "pursue",
            _ => "hold",
        };

        /// <summary>"attacking Raider line", "chasing the runners, 6.2 s": what a squad is doing about the enemy (M4 step
        /// 1); empty when nothing.</summary>
        public static string Fight(BattleState state, Squad squad)
        {
            if (squad.Pursuing)
            {
                return "chasing the runners, " + Seconds(squad.PursuitUntil - state.Tick);
            }

            Squad? target = squad.AttackTarget.IsNone ? null : state.FindSquad(squad.AttackTarget);
            return target != null ? "attacking " + target.Name : string.Empty;
        }

        /// <summary>What the enemy AI has a squad doing (M4 step 2): "AI: attacking Hird line", "AI: going round Hird line's
        /// flank", "AI: holding its ground", "AI: falling back to regroup", "AI: leaving the field"; empty when idle.</summary>
        public static string Ai(BattleState state, Squad squad)
        {
            string target = TargetName(state, squad.AiTarget);
            return squad.AiState switch
            {
                SquadAiState.Engage => "AI: attacking " + target,
                SquadAiState.Advance => "AI: going round " + target + "'s flank",
                SquadAiState.Hold => "AI: holding its ground",
                SquadAiState.Regroup => "AI: falling back to regroup",
                SquadAiState.Withdraw => "AI: leaving the field",
                _ => string.Empty,
            };
        }

        /// <summary>The log's line for an AI decision (M4 step 2): "Raider line attacks Hird line", "Raider levy goes round
        /// Hird line's flank", "Raider line turns on Shield wall", "Raider line holds its ground, outnumbered", "Raider line
        /// falls back to regroup", "Raider line leaves the field"; empty when it goes idle.</summary>
        public static string AiLine(BattleState state, SquadAiChanged changed)
        {
            Squad? squad = state.FindSquad(changed.Squad);
            string name = squad != null ? squad.Name : "squad #" + changed.Squad.Value;
            string target = TargetName(state, changed.Target);
            return changed.To switch
            {
                SquadAiState.Engage => changed.From == SquadAiState.Engage ? name + " turns on " + target
                    : changed.From == SquadAiState.Advance ? name + " falls on " + target + "'s flank" : name + " attacks " + target,
                SquadAiState.Advance => name + " goes round " + target + "'s flank",
                SquadAiState.Hold => name + " holds its ground, outnumbered",
                SquadAiState.Regroup => name + " falls back to regroup",
                SquadAiState.Withdraw => name + " leaves the field",
                _ => string.Empty,
            };
        }

        private static string TargetName(BattleState state, EntityId id)
        {
            Squad? squad = id.IsNone ? null : state.FindSquad(id);
            return squad != null ? squad.Name : string.Empty;
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
            "routing" => "he is running",
            "squad-full" => "that squad is full",
            "in-squad" => "he is in a squad",
            "not-in-squad" => "he isn't in a squad",
            "not-an-enemy" => "that man is on his side",
            "cooling-down" => "his ability is still cooling down",
            "not-built" => "that ability isn't built yet",
            "target-broken" => "that squad is running already",
            "ai-side" => "the AI commands that side",
            _ => reason.Code ?? "refused",
        };

        /// <summary>Why a Retainer's order was refused (M3 step 10): the same codes read differently per command.</summary>
        public static string RetainerRefusal(ICommand command, RejectReason reason) => (command, reason.Code) switch
        {
            (AttachRetainerCommand _, "in-squad") => "he is in that squad already",
            (AttachRetainerCommand _, "unknown-entity") => "that squad is gone",
            (AttachRetainerCommand _, "wrong-side") => "that squad isn't yours",
            (AttachRetainerCommand _, "not-alive") => "he, or that squad, is out of the fight",
            (AttachRetainerCommand _, "squad-broken") => "that squad is running",
            (AttachRetainerCommand _, "squad-full") => "that squad is full",
            (RetainerAttackCommand _, "unknown-entity") => "that man is gone",
            (RetainerAttackCommand _, "not-alive") => "he, or that man, is out of the fight",
            (UseAbilityCommand _, "not-in-squad") => "he must stand in a squad to use it",
            (_, "in-squad") => "he is in a squad: take him out of it first (X)",
            (_, "unknown-entity") => "no such Retainer",
            (_, "wrong-side") => "not your Retainer",
            (_, "not-alive") => "he is out of the fight",
            _ => Refusal(reason),
        };

        /// <summary>"Hrafn (loyal, elite)" — a Retainer's name, trait and experience; a plain "#5" for other men.</summary>
        public static string Retainer(Fighter fighter) =>
            fighter.Retainer is RetainerPart part ? $"{part.Name} ({part.Trait.Id}, {fighter.Experience.Id})" : $"#{fighter.Id.Value}";

        /// <summary>"leads: losses −20%, recovers +20%" — what his leadership does for the squad he is in (M3 step 10);
        /// "no leadership" at 0.</summary>
        public static string Leadership(ExperienceDef experience)
        {
            int percent = experience.Leadership / 10;
            return percent > 0 ? $"leads: losses \u2212{percent}%, recovers +{percent}%" : "no leadership";
        }

        /// <summary>An ability's name in words: "battle cry", "stand to the death", … (M3 step 11).</summary>
        public static string AbilityName(AbilityDef ability) => ability.Kind switch
        {
            AbilityKind.BattleCry => "battle cry",
            AbilityKind.CloseRanks => "close ranks",
            AbilityKind.PromiseLoot => "promise loot",
            AbilityKind.StandToTheDeath => "stand to the death",
            AbilityKind.FrontRankRush => "front-rank rush",
            AbilityKind.NoQuarter => "no quarter",
            _ => ability.Id,
        };

        /// <summary>Whether <paramref name="retainer"/> could use his ability now (the command would pass): built, cooled
        /// down, in the fight and not running, and in a squad when it acts on one.</summary>
        public static bool AbilityReady(BattleState state, Fighter retainer)
        {
            if (!(retainer.Retainer is RetainerPart part) || !retainer.IsAlive || retainer.IsRouting)
            {
                return false;
            }

            AbilityDef ability = part.Trait.Ability;
            return ability.Built && state.Tick >= part.AbilityReadyTick && (!ability.OnSquad || !retainer.SquadId.IsNone);
        }

        /// <summary>"battle cry: ready", "battle cry: in 72 s", "close ranks: needs a squad", "promise loot: not built
        /// yet"; empty for a man who isn't a Retainer.</summary>
        public static string AbilityState(BattleState state, Fighter retainer)
        {
            if (!(retainer.Retainer is RetainerPart part))
            {
                return string.Empty;
            }

            AbilityDef ability = part.Trait.Ability;
            string name = AbilityName(ability);
            if (!ability.Built)
            {
                return name + ": not built yet";
            }

            if (state.Tick < part.AbilityReadyTick)
            {
                int seconds = (part.AbilityReadyTick - state.Tick + BattleState.TicksPerSecond - 1) / BattleState.TicksPerSecond;
                return $"{name}: in {seconds} s";
            }

            return ability.OnSquad && retainer.SquadId.IsNone ? name + ": needs a squad" : name + ": ready";
        }

        /// <summary>"ranks closed 6.0 s · standing to the death 2.0 s": the lasting abilities acting on a squad and the time
        /// they have left; empty when none (M3 step 11).</summary>
        public static string Effects(BattleState state, Squad squad)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (AbilityEffect effect in squad.Effects)
            {
                if (state.Tick < effect.Until)
                {
                    string what = effect.Ability.Kind == AbilityKind.CloseRanks ? "ranks closed"
                        : effect.Ability.Kind == AbilityKind.StandToTheDeath ? "standing to the death" : AbilityName(effect.Ability);
                    parts.Add(what + " " + Seconds(effect.Until - state.Tick));
                }
            }

            return string.Join(" \u00b7 ", parts);
        }

        /// <summary>What a Retainer standing alone has been told: "walking to (30.0, 12.5)", "going at #33 (Raider line)",
        /// "standing alone".</summary>
        public static string LoneOrder(BattleState state, Fighter fighter)
        {
            LoneOrder order = fighter.LoneOrder;
            if (order.Kind == LoneOrderKind.Move)
            {
                return "walking to (" + Metres(order.Target.X) + ", " + Metres(order.Target.Y) + ")";
            }

            return order.Kind == LoneOrderKind.Attack ? "going at " + Name(state, order.Enemy) : "standing alone";
        }

        // Plain C# here (no Unity vectors): HudText runs in the .NET test harness too.
        private static string Metres(Fix value) =>
            (value.Raw / (double)Fix.OneRaw).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

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
            MoraleState.Left => "left the field",
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

            if (fighter.MoraleState == MoraleState.Left)
            {
                return "left the field with his squad";
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

        /// <summary>"#33 (Raider line)"; a Retainer by name: "Hrafn (Veterans)", "Vesti".</summary>
        public static string Name(BattleState state, EntityId fighterId)
        {
            Fighter? fighter = fighterId.IsNone ? null : state.FindFighter(fighterId);
            if (fighter == null)
            {
                return "no one";
            }

            Squad? squad = fighter.SquadId.IsNone ? null : state.FindSquad(fighter.SquadId);
            return Name(fighter, squad);
        }

        /// <summary>"#33 (Raider line)" or a Retainer's "Hrafn (Veterans)", with the squad given (null: none).</summary>
        public static string Name(Fighter fighter, Squad? squad)
        {
            string who = fighter.Retainer is RetainerPart part ? part.Name : $"#{fighter.Id.Value}";
            return squad != null ? $"{who} ({squad.Name})" : who;
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
            MoraleReason.LeaderFell => "their leader fell",
            MoraleReason.BattleCry => "a battle cry",
            MoraleReason.EnemyBattleCry => "an enemy's battle cry",
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

        /// <summary>"Hird line: that point is off the map" — the squad's name when the command names one; a Retainer's
        /// order names him: "Hrafn: that squad is full".</summary>
        public static string Refusal(BattleState state, ICommand command, RejectReason reason)
        {
            EntityId retainerId = command switch
            {
                AttachRetainerCommand attach => attach.RetainerId,
                DetachRetainerCommand detach => detach.RetainerId,
                MoveRetainerCommand move => move.RetainerId,
                RetainerAttackCommand attack => attack.RetainerId,
                UseAbilityCommand ability => ability.RetainerId,
                _ => EntityId.None,
            };
            if (!retainerId.IsNone)
            {
                Fighter? retainer = state.FindFighter(retainerId);
                return (retainer?.Retainer is RetainerPart part ? part.Name + ": " : "Order refused: ") + RetainerRefusal(command, reason);
            }

            EntityId squadId = command switch
            {
                MoveSquadCommand move => move.SquadId,
                SetFormationCommand formation => formation.SquadId,
                AttackSquadCommand attack => attack.SquadId,
                SetStanceCommand stance => stance.SquadId,
                _ => EntityId.None,
            };
            Squad? squad = squadId.IsNone ? null : state.FindSquad(squadId);
            string why = command is AttackSquadCommand ? AttackRefusal(reason) : Refusal(reason);
            return (squad != null ? squad.Name + ": " : "Order refused: ") + why;
        }

        /// <summary>Why an attack on a squad was refused (M4 step 1): some codes read differently than for a move.</summary>
        public static string AttackRefusal(RejectReason reason) => reason.Code switch
        {
            "not-an-enemy" => "that squad is yours",
            "unknown-entity" => "that squad is gone",
            "not-alive" => "nobody is left to fight",
            _ => Refusal(reason),
        };
    }
}
