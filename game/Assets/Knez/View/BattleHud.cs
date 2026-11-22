#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Units;
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// The on-screen panel (M2 step 6), drawn with Unity's immediate-mode GUI so it needs no scene set-up: the clock,
    /// speed and pause; every squad with its formation, state and men left, and a morale bar that is a placeholder until
    /// morale arrives (M3); refused orders, fading after a few seconds; and the controls. Reads the state only.
    /// </summary>
    public sealed class BattleHud : MonoBehaviour
    {
        private const float RefusalSeconds = 4f;

        [SerializeField] private BattleRunner? runner;

        private readonly List<(string Text, float Until)> _refusals = new List<(string, float)>();
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
            foreach (SimEvent simEvent in state.Events)
            {
                if (simEvent is CommandRejected rejected)
                {
                    _refusals.Add((HudText.Refusal(state, rejected.Command, rejected.Reason), Time.unscaledTime + RefusalSeconds));
                }
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
            float y = 10;
            GUI.Box(new Rect(6, 6, 330, 40 + (state.Squads.Count * 38)), GUIContent.none);
            string clock = $"<b>{state.Scenario.Id}</b>   tick {state.Tick} ({state.Tick / (float)BattleState.TicksPerSecond:0.0} s)   " +
                (runner.Clock.Paused ? "<color=#ffcc55>PAUSED</color>" : $"×{runner.Clock.Speed:0.##}");
            GUI.Label(new Rect(14, y, 320, 22), clock, _label);
            y += 26;
            foreach (Squad squad in state.Squads)
            {
                int total = Spawned(state.Scenario, squad.Name);
                string side = squad.SideId == 0 ? "#7fb2ff" : "#ff8f7f";
                GUI.Label(new Rect(14, y, 320, 20),
                    $"<color={side}><b>{squad.Name}</b></color>  {squad.Formation.Id} · {squad.Members.Count}/{total} · {HudText.Status(squad)}", _small);
                // Morale bar: a placeholder until morale exists (M3) — full and grey.
                GUI.Box(new Rect(16, y + 20, 140, 8), GUIContent.none);
                GUI.Label(new Rect(162, y + 15, 160, 18), "<color=#999999>morale (M3)</color>", _small);
                y += 38;
            }

            float now = Time.unscaledTime;
            _refusals.RemoveAll(r => r.Until < now);
            float ry = Screen.height - 60 - (_refusals.Count * 20);
            foreach (var (text, _) in _refusals)
            {
                GUI.Label(new Rect(12, ry, 700, 20), "<color=#ff9f7f>" + text + "</color>", _label);
                ry += 20;
            }

            GUI.Label(new Rect(12, Screen.height - 30, Screen.width - 24, 22),
                "<color=#cccccc>Left: select (drag: box, Shift: add, Esc: clear) · Right: move (drag: facing) · 1/2/3: line/shield wall/column · Space: pause · =/−: speed · WASD/middle drag: pan · scroll: zoom</color>",
                _small);
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
