using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Knez.Sim.Content;
using Knez.Sim.Defs;

namespace Knez.Sim.Cli;

/// <summary>
/// Headless battle runner (battle-sim.md §17). <c>run</c> loads a scenario from <c>content/</c>, simulates it,
/// prints the tick count and final state hash, and with <c>--frames</c> writes a file for the replay viewer.
/// <c>batch</c>, <c>bench</c> and <c>replay verify</c> arrive in later M1 steps.
/// </summary>
public static class CliApp
{
    public const int ExitOk = 0;
    public const int ExitSimulationError = 1;
    public const int ExitUsage = 2;

    public const ulong DefaultSeed = 1;
    public const int DefaultTicks = 30 * BattleState.TicksPerSecond;

    public const string Usage = """
        knez-sim - headless Game_knez battle runner

        Usage:
          dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run <scenario.json> [--seed N] [--ticks N]
              [--frames FILE] [--json]

        Commands:
          run     Simulate a scenario; print its squads, the tick count and the final state hash.
                  Defs come from the 'defs' folder above the scenario (content/defs/*.json).
          help    Show this text.

        Options for run:
          --seed N         battle seed (default 1)
          --ticks N        ticks to simulate, 20 per second (default 600 = 30 s)
          --frames FILE    also write every tick to FILE for tools/replay-viewer/ (folders are created);
                           put it under artifacts/replays/, which git ignores
          --json           print one JSON object instead of text

        Example:
          ... -- run content/scenarios/formations.json --seed 42 --frames artifacts/replays/formations-s42.frames.json

        Exit codes: 0 ok, 1 simulation error (a frames file is still closed as valid JSON, marked "faulted"),
                    2 bad arguments, bad content, or the frames file can't be written.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        return args[0] switch
        {
            "run" => RunCommand(args[1..], stdout, stderr),
            _ => UsageError(stderr, $"unknown command '{args[0]}'"),
        };
    }

    private static int RunCommand(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ulong seed = DefaultSeed;
        int ticks = DefaultTicks;
        bool json = false;
        string? scenarioPath = null;
        string? framesPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out seed):
                    i++;
                    break;
                case "--ticks" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) && ticks > 0:
                    i++;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--frames" when i + 1 < args.Length && !args[i + 1].StartsWith('-') && args[i + 1].Length > 0:
                    framesPath = args[++i];
                    break;
                case "--frames":
                    return UsageError(stderr, "--frames needs a file path");
                case "--seed" or "--ticks":
                    return UsageError(stderr, $"{args[i]} needs a positive whole number");
                case var other when !other.StartsWith('-'):
                    if (scenarioPath != null)
                    {
                        return UsageError(stderr, $"'run' takes one scenario file (got '{scenarioPath}' and '{other}')");
                    }

                    scenarioPath = other;
                    break;
                default:
                    return UsageError(stderr, $"unknown option '{args[i]}'");
            }
        }

        if (string.IsNullOrWhiteSpace(scenarioPath))
        {
            return UsageError(stderr, "'run' needs a scenario file, e.g. content/scenarios/formations.json");
        }

        ScenarioDef scenario;
        try
        {
            scenario = ScenarioFiles.Load(scenarioPath);
        }
        catch (Exception ex) when (ex is ContentException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            stderr.WriteLine($"knez-sim: {ex.Message}");
            return ExitUsage;
        }

        // Open the frames file before simulating, so a bad path fails fast and as a usage error.
        Stream? framesStream = null;
        if (framesPath != null)
        {
            try
            {
                string full = Path.GetFullPath(framesPath);
                if (string.Equals(full, Path.GetFullPath(scenarioPath), StringComparison.Ordinal))
                {
                    return UsageError(stderr, "--frames would overwrite the scenario file; write frames under artifacts/replays/");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                framesStream = new FileStream(full, FileMode.Create, FileAccess.Write);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                stderr.WriteLine($"knez-sim: cannot write frames to {framesPath}: {ex.Message}");
                return ExitUsage;
            }
        }

        using (framesStream)
        {
            BattleSimulation? sim = null;
            FrameRecorder? recorder = null;
            var clock = new Stopwatch(); // only stepping is timed; writing frames is not part of the sim's cost
            try
            {
                sim = new BattleSimulation(BattleSetup.Create(scenario, seed));
                recorder = framesStream == null ? null : new FrameRecorder(framesStream, sim.State);
                recorder?.Capture(sim.State);
                for (int i = 0; i < ticks; i++)
                {
                    clock.Start();
                    sim.Step();
                    clock.Stop();
                    recorder?.Capture(sim.State);
                }

                recorder?.Complete();
            }
            catch (IOException ex)
            {
                // The sim does no I/O (the purity check enforces it), so this came from writing frames.
                stderr.WriteLine($"knez-sim: writing frames to {framesPath} failed: {ex.Message}");
                return ExitUsage;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"simulation error: {ex.GetType().Name}: {ex.Message}");
                FinishFaultedFrames(recorder, sim, ex, framesPath, stderr);
                return ExitSimulationError;
            }
            finally
            {
                recorder?.Dispose();
            }

            var hash = sim.ComputeHash();
            double elapsedMs = clock.Elapsed.TotalMilliseconds;
            var state = sim.State;

            if (json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new
                {
                    scenario = scenario.Id,
                    scenarioFile = scenarioPath,
                    seed,
                    ticks = state.Tick,
                    squads = state.Squads.Count,
                    fighters = state.Fighters.Count,
                    finalHash = Hex(hash.Total),
                    sections = new { core = Hex(hash.Core), rng = Hex(hash.Rng), squads = Hex(hash.Squads), fighters = Hex(hash.Fighters) },
                    frames = recorder == null ? null : new { file = framesPath, count = recorder.FrameCount },
                    elapsedMs = Math.Round(elapsedMs, 3),
                }));
            }
            else
            {
                var inv = CultureInfo.InvariantCulture;
                stdout.WriteLine($"scenario:   {scenario.Id} ({scenarioPath})");
                stdout.WriteLine($"forces:     {Forces(state)}");
                stdout.WriteLine($"seed:       {seed}");
                stdout.WriteLine(string.Format(inv, "ticks:      {0} ({1:0.0} s of battle)", state.Tick, state.Tick / (double)BattleState.TicksPerSecond));
                stdout.WriteLine($"final hash: {hash}");
                stdout.WriteLine(string.Format(inv, "elapsed:    {0:0.###} ms ({1:0.####} ms/tick)", elapsedMs, elapsedMs / state.Tick));
                if (recorder != null)
                {
                    stdout.WriteLine($"frames:     {framesPath} ({recorder.FrameCount} frames)");
                }
            }

            return ExitOk;
        }
    }

    /// <summary>
    /// After the simulation threw, close the frames file as valid JSON with a "faulted" note: the ticks just before a
    /// crash are the ones worth watching. The writer is between frames unless Capture itself threw.
    /// </summary>
    private static void FinishFaultedFrames(FrameRecorder? recorder, BattleSimulation? sim, Exception ex, string? framesPath,
        TextWriter stderr)
    {
        if (recorder == null || sim == null)
        {
            return;
        }

        int tick = sim.State.Tick;
        try
        {
            recorder.CompleteFaulted(tick, $"{ex.GetType().Name}: {ex.Message}");
            stderr.WriteLine($"frames:     {framesPath} ({recorder.FrameCount} frames, faulted in tick {tick})");
        }
        catch (Exception writeError) when (writeError is IOException or InvalidOperationException)
        {
            stderr.WriteLine($"knez-sim: could not finish {framesPath}: {writeError.Message}");
        }
    }

    /// <summary>"side 0: 2 squads, 16 fighters; side 1: 3 squads, 19 fighters".</summary>
    private static string Forces(BattleState state)
    {
        var sides = state.Squads.Select(s => s.SideId).Concat(state.Fighters.Select(f => f.SideId)).Distinct().Order();
        return string.Join("; ", sides.Select(side =>
            $"side {side}: {state.Squads.Count(s => s.SideId == side)} squads, {state.Fighters.Count(f => f.SideId == side)} fighters"));
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"knez-sim: {message}");
        stderr.WriteLine("Run 'help' for usage.");
        return ExitUsage;
    }

    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}
