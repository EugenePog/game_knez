#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.View
{
    /// <summary>
    /// The on-screen panels (M2 step 6, M3), drawn with Unity's immediate-mode GUI so they need no scene set-up. Top left:
    /// the clock, speed and pause and, once decided, the result; then every squad (scrolling when they don't fit) with
    /// its formation, men in the fight and state, who it started with, how its men are hurt, its blows, its average
    /// morale as a bar, one cell per man coloured by his morale state, how many must run before it breaks, and what has
    /// cost it morale — once broken, why it broke (M3 step 8); its Retainers by name, trait, experience and leadership,
    /// and after the squads the Retainers standing alone with what they were told (M3 step 10). Bottom left: the battle
    /// log (L hides it). Top right: the man under the mouse, with what moved his morale. M3 step 11: each Retainer's
    /// ability and its cooldown, the abilities acting on a squad, and buttons for the selection's Retainers: his ability
    /// (as Q) and his chain to the squad (as U; <see cref="RetainerChains"/>). Refused orders fade after a few seconds. Reads the state only;
    /// <see cref="Covers"/> tells input which screen areas are the HUD's.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        private const float RefusalSeconds = 4f;
        private const float PanelWidth = 450;
        private const float LogHeight = 200;
        private const int LogShown = 200; // the newest lines the log panel lays out

        [SerializeField] private BattleRunner? runner;

        private readonly List<(string Text, float Until)> _refusals = new List<(string, float)>();
        private readonly List<Rect> _panels = new List<Rect>();
        private readonly BattleRecord _record = new BattleRecord();
        private string? _result;
        private GUIStyle? _label;
        private GUIStyle? _small;
        private GUIStyle? _wrap;
        private Vector2 _squadScroll;
        private Vector2 _logScroll;
        private bool _followLog = true;

        /// <summary>The Retainer the player has selected on his own (set by input), or none: his ability and chain get buttons.</summary>
        public EntityId SelectedRetainer { get; set; } = EntityId.None;

        /// <summary>The squads the player has selected (set by input): their chained Retainers get buttons.</summary>
        public IReadOnlyList<EntityId> SelectedSquads { get; set; } = new List<EntityId>();

        /// <summary>Which of the player's Retainers are chained to their squads; input reads it, the buttons change it.</summary>
        public RetainerChains Chains { get; } = new RetainerChains();

        /// <summary>A button unchained a Retainer: input selects him.</summary>
        public event System.Action<EntityId>? RetainerPicked;

        /// <summary>A button chained a Retainer back: input selects his squad.</summary>
        public event System.Action<EntityId>? SquadPicked;

        /// <summary>The side the player commands (set by input).</summary>
        public int PlayerSide { get; set; }

        /// <summary>Whether the battle log panel is shown (L).</summary>
        public bool ShowLog { get; set; } = true;

        /// <summary>The view's record of the battle: tally, morale ledger, log.</summary>
        public BattleRecord Record => _record;

        /// <summary>Whether a screen point (pixels, origin bottom left, as input reports it) lies on a HUD panel drawn last
        /// frame — input over the HUD isn't meant for the map.</summary>
        public bool Covers(Vector2 screen)
        {
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            foreach (Rect panel in _panels)
            {
                if (panel.Contains(gui))
                {
                    return true;
                }
            }

            return false;
        }

        private void Start()
        {
            if (runner == null) // Unity's ==: an empty Inspector field is a "fake null" that ?? misses
            {
                runner = GetComponent<BattleRunner>();
            }

            if (runner != null)
            {
                runner.Ticked += OnTicked;
                if (runner.Simulation != null)
                {
                    EnsureRecord(runner.Simulation.State); // Start runs before the first Update steps the battle: tick 0
                }
            }
        }

        private void OnDestroy()
        {
            if (runner != null)
            {
                runner.Ticked -= OnTicked;
            }
        }

        private void OnTicked(BattleState state)
        {
            EnsureRecord(state);
            _record.Observe(state);
            foreach (SimEvent simEvent in state.Events)
            {
                if (simEvent is CommandRejected rejected)
                {
                    _refusals.Add((HudText.Refusal(state, rejected.Command, rejected.Reason), Time.unscaledTime + RefusalSeconds));
                }
                else if (simEvent is BattleEnded ended)
                {
                    _result = ended.Winner < 0 ? "Nobody holds the field."
                        : $"Side {ended.Winner} holds the field ({HudText.Seconds(ended.Tick)}).";
                }
            }
        }

        private void EnsureRecord(BattleState state)
        {
            if (!_record.Begun)
            {
                _record.Begin(state); // before the first step, the battle as it starts
            }
        }

        private void OnGUI()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            _wrap ??= new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            _panels.Clear();
            if (runner == null || sim == null)
            {
                GUI.Label(new Rect(12, 12, 900, 24), runner != null && runner.Error != null ? runner.Error : "No battle loaded.", _label);
                return;
            }

            BattleState state = sim.State;
            EnsureRecord(state);
            float logTop = ShowLog ? Screen.height - 54 - LogHeight : Screen.height - 54; // two help lines below
            DrawSquads(state, runner, logTop - 8);
            if (ShowLog)
            {
                DrawLog(new Rect(6, logTop, Mathf.Min(760, Screen.width - 12), LogHeight));
            }

            DrawHovered(state);
            float actionsTop = DrawActions(state, runner, logTop - 8);

            float now = Time.unscaledTime;
            _refusals.RemoveAll(r => r.Until < now);
            float ry = actionsTop - 4 - (_refusals.Count * 20);
            foreach (var (text, _) in _refusals)
            {
                GUI.Label(new Rect(PanelWidth + 20, ry, 700, 20), "<color=#ff9f7f>" + text + "</color>", _label);
                ry += 20;
            }

            GUI.Label(new Rect(12, Screen.height - 48, Screen.width - 24, 22),
                "<color=#cccccc>Left: select (drag: box, Shift: add, Esc: clear) · Right: move (drag: facing), on an enemy: attack his squad · H/G/P: hold/advance/pursue · 1/2/3: line/shield wall/column · Space: pause · =/−: speed · WASD/middle drag: pan · scroll: zoom · L: log · B: morale bars · point at a man for details</color>",
                _small);
            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 22),
                "<color=#e8c35a>Retainer (gold ring):</color><color=#cccccc> in a squad he is chained to it: clicks and orders go to the squad, Q uses his ability · U or the button unchains him: then drag him onto a squad to join it, onto open ground to walk there; right-click ground / an enemy / a squad: walk / go at him / join; X: leave the squad · U again chains him back</color>",
                _small);
        }

        /// <summary>The clock and every squad, in a panel that scrolls when the squads don't fit above <paramref name="bottom"/>.</summary>
        private void DrawSquads(BattleState state, BattleRunner battle, float bottom)
        {
            float inner = PanelWidth - 24;
            var blocks = new List<(Squad Squad, float Height)>();
            float content = 0;
            foreach (Squad squad in state.Squads)
            {
                float height = SquadHeight(state, squad, inner);
                blocks.Add((squad, height));
                content += height;
            }

            List<Fighter> alone = _record.Tally.RetainersOf(state, EntityId.None);
            float aloneHeight = alone.Count > 0 ? 15 + (alone.Count * 30) + 6 : 0;
            content += aloneHeight;

            float header = _result != null ? 48 : 28;
            float panelHeight = Mathf.Min(header + content + 8, Mathf.Max(120, bottom - 6));
            var panel = new Rect(6, 6, PanelWidth, panelHeight);
            _panels.Add(panel);
            GUI.Box(panel, GUIContent.none);
            string clock = $"<b>{state.Scenario.Id}</b>   tick {state.Tick} ({HudText.Seconds(state.Tick)})   " +
                (battle.Clock.Paused ? "<color=#ffcc55>PAUSED</color>" : $"×{battle.Clock.Speed:0.##}");
            GUI.Label(new Rect(14, 10, PanelWidth - 16, 22), clock, _label);
            if (_result != null)
            {
                GUI.Label(new Rect(14, 30, PanelWidth - 16, 20), "<color=#ffe08a><b>" + _result + "</b></color>", _small);
            }

            var view = new Rect(8, 6 + header, PanelWidth - 4, panelHeight - header - 4);
            _squadScroll = GUI.BeginScrollView(view, _squadScroll, new Rect(0, 0, PanelWidth - 24, content));
            float y = 0;
            foreach (var (squad, height) in blocks)
            {
                DrawSquad(state, squad, new Rect(6, y, inner, height));
                y += height;
            }

            if (alone.Count > 0)
            {
                DrawAlone(state, alone, new Rect(6, y, inner, aloneHeight));
            }

            GUI.EndScrollView();
        }

        private float SquadHeight(BattleState state, Squad squad, float width)
        {
            int composition = _record.Tally.Composition(state, squad).Count;
            int retainers = _record.Tally.RetainersOf(state, squad.Id).Count;
            int groups = composition + retainers > 0 ? composition + retainers : 1;
            string? why = Why(state, squad);
            float whyHeight = why != null ? _wrap!.CalcHeight(new GUIContent(why), width) : 0;
            return 15 + (groups * 15) + 17 + 16 + 14 + whyHeight + 10; // as DrawSquad lays them out
        }

        /// <summary>Why a broken squad broke, or what has cost a standing one morale so far; null when nothing has.</summary>
        private string? Why(BattleState state, Squad squad)
        {
            if (squad.Broken)
            {
                string? why = _record.Log.WhyBroke(squad.Id);
                return "<color=#ff9f7f><b>Broke:</b> " + (why ?? "its men ran.") + "</color>";
            }

            MoraleAccount account = _record.Ledger.Of(state, squad.Id);
            if (account.Men == 0 || account.Reasons.Count == 0)
            {
                return null;
            }

            return "<color=#c8c8c8>Morale " + HudText.Account(account, 3) + "</color>";
        }

        private void DrawSquad(BattleState state, Squad squad, Rect at)
        {
            MoraleDef rules = state.Scenario.Morale;
            SquadStats stats = _record.Tally.Of(state, squad);
            var composition = _record.Tally.Composition(state, squad);
            List<Fighter> retainers = _record.Tally.RetainersOf(state, squad.Id);
            int total = retainers.Count;
            foreach (var group in composition)
            {
                total += group.Count;
            }

            float x = at.x;
            float y = at.y;
            string side = squad.SideId == 0 ? "#7fb2ff" : "#ff8f7f";
            string effects = HudText.Effects(state, squad);
            string fight = HudText.Fight(state, squad);
            string ai = HudText.Ai(state, squad);
            fight = ai.Length > 0 && ai == "AI: " + fight ? string.Empty : fight; // "AI: attacking X" says it already
            GUI.Label(new Rect(x, y, at.width, 18),
                $"<color={side}><b>{squad.Name}</b></color>  {squad.Formation.Id} · {HudText.Stance(squad.Stance)} · {stats.InFight}/{total} · {HudText.Status(squad)}"
                + (fight.Length > 0 ? " · <color=#ffb080>" + fight + "</color>" : string.Empty)
                + (ai.Length > 0 ? " · <color=#c8a8ff>" + ai + "</color>" : string.Empty)
                + (effects.Length > 0 ? " · <color=#e8c35a>" + effects + "</color>" : string.Empty), _small);
            y += 15;
            foreach (var (sample, count, inFight) in composition)
            {
                GUI.Label(new Rect(x, y, at.width, 18), "<color=#d8d8d8>" + HudText.Group(sample, count, inFight) + "</color>", _small);
                y += 15;
            }

            foreach (Fighter retainer in retainers)
            {
                string condition = Condition(retainer);
                string chain = retainer.SideId == PlayerSide && retainer.IsAlive && !retainer.SquadId.IsNone
                    ? (Chains.IsChained(retainer) ? " · chained" : " · unchained") : string.Empty;
                GUI.Label(new Rect(x, y, at.width, 18), "<color=#e8c35a><b>" + HudText.Retainer(retainer) + "</b></color><color=#d8d8d8> · "
                    + (condition.Length > 0 ? condition + " · " : string.Empty) + HudText.AbilityState(state, retainer) + chain + "</color>", _small);
                y += 15;
            }

            if (composition.Count + retainers.Count == 0)
            {
                y += 15;
            }

            string hurt = $"hurt {stats.Light} light, {stats.Heavy} heavy · fallen {stats.Fallen} · fled {stats.Fled}"
                + (stats.Left > 0 ? $" · left the field {stats.Left}" : string.Empty);
            string blows = stats.Blows > 0 ? $" · blows {stats.Blows}, {stats.Landed} through" : string.Empty;
            GUI.Label(new Rect(x, y, at.width, 18), "<color=#d8d8d8>" + hurt + blows + "</color>", _small);
            y += 17;

            Bar(new Rect(x + 2, y + 4, 140, 8), stats.InFight > 0 ? stats.Morale / 1000f : 0, MoraleColour(rules, stats.Morale));
            GUI.Label(new Rect(x + 150, y - 2, at.width - 150, 18),
                stats.InFight > 0 ? $"morale {stats.Morale} · {HudText.MoraleWord(rules, stats.Morale)} · {BreakLine(rules, squad, stats)}" : "no one left",
                _small);
            y += 16;

            Cells(state, squad, new Vector2(x + 2, y));
            y += 14;

            string? why = Why(state, squad);
            if (why != null)
            {
                float h = _wrap!.CalcHeight(new GUIContent(why), at.width);
                GUI.Label(new Rect(x, y, at.width, h), why, _wrap);
            }
        }

        /// <summary>The Retainers standing alone, two lines each: who and how he is; what he was told or is doing.</summary>
        private void DrawAlone(BattleState state, List<Fighter> alone, Rect at)
        {
            float y = at.y;
            GUI.Label(new Rect(at.x, y, at.width, 18), "<color=#e8c35a><b>Standing alone</b></color>", _small);
            y += 15;
            foreach (Fighter retainer in alone)
            {
                string side = retainer.SideId == 0 ? "#7fb2ff" : "#ff8f7f";
                string condition = Condition(retainer);
                string how = retainer.IsAlive
                    ? $"morale {retainer.Morale} ({HudText.MoraleWord(retainer.MoraleState)})" + (condition.Length > 0 ? " · " + condition : string.Empty)
                    : condition;
                GUI.Label(new Rect(at.x, y, at.width, 18), $"<color={side}><b>{HudText.Retainer(retainer)}</b></color><color=#d8d8d8> · {how}</color>", _small);
                y += 15;
                string doing = !retainer.IsAlive || retainer.IsRouting || retainer.IsEngaged || retainer.Action != FighterAction.Idle
                    ? HudText.Action(state, retainer)
                    : HudText.LoneOrder(state, retainer);
                GUI.Label(new Rect(at.x + 12, y, at.width - 12, 18), "<color=#bdbdbd>" + doing + "</color>", _small);
                y += 15;
            }
        }

        /// <summary>A man's condition in a word or two, empty when he is unhurt and steady in the fight.</summary>
        private static string Condition(Fighter man)
        {
            if (man.Wound == WoundLevel.Down)
            {
                return "fallen";
            }

            if (man.MoraleState == MoraleState.Fled)
            {
                return "fled";
            }

            if (man.MoraleState == MoraleState.Left)
            {
                return "left the field";
            }

            string wound = man.Wound == WoundLevel.Heavy ? "badly hurt" : man.Wound == WoundLevel.Light ? "hurt" : string.Empty;
            string running = man.IsRouting ? "running" : string.Empty;
            return wound.Length > 0 && running.Length > 0 ? wound + ", " + running : wound + running;
        }

        /// <summary>
        /// The selection's Retainers as rows of buttons above the bottom-left panels: his ability (Q — a command for the next
        /// tick; greyed while it can't be used, saying why) and his chain (U): for a selected squad, "Unchain" each Retainer
        /// chained to it; for a Retainer on his own in a squad, "Chain" him back. Returns the top of the rows.
        /// </summary>
        private float DrawActions(BattleState state, BattleRunner battle, float bottom)
        {
            var rows = new List<(Fighter Retainer, bool Chained)>();
            Fighter? selected = SelectedRetainer.IsNone ? null : state.FindFighter(SelectedRetainer);
            if (selected != null && selected.IsAlive && selected.SideId == PlayerSide)
            {
                rows.Add((selected, false));
            }
            else
            {
                foreach (EntityId squad in SelectedSquads)
                {
                    foreach (Fighter retainer in Chains.ChainedTo(state, squad))
                    {
                        if (retainer.SideId == PlayerSide)
                        {
                            rows.Add((retainer, true));
                        }
                    }
                }
            }

            float y = bottom;
            for (int r = rows.Count - 1; r >= 0; r--)
            {
                var (retainer, chained) = rows[r];
                RetainerPart part = retainer.Retainer!.Value;
                y -= 34;
                var ability = new Rect(PanelWidth + 20, y, 330, 30);
                bool enabled = GUI.enabled;
                GUI.enabled = HudText.AbilityReady(state, retainer);
                if (GUI.Button(ability, $"Q · {part.Name}: {HudText.AbilityState(state, retainer)}"))
                {
                    battle.Enqueue(new Knez.Sim.Commands.UseAbilityCommand(PlayerSide, retainer.Id));
                }

                GUI.enabled = enabled;
                _panels.Add(ability);
                Squad? squad = retainer.SquadId.IsNone ? null : state.FindSquad(retainer.SquadId);
                if (squad == null)
                {
                    continue; // alone: nothing to chain to
                }

                var chain = new Rect(ability.xMax + 4, y, 240, 30);
                _panels.Add(chain);
                if (GUI.Button(chain, chained ? $"U · Unchain {part.Name} from {squad.Name}" : $"U · Chain {part.Name} to {squad.Name}"))
                {
                    if (chained)
                    {
                        Chains.Unchain(retainer.Id);
                        RetainerPicked?.Invoke(retainer.Id); // he takes his own orders now: select him
                    }
                    else
                    {
                        Chains.Chain(retainer.Id);
                        SquadPicked?.Invoke(squad.Id); // he follows his squad again: select it
                    }
                }
            }

            return y;
        }

        /// <summary>"2 of 7 running · breaks when 4 run" / "broken · re-forms when 4 are back".</summary>
        private static string BreakLine(MoraleDef rules, Squad squad, SquadStats stats)
        {
            int inFight = stats.InFight;
            if (squad.Broken)
            {
                int back = inFight - stats.Running;
                int needBack = (int)((long)rules.ReformShare * inFight / 1000) + 1;
                return $"{back} of {inFight} back · re-forms at {needBack}";
            }

            int need = (int)((long)rules.BreakShare * inFight / 1000) + 1;
            return $"{stats.Running} of {inFight} running · breaks at {need}";
        }

        /// <summary>One cell per man who started in the squad, in id order, coloured by his morale state; dark when down,
        /// hollow when he fled.</summary>
        private void Cells(BattleState state, Squad squad, Vector2 at)
        {
            float x = at.x;
            foreach (Fighter man in state.Fighters)
            {
                if (_record.Tally.HomeOf(man) != squad.Id)
                {
                    continue;
                }

                var cell = new Rect(x, at.y, 10, 9);
                if (man.MoraleState == MoraleState.Fled || man.MoraleState == MoraleState.Left)
                {
                    Frame(cell, new Color(0.7f, 0.7f, 0.7f, 0.8f));
                }
                else
                {
                    Fill(cell, man.Wound == WoundLevel.Down ? new Color(0.25f, 0.25f, 0.25f, 0.9f) : StateColour(man.MoraleState));
                }

                x += 12;
            }
        }

        /// <summary>The battle log: the newest lines, scrolled to the end unless the player scrolled back.</summary>
        private void DrawLog(Rect panel)
        {
            _panels.Add(panel);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 8, panel.y + 2, panel.width - 16, 18), "<b>Battle log</b>  <color=#9a9a9a>(L hides it)</color>", _small);
            IReadOnlyList<LogLine> lines = _record.Log.Lines;
            int first = Mathf.Max(0, lines.Count - LogShown);
            float width = panel.width - 36;
            var texts = new List<(string Text, float Height)>(lines.Count - first);
            float content = 0;
            for (int i = first; i < lines.Count; i++)
            {
                LogLine line = lines[i];
                string text = $"<color=#9a9a9a>{HudText.Clock(line.Tick),6}</color>  <color={KindColour(line.Kind)}>{line.Text}</color>";
                float h = _wrap!.CalcHeight(new GUIContent(text), width);
                texts.Add((text, h));
                content += h;
            }

            var view = new Rect(panel.x + 4, panel.y + 20, panel.width - 8, panel.height - 24);
            float maxY = Mathf.Max(0, content - view.height);
            if (_followLog)
            {
                _logScroll.y = maxY;
            }

            _logScroll = GUI.BeginScrollView(view, _logScroll, new Rect(0, 0, width, content));
            float y = 0;
            foreach (var (text, h) in texts)
            {
                GUI.Label(new Rect(4, y, width, h), text, _wrap);
                y += h;
            }

            GUI.EndScrollView();
            _followLog = _logScroll.y >= maxY - 2;
        }

        /// <summary>The man under the mouse: his kit, wound, morale and what moved it, and what he is doing, top right.</summary>
        private void DrawHovered(BattleState state)
        {
            Camera? cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector2 mouse = Event.current.mousePosition; // GUI space: y grows downward
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(mouse.x, Screen.height - mouse.y, -cam.transform.position.z));
            Fighter? man = null;
            float best = float.MaxValue;
            foreach (Fighter fighter in state.Fighters)
            {
                float d = Vector2.Distance(fighter.Position.ToVector2(), world);
                float reach = fighter.Radius.ToFloat() + 0.25f;
                if (d <= reach && d < best && (fighter.IsAlive || man == null))
                {
                    best = d;
                    man = fighter;
                }
            }

            if (man == null)
            {
                return;
            }

            Squad? home = state.FindSquad(_record.Tally.HomeOf(man));
            string where = home != null ? home.Name : man.IsRetainer ? "standing alone" : "no squad";
            var lines = new List<string>
            {
                man.Retainer is RetainerPart part
                    ? $"<color=#e8c35a><b>{part.Name}</b></color> #{man.Id.Value} · {part.Trait.Id} · {where} (side {man.SideId})"
                    : $"<b>#{man.Id.Value}</b> · {where} (side {man.SideId})",
                HudText.Kit(man.Kit, man.Experience),
                $"{HudText.Wound(man.Wound)} · morale {man.Morale} ({HudText.MoraleWord(man.MoraleState)})",
                "morale " + HudText.Account(_record.Ledger.Of(man), 4),
                HudText.Action(state, man),
            };
            if (man.IsRetainer)
            {
                lines.Insert(2, HudText.Leadership(man.Experience) + " · " + HudText.AbilityState(state, man)
                    + (home == null && man.IsAlive ? " · " + HudText.LoneOrder(state, man) : string.Empty));
            }

            BlowStruck? by = _record.Tally.LastBlowBy(man);
            if (by != null)
            {
                lines.Add($"his last blow: {HudText.Blow(by)} at {_record.Log.Name(state, by.Defender)}");
            }

            BlowStruck? at = _record.Tally.LastBlowAt(man);
            if (at != null)
            {
                lines.Add($"last blow at him: {HudText.Blow(at)} by {_record.Log.Name(state, at.Attacker)}");
            }

            float width = 440;
            float height = 10 + (lines.Count * 18);
            var box = new Rect(Screen.width - width - 8, 6, width, height);
            GUI.Box(box, GUIContent.none);
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(box.x + 8, box.y + 4 + (i * 18), width - 12, 18), lines[i], _small);
            }
        }

        private static string KindColour(LogKind kind) => kind switch
        {
            LogKind.FirstBlow => "#ffe08a",
            LogKind.Wound => "#e8b0a0",
            LogKind.Fell => "#ff7a6a",
            LogKind.Ran => "#ffb060",
            LogKind.Rallied => "#9fd0ff",
            LogKind.Fled => "#b0b0b0",
            LogKind.Broke => "#ff6a5a",
            LogKind.Reformed => "#8fe0a0",
            LogKind.Refused => "#ff9f7f",
            LogKind.Ability => "#e8c35a",
            LogKind.Ai => "#c8a8ff",
            _ => "#ffe08a",
        };

        /// <summary>A man's morale state as a colour: the same in the squad cells and the bars over men.</summary>
        public static Color StateColour(MoraleState state) => state switch
        {
            MoraleState.Shaken => new Color(0.75f, 0.80f, 0.25f),
            MoraleState.Wavering => new Color(0.95f, 0.65f, 0.15f),
            MoraleState.Routing => new Color(0.90f, 0.20f, 0.20f),
            MoraleState.Rallying => new Color(0.35f, 0.65f, 0.95f),
            MoraleState.Fled => new Color(0.6f, 0.6f, 0.6f),
            MoraleState.Left => new Color(0.6f, 0.6f, 0.6f),
            _ => new Color(0.30f, 0.70f, 0.32f),
        };

        private static Color MoraleColour(MoraleDef rules, int morale) =>
            morale >= rules.Shaken ? new Color(0.30f, 0.70f, 0.32f) : morale >= rules.Wavering ? new Color(0.95f, 0.75f, 0.20f)
            : morale >= rules.Routing ? new Color(0.95f, 0.50f, 0.15f) : new Color(0.85f, 0.20f, 0.20f);

        private static void Bar(Rect rect, float fraction, Color colour)
        {
            Fill(rect, new Color(0, 0, 0, 0.45f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), colour);
        }

        private static void Fill(Rect rect, Color colour)
        {
            Color old = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Frame(Rect rect, Color colour)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, 1), colour);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), colour);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), colour);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), colour);
        }
    }
}
