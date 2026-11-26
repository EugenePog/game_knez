#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Units;
using EntityId = Knez.Sim.EntityId;

namespace Knez.Game.View
{
    /// <summary>
    /// The battle in words, line by line (M3 step 8): the first blow, wounds and men going down, men running, rallying
    /// and fleeing, squads breaking — with why — and re-forming, refused orders, the result. Built from the sim's events
    /// by <see cref="BattleRecord"/>; plain C#, never touches the state. Shaken and wavering aren't logged (the morale
    /// bars show them), nor blows turned aside (too many), nor each man swept along when his squad breaks.
    /// </summary>
    public sealed class BattleLog
    {
        /// <summary>Lines kept; older ones are dropped.</summary>
        public const int MaxLines = 2000;

        private readonly List<LogLine> _lines = new List<LogLine>();
        private readonly Dictionary<int, string> _whyBroke = new Dictionary<int, string>();
        private readonly SquadTally _tally;
        private readonly MoraleLedger _ledger;
        private bool _firstBlow;

        /// <summary>Creates a log that names men by the squad they started in and explains breaks from the ledger.</summary>
        public BattleLog(SquadTally tally, MoraleLedger ledger)
        {
            _tally = tally;
            _ledger = ledger;
        }

        /// <summary>The lines, oldest first.</summary>
        public IReadOnlyList<LogLine> Lines => _lines;

        /// <summary>Lines added since the battle began (grows even when old lines are dropped).</summary>
        public int Added { get; private set; }

        /// <summary>Why a squad broke the last time it did, or null.</summary>
        public string? WhyBroke(EntityId squad) => _whyBroke.TryGetValue(squad.Value, out string? why) ? why : null;

        /// <summary>Starts a new battle's log.</summary>
        public void Begin()
        {
            _lines.Clear();
            _whyBroke.Clear();
            _firstBlow = false;
            Added = 0;
        }

        /// <summary>
        /// Writes the line for one event of the tick just stepped, after the ledger has taken the whole tick.
        /// <paramref name="sweptAlong"/>: this event sets a man running only because his squad broke — the break's own
        /// line counts him, so he gets no line of his own.
        /// </summary>
        public void Apply(BattleState state, SimEvent simEvent, bool sweptAlong)
        {
            switch (simEvent)
            {
                case BlowStruck blow when !_firstBlow:
                    _firstBlow = true;
                    Add(blow.Tick, LogKind.FirstBlow, SideOf(state, blow.Attacker), $"First blow: {Name(state, blow.Attacker)} at {Name(state, blow.Defender)}");
                    break;
                case Wounded wounded:
                    string by = Name(state, wounded.By);
                    if (wounded.Now == WoundLevel.Down)
                    {
                        Add(wounded.Tick, LogKind.Fell, SideOf(state, wounded.Fighter), $"{Name(state, wounded.Fighter)} falls, struck by {by}");
                    }
                    else
                    {
                        string how = wounded.Now == WoundLevel.Heavy ? "heavily" : "lightly";
                        Add(wounded.Tick, LogKind.Wound, SideOf(state, wounded.Fighter), $"{Name(state, wounded.Fighter)} {how} wounded by {by}");
                    }

                    break;
                case MoraleStateChanged changed when !(sweptAlong && changed.To == MoraleState.Routing):
                    State(state, changed);
                    break;
                case SquadBroke broke:
                    Squad? squad = state.FindSquad(broke.Squad);
                    string name = squad != null ? squad.Name : $"squad #{broke.Squad.Value}";
                    int side = squad != null ? squad.SideId : -1;
                    if (broke.Reformed)
                    {
                        Add(broke.Tick, LogKind.Reformed, side, $"{name} re-formed: {broke.InFight - broke.Running} of {broke.InFight} back");
                    }
                    else
                    {
                        string why = HudText.WhyBroke(state.Scenario.Morale, broke, Down(state, broke.Squad, out int started), started,
                            _ledger.Of(state, broke.Squad));
                        _whyBroke[broke.Squad.Value] = why;
                        Add(broke.Tick, LogKind.Broke, side, $"{name} broke. {why}");
                    }

                    break;
                case CommandRejected rejected:
                    Add(rejected.Tick, LogKind.Refused, -1, "Refused — " + HudText.Refusal(state, rejected.Command, rejected.Reason));
                    break;
                case BattleEnded ended:
                    Add(ended.Tick, LogKind.Ended, ended.Winner, ended.Winner < 0 ? "Nobody holds the field." : $"Side {ended.Winner} holds the field.");
                    break;
                case AbilityUsed used:
                    Add(used.Tick, LogKind.Ability, SideOf(state, used.Retainer), Used(state, used));
                    break;
            }
        }

        private void State(BattleState state, MoraleStateChanged changed)
        {
            Fighter? man = state.FindFighter(changed.Fighter);
            int side = man != null ? man.SideId : -1;
            string name = Name(state, changed.Fighter);
            if (changed.To == MoraleState.Routing)
            {
                Add(changed.Tick, LogKind.Ran, side, $"{name} runs (morale {(man != null ? man.Morale : 0)})");
            }
            else if (changed.To == MoraleState.Rallying)
            {
                Add(changed.Tick, LogKind.Rallied, side, $"{name} rallies");
            }
            else if (changed.To == MoraleState.Fled)
            {
                Add(changed.Tick, LogKind.Fled, side, $"{name} fled the field");
            }
            else if (changed.From == MoraleState.Rallying)
            {
                Add(changed.Tick, LogKind.Rallied, side, $"{name} is back in his place");
            }
        }

        /// <summary>"#5 (Hird line)", by the squad he started in (the fallen leave their squad in the sim); a Retainer by
        /// name and the squad he counts for: "Hrafn (Veterans)", "Vesti" (M3 step 10).</summary>
        public string Name(BattleState state, EntityId fighter)
        {
            if (fighter.IsNone)
            {
                return "no one";
            }

            Fighter? man = state.FindFighter(fighter);
            Squad? squad = state.FindSquad(man != null ? _tally.HomeOf(man) : _tally.HomeOf(fighter));
            if (man != null)
            {
                return HudText.Name(man, squad);
            }

            return squad != null ? $"#{fighter.Value} ({squad.Name})" : $"#{fighter.Value}";
        }

        /// <summary>"Hrafn gives a battle cry", "Veterans stand to the death for 5.0 s at Hrafn's word" (M3 step 11).</summary>
        private string Used(BattleState state, AbilityUsed used)
        {
            Fighter? retainer = state.FindFighter(used.Retainer);
            string name = retainer?.Retainer is RetainerPart part ? part.Name : Name(state, used.Retainer);
            AbilityDef? ability = retainer?.Retainer is RetainerPart his ? his.Trait.Ability : null;
            Squad? squad = used.Squad.IsNone ? null : state.FindSquad(used.Squad);
            if (ability == null || ability.Kind == AbilityKind.BattleCry || squad == null)
            {
                return $"{name} gives a {(ability != null ? HudText.AbilityName(ability) : used.Ability)}";
            }

            string what = ability.Kind == AbilityKind.CloseRanks ? "close ranks" : HudText.AbilityName(ability);
            return $"{squad.Name} {what} for {HudText.Seconds(ability.DurationTicks)} at {name}'s word";
        }

        private static int SideOf(BattleState state, EntityId fighter)
        {
            Fighter? man = fighter.IsNone ? null : state.FindFighter(fighter);
            return man != null ? man.SideId : -1;
        }

        private int Down(BattleState state, EntityId squad, out int started)
        {
            started = 0;
            int down = 0;
            foreach (Fighter fighter in state.Fighters)
            {
                if (_tally.HomeOf(fighter) == squad)
                {
                    started++;
                    down += fighter.Wound == WoundLevel.Down ? 1 : 0;
                }
            }

            return down;
        }

        private void Add(int tick, LogKind kind, int side, string text)
        {
            if (_lines.Count >= MaxLines)
            {
                _lines.RemoveRange(0, MaxLines / 4);
            }

            _lines.Add(new LogLine(tick, kind, side, text));
            Added++;
        }
    }
}
