#nullable enable
using System;
using System.IO;
using Knez.Sim;
using Knez.Sim.Content;
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// Runs one battle in the scene (battle-sim.md §3, M2 step 2): loads the scenario, then each frame steps the
    /// simulation as many ticks as <see cref="SimClock"/> says. It owns the <see cref="BattleSimulation"/>; drawing and
    /// input read it and enqueue commands — nothing else writes the state. Pausing means simply not stepping.
    /// </summary>
    public sealed class BattleRunner : MonoBehaviour
    {
        [Tooltip("Scenario file, relative to the content folder (the repo's content/ in the editor).")]
        [SerializeField] private string scenario = "scenarios/drill.json";

        [Tooltip("Battle seed (positive).")]
        [SerializeField] private int seed = 1;

        [Tooltip("Game seconds per real second.")]
        [SerializeField, Range(0.25f, 4f)] private float speed = 1f;

        [SerializeField] private bool startPaused;

        /// <summary>The running battle, or null when it failed to load.</summary>
        public BattleSimulation? Simulation { get; private set; }

        /// <summary>Real time → ticks: pause, speed, interpolation alpha.</summary>
        public SimClock Clock { get; } = new SimClock();

        /// <summary>Why the battle didn't load or stopped, or null.</summary>
        public string? Error { get; private set; }

        /// <summary>Raised after every tick stepped, with the state as it now stands (the view keeps the tick before).</summary>
        public event Action<BattleState>? Ticked;

        /// <summary>Steps <paramref name="ticks"/> ticks, raising <see cref="Ticked"/> after each.</summary>
        public void Step(int ticks)
        {
            for (int i = 0; i < ticks && Simulation != null; i++)
            {
                Simulation.Step();
                Ticked?.Invoke(Simulation.State);
            }
        }

        /// <summary>Pauses or resumes the battle.</summary>
        public void TogglePause() => Clock.Paused = !Clock.Paused;

        /// <summary>Starts <paramref name="scenarioDef"/> from code (tests, menus) instead of the Inspector's file.</summary>
        public void Load(Knez.Sim.Defs.ScenarioDef scenarioDef, ulong battleSeed)
        {
            Simulation = new BattleSimulation(BattleSetup.Create(scenarioDef, battleSeed));
            Error = null;
        }

        private void Awake()
        {
            Clock.Speed = speed;
            Clock.Paused = startPaused;
            try
            {
                var def = ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), scenario);
                Simulation = new BattleSimulation(BattleSetup.Create(def, (ulong)Math.Max(1, seed)));
                Debug.Log($"Knez: loaded {def.Id} — {def.Squads.Count} squads, seed {seed}.");
            }
            catch (Exception ex) when (ex is ContentException or IOException or ArgumentException)
            {
                Stop($"Knez: can't load {scenario}: {ex.Message}");
            }
        }

        private void Update()
        {
            if (Simulation == null)
            {
                return;
            }

            int ticks = Clock.Advance(Time.unscaledDeltaTime);
            try
            {
                Step(ticks);
            }
            catch (Exception ex)
            {
                // The sim's state is half-updated after a throw (BattleSimulation.IsFaulted): stop, don't limp on.
                Stop($"Knez: simulation error in tick {Simulation.State.Tick}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnValidate() => Clock.Speed = speed;

        private void Stop(string message)
        {
            Error = message;
            Debug.LogError(message);
            enabled = false;
        }
    }
}
