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

        /// <summary>The orders given so far, written out as a replayable scenario when the battle object goes away.</summary>
        public SessionRecorder Recorder { get; private set; } = new SessionRecorder();

        /// <summary>Where the last session was written, or null.</summary>
        public string? SessionFile { get; private set; }

        private SourceText? _scenarioText;
        private int _battleSeed;
        private string? _fault;

        /// <summary>Raised after every tick stepped, with the state as it now stands (the view keeps the tick before).</summary>
        public event Action<BattleState>? Ticked;

        /// <summary>Steps <paramref name="ticks"/> ticks, raising <see cref="Ticked"/> after each.</summary>
        public void Step(int ticks)
        {
            for (int i = 0; i < ticks && Simulation != null; i++)
            {
                Simulation.Step();
                Recorder.Stepped(Simulation.State);
                Ticked?.Invoke(Simulation.State);
            }
        }

        /// <summary>Gives <paramref name="command"/> to the battle for the next tick and records it for the session file.</summary>
        public void Enqueue(Knez.Sim.Commands.ICommand command)
        {
            if (Simulation == null)
            {
                return;
            }

            Simulation.Enqueue(command);
            Recorder.Enqueued(Simulation.State, command);
        }

        /// <summary>Pauses or resumes the battle.</summary>
        public void TogglePause() => Clock.Paused = !Clock.Paused;

        /// <summary>Starts <paramref name="scenarioDef"/> from code (tests, menus) instead of the Inspector's file.</summary>
        public void Load(Knez.Sim.Defs.ScenarioDef scenarioDef, ulong battleSeed)
        {
            Simulation = new BattleSimulation(BattleSetup.Create(scenarioDef, battleSeed));
            Error = null;
            Recorder = new SessionRecorder();
            _scenarioText = null; // no file to write the orders into
        }

        private void Awake()
        {
            Clock.Speed = speed;
            Clock.Paused = startPaused;
            try
            {
                string root = ContentFiles.DefaultRoot();
                var def = ContentFiles.LoadScenario(root, scenario);
                _scenarioText = new SourceText(scenario, File.ReadAllText(Path.Combine(root, scenario)));
                _battleSeed = Math.Max(1, seed);
                Simulation = new BattleSimulation(BattleSetup.Create(def, (ulong)_battleSeed));
                Debug.Log($"Knez: loaded {def.Id} — {def.Squads.Count} squads, seed {_battleSeed}.");
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
                // Keep the faulted tick's orders (applied before the throw) so the session reproduces the fault.
                Recorder.Stepped(Simulation.State);
                _fault = $"faulted in tick {Simulation.State.Tick} ({ex.GetType().Name})";
                Stop($"Knez: simulation error in tick {Simulation.State.Tick}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void OnValidate() => Clock.Speed = speed;

        // Leaving Play mode or quitting destroys the runner: that's when the session is written.
        private void OnDestroy() => WriteSession();

        /// <summary>
        /// Writes the scenario with every applied order in its script to <c>artifacts/sessions/</c> (next to the repo's
        /// content in the editor, persistentDataPath/sessions in a build), so the battle replays headless with the CLI.
        /// Nothing is written when no order was given.
        /// </summary>
        public void WriteSession()
        {
            if (_scenarioText == null || Simulation == null || Recorder.Orders.Count == 0 || SessionFile != null)
            {
                return;
            }

            SourceText source = _scenarioText.Value;
            try
            {
                string id = Path.GetFileNameWithoutExtension(source.Name);
                int battleSeed = _battleSeed; // as Awake started it, whatever the Inspector says now
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                string folder = Application.isEditor
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "sessions"))
                    : Path.Combine(Application.persistentDataPath, "sessions");
                string file = Path.Combine(folder, $"{id}-seed{battleSeed}-{stamp}.json");
                string description = $"Played in Unity on {stamp} with seed {battleSeed} until tick {Simulation.State.Tick}{(_fault != null ? ", where it " + _fault : string.Empty)}; "
                    + $"{Recorder.Orders.Count} orders in the script. Replay it with --seed {battleSeed}. Scenario: {source.Name}.";
                string text = Recorder.Write(source, $"{id}-session", description);
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, text);
                SessionFile = file;
                Debug.Log($"Knez: session written to {file} — replay: dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- "
                    + $"run {file} --seed {battleSeed} --ticks {Math.Max(1, Simulation.State.Tick)} --frames artifacts/replays/{id}-session.frames.json");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ContentException)
            {
                Debug.LogWarning($"Knez: couldn't write the session: {ex.Message}");
            }
        }

        private void Stop(string message)
        {
            Error = message;
            Debug.LogError(message);
            enabled = false;
        }
    }
}
