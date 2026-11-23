#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Units;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.View
{
    /// <summary>
    /// The on-screen panel (M2 step 6, M3), drawn with Unity's immediate-mode GUI so it needs no scene set-up: the clock,
    /// speed and pause and, once decided, the result; every squad with its formation, men in the fight and state, its kit,
    /// how its men are hurt, its blows, and its average morale as a bar; the man under the mouse (top right); refused
    /// orders, fading after a few seconds; and the controls. Reads the state only.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        private const float RefusalSeconds = 4f;

        [SerializeField] private BattleRunner? runner;

        private readonly List<(string Text, float Until)> _refusals = new List<(string, float)>();
        private readonly SquadTally _tally = new SquadTally();
        private bool _begun;
        private string? _result;
        private GUIStyle? _label;
        private GUIStyle? _small;

        private void Start()
        {
            if (runner == null) // Unity's ==: an empty Inspector field is a "fake null" that ?? misses
            {
                runner = GetComponent<BattleRunner>();
            }
            if (runner != null)
            {
                runner.Ticked += OnTicked;
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
            EnsureTally(state);
            _tally.Observe(state);
            foreach (SimEvent simEvent in state.Events)
            {
                if (simEvent is CommandRejected rejected)
                {
                    _refusals.Add((HudText.Refusal(state, rejected.Command, rejected.Reason), Time.unscaledTime + RefusalSeconds));
                }
                else if (simEvent is BattleEnded ended)
                {
                    _result = ended.Winner < 0 ? "Nobody holds the field."
                        : $"Side {ended.Winner} holds the field ({ended.Tick / (float)BattleState.TicksPerSecond:0.0} s).";
                }
            }
        }

        private void EnsureTally(BattleState state)
        {
            if (!_begun)
            {
                _tally.Begin(state);
                _begun = true;
            }
        }

        private void OnGUI()
        {
            BattleSimulation? sim = runner != null ? runner.Simulation : null;
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            if (runner == null || sim == null)
            {
                GUI.Label(new Rect(12, 12, 900, 24), runner != null && runner.Error != null ? runner.Error : "No battle loaded.", _label);
                return;
            }

            BattleState state = sim.State;
            EnsureTally(state);
            const float SquadHeight = 70;
            const float Width = 430;
            float y = 10;
            GUI.Box(new Rect(6, 6, Width, 40 + (state.Squads.Count * SquadHeight) + (_result != null ? 22 : 0)), GUIContent.none);
            string clock = $"<b>{state.Scenario.Id}</b>   tick {state.Tick} ({state.Tick / (float)BattleState.TicksPerSecond:0.0} s)   " +
                (runner.Clock.Paused ? "<color=#ffcc55>PAUSED</color>" : $"×{runner.Clock.Speed:0.##}");
            GUI.Label(new Rect(14, y, Width - 10, 22), clock, _label);
            y += 26;
            if (_result != null)
            {
                GUI.Label(new Rect(14, y - 4, Width - 10, 22), "<color=#ffe08a><b>" + _result + "</b></color>", _small);
                y += 22;
            }

            MoraleDef rules = state.Scenario.Morale;
            foreach (Squad squad in state.Squads)
            {
                int total = Spawned(state.Scenario, squad.Name);
                SquadStats stats = _tally.Of(state, squad);
                string side = squad.SideId == 0 ? "#7fb2ff" : "#ff8f7f";
                GUI.Label(new Rect(14, y, Width - 10, 20),
                    $"<color={side}><b>{squad.Name}</b></color>  {squad.Formation.Id} · {stats.InFight}/{total} · {HudText.Status(squad)}", _small);
                Fighter? any = AnyMan(state, squad);
                if (any != null)
                {
                    GUI.Label(new Rect(14, y + 15, Width - 10, 18), "<color=#d8d8d8>" + HudText.Kit(any.Def.Kit) + "</color>", _small);
                }

                string hurt = $"hurt {stats.Light} light, {stats.Heavy} heavy · fallen {stats.Fallen} · fled {stats.Fled}";
                string blows = stats.Blows > 0 ? $" · blows {stats.Blows}, {stats.Landed} through" : string.Empty;
                GUI.Label(new Rect(14, y + 30, Width - 10, 18), "<color=#d8d8d8>" + hurt + blows + "</color>", _small);
                Bar(new Rect(16, y + 52, 140, 8), stats.InFight > 0 ? stats.Morale / 1000f : 0, MoraleColour(rules, stats.Morale));
                string running = stats.Running > 0 ? $" · {stats.Running} running" : string.Empty;
                GUI.Label(new Rect(162, y + 46, Width - 160, 18),
                    stats.InFight > 0 ? $"morale {stats.Morale} · {HudText.MoraleWord(rules, stats.Morale)}{running}" : "no one left", _small);
                y += SquadHeight;
            }

            DrawHovered(state);

            float now = Time.unscaledTime;
            _refusals.RemoveAll(r => r.Until < now);
            float ry = Screen.height - 60 - (_refusals.Count * 20);
            foreach (var (text, _) in _refusals)
            {
                GUI.Label(new Rect(12, ry, 700, 20), "<color=#ff9f7f>" + text + "</color>", _label);
                ry += 20;
            }

            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 22),
                "<color=#cccccc>Left: select (drag: box, Shift: add, Esc: clear) · Right: move (drag: facing) · 1/2/3: line/shield wall/column · Space: pause · =/−: speed · WASD/middle drag: pan · scroll: zoom · point at a man for details</color>",
                _small);
        }

        /// <summary>The man under the mouse: his kit, wound, morale and what he is doing, top right.</summary>
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

            Squad? home = state.FindSquad(_tally.HomeOf(man));
            var lines = new List<string>
            {
                $"<b>#{man.Id.Value}</b> {man.Def.Id} · {(home != null ? home.Name : "no squad")} (side {man.SideId})",
                HudText.Kit(man.Def.Kit),
                $"{HudText.Wound(man.Wound)} · morale {man.Morale} ({HudText.MoraleWord(man.MoraleState)})",
                HudText.Action(state, man),
            };
            BlowStruck? by = _tally.LastBlowBy(man);
            if (by != null)
            {
                lines.Add($"his last blow: {HudText.Blow(by)} at {HudText.Name(state, by.Defender)}");
            }

            BlowStruck? at = _tally.LastBlowAt(man);
            if (at != null)
            {
                lines.Add($"last blow at him: {HudText.Blow(at)} by {HudText.Name(state, at.Attacker)}");
            }

            float width = 420;
            float height = 10 + (lines.Count * 18);
            var box = new Rect(Screen.width - width - 8, 6, width, height);
            GUI.Box(box, GUIContent.none);
            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(box.x + 8, box.y + 4 + (i * 18), width - 12, 18), lines[i], _small);
            }
        }

        private static Fighter? AnyMan(BattleState state, Squad squad)
        {
            foreach (EntityId id in squad.Members)
            {
                Fighter? fighter = state.FindFighter(id);
                if (fighter != null)
                {
                    return fighter;
                }
            }

            return null;
        }

        private static Color MoraleColour(MoraleDef rules, int morale) =>
            morale >= rules.Shaken ? new Color(0.30f, 0.70f, 0.32f) : morale >= rules.Wavering ? new Color(0.95f, 0.75f, 0.20f)
            : morale >= rules.Routing ? new Color(0.95f, 0.50f, 0.15f) : new Color(0.85f, 0.20f, 0.20f);

        private static void Bar(Rect rect, float fraction, Color colour)
        {
            Color old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = colour;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static int Spawned(ScenarioDef scenario, string name)
        {
            foreach (SquadSpawnDef spawn in scenario.Squads)
            {
                if (spawn.Name == name)
                {
                    return spawn.Count;
                }
            }

            return 0;
        }
    }
}
