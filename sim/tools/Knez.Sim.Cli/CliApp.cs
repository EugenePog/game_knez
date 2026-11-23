using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Knez.Sim.Content;
using Knez.Sim.Defs;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;

namespace Knez.Sim.Cli;

/// <summary>
/// Headless battle runner (battle-sim.md §17). <c>run</c> loads a scenario from <c>content/</c>, simulates it,
/// prints the tick count, the M1 movement metrics and the final state hash, and with <c>--frames</c> writes a file for
/// the replay viewer. <c>batch</c> runs a scenario over a range of seeds and aggregates the metrics; <c>bench</c> times
/// ticks (p50 / p99 / max). <c>replay verify</c> arrives with replay files.
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
          ... -- batch <scenario.json> [--seeds A-B] [--ticks N] [--json]
          ... -- bench <scenario.json> [--runs N] [--ticks N] [--json]

        Commands:
          run     Simulate a scenario; print its squads, the tick count, the movement metrics (overlap, stuck,
                  slot error) and the final state hash.
                  Defs come from the 'defs' folder above the scenario (content/defs/*.json).
          batch   Run a scenario once per seed (default 1-20) and print the metrics over all runs.
          bench   Time every tick over several runs (default 5, after a 1 s warm-up): p50, p99, max.
          help    Show this text.

        Options for run:
          --seed N         battle seed (default 1)
          --ticks N        ticks to simulate, 20 per second (default 600 = 30 s)
          --frames FILE    also write every tick to FILE for tools/replay-viewer/ (folders are created);
                           put it under artifacts/replays/, which git ignores
          --json           print one JSON object instead of text
          --seeds A-B      (batch) the seeds to run, inclusive
          --runs N         (bench) timed runs after the warm-up

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
            "batch" => BatchCommand(args[1..], stdout, stderr),
            "bench" => BenchCommand(args[1..], stdout, stderr),
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
            var meter = new MovementMeter();
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

                    meter.Observe(sim.State); // the M1 movement criteria (battle-sim.md §10)
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
            var m = Metrics.Of(meter);

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
                    overlap = new { ratio = Math.Round(m.Overlap, 6), worstTickFighters = meter.WorstOverlap, passingRatio = Math.Round(m.Passing, 6), inWallsRatio = Math.Round(m.InWalls, 6) },
                    stuck = new { fighters = meter.FightersStuck, longestTicks = meter.LongestStuck, fighterTicks = meter.StuckFighterTicks },
                    engaged = new { ratio = Math.Round(m.Engaged, 6), openOrders = OpenOrders(state) },
                    longestFarFromSlotTicks = meter.LongestFarFromSlot,
                    slotError = new { mean = Math.Round(m.MeanSlotError, 4), worst = Math.Round(m.WorstSlotError, 4) },
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
                stdout.WriteLine(string.Format(inv, "crowding:   {0:0.00}% of fighter-ticks overlap > 5 cm (worst tick: {1} fighters); {2:0.00}% passing through allied ranks; {3:0.00}% inside walls > 5 cm", m.Overlap * 100, meter.WorstOverlap, m.Passing * 100, m.InWalls * 100));
                stdout.WriteLine(string.Format(inv, "stuck:      {0} fighters stuck > 2 s (longest {1:0.0} s, {2} fighter-ticks past 2 s)", meter.FightersStuck, meter.LongestStuck / (double)BattleState.TicksPerSecond, meter.StuckFighterTicks));
                stdout.WriteLine(string.Format(inv, "engaged:    {0:0.00}% of squad members' ticks in a squad in melee (not counted as stuck); {1} orders still open", m.Engaged * 100, OpenOrders(state)));
                stdout.WriteLine(string.Format(inv, "slot error: mean {0:0.000} m, worst {1:0.00} m; longest spell > 1 m from a slot {2:0.0} s", m.MeanSlotError, m.WorstSlotError, meter.LongestFarFromSlot / (double)BattleState.TicksPerSecond));
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

    /// <summary>The meter's integer sums as ratios and metres, for printing.</summary>
    private readonly record struct Metrics(double Overlap, double Passing, double InWalls, double MeanSlotError, double WorstSlotError,
        double Engaged)
    {
        public static Metrics Of(MovementMeter meter)
        {
            double perTick = meter.FighterTicks == 0 ? 0 : 1.0 / meter.FighterTicks;
            return new Metrics(
                meter.OverlappingTicks * perTick,
                meter.PassingTicks * perTick,
                meter.InWallTicks * perTick,
                meter.MemberTicks == 0 ? 0 : meter.SlotErrorSumRaw / (double)Fix.OneRaw / meter.MemberTicks,
                meter.WorstSlotError.Raw / (double)Fix.OneRaw,
                meter.MemberTicks == 0 ? 0 : meter.EngagedTicks / (double)meter.MemberTicks);
        }
    }

    /// <summary>Squads whose move order hasn't ended: engaged squads keep theirs until combat (M3); any other is a jam.</summary>
    private static int OpenOrders(BattleState state) => state.Squads.Count(s => s.Order.Kind == Orders.SquadOrderKind.Move);

    /// <summary>Common options of batch and bench: a scenario file plus named numeric options.</summary>
    private static bool ParseScenarioArgs(string command, string[] args, TextWriter stderr, out string scenarioPath, out int ticks,
        out bool json, out (ulong From, ulong To) seeds, out int runs, out int exit)
    {
        scenarioPath = string.Empty;
        ticks = DefaultTicks;
        json = false;
        seeds = (1, 20);
        runs = 5;
        exit = ExitOk;
        string? path = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ticks" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) && ticks > 0:
                    i++;
                    break;
                case "--runs" when command == "bench" && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out runs) && runs > 0:
                    i++;
                    break;
                case "--seeds" when command == "batch" && i + 1 < args.Length && TryParseRange(args[i + 1], out seeds):
                    i++;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--runs" when command != "bench":
                case "--seeds" when command != "batch":
                    exit = UsageError(stderr, $"unknown option '{args[i]}' for '{command}'");
                    return false;
                case "--ticks" or "--runs" or "--seeds":
                    exit = UsageError(stderr, $"{args[i]} needs {(args[i] == "--seeds" ? "a seed or a range like 1-200 (at most 100 000 seeds)" : "a positive whole number")}");
                    return false;
                default:
                    if (args[i].StartsWith('-') || path != null)
                    {
                        exit = UsageError(stderr, path != null ? $"'{command}' takes one scenario file" : $"unknown option '{args[i]}'");
                        return false;
                    }

                    path = args[i];
                    break;
            }
        }

        if (path == null)
        {
            exit = UsageError(stderr, $"'{command}' needs a scenario file, e.g. content/scenarios/bench/s1-clash.json");
            return false;
        }

        scenarioPath = path;
        return true;
    }

    private static bool TryParseRange(string text, out (ulong From, ulong To) range)
    {
        range = (0, 0);
        string[] parts = text.Split('-');
        if (parts.Length == 1 && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong one))
        {
            range = (one, one);
            return true;
        }

        if (parts.Length == 2 && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong from)
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong to) && from <= to && to - from < 100000)
        {
            range = (from, to);
            return true;
        }

        return false;
    }

    private static bool TryLoad(string path, TextWriter stderr, out ScenarioDef scenario)
    {
        try
        {
            scenario = ScenarioFiles.Load(path);
            return true;
        }
        catch (Exception ex) when (ex is ContentException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            stderr.WriteLine($"knez-sim: {ex.Message}");
            scenario = null!;
            return false;
        }
    }

    /// <summary>
    /// batch: the scenario once per seed; per-run metrics summed (stuck, open orders: the worst run). Movement draws no
    /// random numbers yet, so every seed gives the same battle until combat (M3) — "distinct outcomes" shows it (it
    /// compares squads and fighters, not the seed, which the core hash section carries).
    /// </summary>
    private static int BatchCommand(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!ParseScenarioArgs("batch", args, stderr, out string path, out int ticks, out bool json, out var seeds, out _, out int exit))
        {
            return exit;
        }

        if (!TryLoad(path, stderr, out ScenarioDef scenario))
        {
            return ExitUsage;
        }

        long runs = 0;
        int openOrders = 0;
        double engagedSum = 0;
        int longestFar = 0;
        int worstStuckFighters = 0;
        int longestStuck = 0;
        long stuckTicks = 0;
        double overlapSum = 0;
        double worstOverlap = 0;
        double slotErrorSum = 0;
        var outcomes = new HashSet<(ulong, ulong)>(); // what moved: the seed itself sits in the core section
        try
        {
            for (ulong seed = seeds.From; seed <= seeds.To; seed++)
            {
                var sim = new BattleSimulation(BattleSetup.Create(scenario, seed));
                var meter = new MovementMeter();
                for (int i = 0; i < ticks; i++)
                {
                    sim.Step();
                    meter.Observe(sim.State);
                }

                var m = Metrics.Of(meter);
                runs++;
                overlapSum += m.Overlap;
                worstOverlap = Math.Max(worstOverlap, m.Overlap);
                slotErrorSum += m.MeanSlotError;
                worstStuckFighters = Math.Max(worstStuckFighters, meter.FightersStuck);
                longestStuck = Math.Max(longestStuck, meter.LongestStuck);
                stuckTicks += meter.StuckFighterTicks;
                engagedSum += m.Engaged;
                longestFar = Math.Max(longestFar, meter.LongestFarFromSlot);
                openOrders = Math.Max(openOrders, OpenOrders(sim.State));
                var hash = sim.ComputeHash();
                outcomes.Add((hash.Squads, hash.Fighters));
                if (seed == ulong.MaxValue)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"simulation error: {ex.GetType().Name}: {ex.Message}");
            return ExitSimulationError;
        }

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = scenario.Id,
                seeds = $"{seeds.From}-{seeds.To}",
                runs,
                ticks,
                overlap = new { mean = Math.Round(overlapSum / runs, 6), worst = Math.Round(worstOverlap, 6) },
                stuck = new { worstRunFighters = worstStuckFighters, longestTicks = longestStuck, fighterTicks = stuckTicks },
                engaged = new { meanRatio = Math.Round(engagedSum / runs, 6), worstRunOpenOrders = openOrders },
                meanSlotError = Math.Round(slotErrorSum / runs, 4),
                longestFarFromSlotTicks = longestFar,
                distinctOutcomes = outcomes.Count,
            }));
        }
        else
        {
            var inv = CultureInfo.InvariantCulture;
            stdout.WriteLine($"scenario:   {scenario.Id} ({path}), seeds {seeds.From}-{seeds.To} ({runs} runs × {ticks} ticks)");
            stdout.WriteLine(string.Format(inv, "overlap:    mean {0:0.00}%, worst run {1:0.00}%", overlapSum / runs * 100, worstOverlap * 100));
            stdout.WriteLine(string.Format(inv, "stuck:      worst run {0} fighters stuck > 2 s (longest {1:0.0} s); {2} fighter-ticks past 2 s in all", worstStuckFighters, longestStuck / (double)BattleState.TicksPerSecond, stuckTicks));
            stdout.WriteLine(string.Format(inv, "engaged:    mean {0:0.00}% of members' ticks; worst run {1} orders still open", engagedSum / runs * 100, openOrders));
            stdout.WriteLine(string.Format(inv, "slot error: mean {0:0.000} m; longest spell > 1 m from a slot {1:0.0} s", slotErrorSum / runs, longestFar / (double)BattleState.TicksPerSecond));
            stdout.WriteLine($"outcomes:   {outcomes.Count} distinct (squads and fighters as they ended; the seed itself is left out)");
        }

        return ExitOk;
    }

    /// <summary>
    /// bench: warm-up runs for at least a second (so .NET's tiered JIT has optimised the hot code — one short run isn't
    /// enough and inflated p99 about 9×), then <c>--runs</c> timed runs; every tick is timed on its own. Prints p50, p99,
    /// max and mean over all timed ticks, and the fighter count (the M1 bar: p99 under 2 ms for 50 fighters).
    /// </summary>
    private static int BenchCommand(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (!ParseScenarioArgs("bench", args, stderr, out string path, out int ticks, out bool json, out _, out int runs, out int exit))
        {
            return exit;
        }

        if (!TryLoad(path, stderr, out ScenarioDef scenario))
        {
            return ExitUsage;
        }

        var times = new List<double>((int)Math.Min((long)ticks * runs, 1 << 20));
        int fighters = 0;
        try
        {
            var warm = Stopwatch.StartNew();
            do
            {
                var sim = new BattleSimulation(BattleSetup.Create(scenario, DefaultSeed));
                sim.StepTicks(ticks);
            }
            while (warm.ElapsedMilliseconds < 1000);

            for (int run = 0; run < runs; run++)
            {
                var sim = new BattleSimulation(BattleSetup.Create(scenario, DefaultSeed));
                fighters = sim.State.Fighters.Count;
                for (int i = 0; i < ticks; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    sim.Step();
                    long end = Stopwatch.GetTimestamp();
                    times.Add((end - start) * 1000.0 / Stopwatch.Frequency);
                }
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"simulation error: {ex.GetType().Name}: {ex.Message}");
            return ExitSimulationError;
        }

        times.Sort();
        double P(double q) => times[Math.Min(times.Count - 1, (int)Math.Ceiling(q * times.Count) - 1)];
        double p50 = P(0.50), p99 = P(0.99), max = times[^1], mean = times.Average();
        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = scenario.Id,
                fighters,
                runs,
                ticks,
                tickMs = new { p50 = Math.Round(p50, 4), p99 = Math.Round(p99, 4), max = Math.Round(max, 4), mean = Math.Round(mean, 4) },
            }));
        }
        else
        {
            var inv = CultureInfo.InvariantCulture;
            stdout.WriteLine($"scenario:   {scenario.Id} ({path}), {fighters} fighters, {runs} timed runs × {ticks} ticks after a 1 s warm-up");
            stdout.WriteLine(string.Format(inv, "tick time:  p50 {0:0.000} ms, p99 {1:0.000} ms, max {2:0.000} ms, mean {3:0.000} ms", p50, p99, max, mean));
        }

        return ExitOk;
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
