using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Knez.Sim.Cli;

/// <summary>
/// Headless battle runner (battle-sim.md §17). M0 skeleton: <c>run</c> simulates the empty battle and prints the tick
/// count and final state hash. Scenario files, <c>batch</c>, <c>bench</c> and <c>replay verify</c> arrive in M1.
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
          dotnet run --project sim/tools/Knez.Sim.Cli -c Release -- run [--seed N] [--ticks N] [--json]

        Commands:
          run     Simulate a battle; print the tick count and the final state hash.
                  M0: always the empty battle (no fighters). Scenario files arrive in M1.
          help    Show this text.

        Options for run:
          --seed N    battle seed (default 1)
          --ticks N   ticks to simulate, 20 per second (default 600 = 30 s)
          --json      print one JSON object instead of text

        Exit codes: 0 ok, 1 simulation error, 2 bad arguments.
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
                case "--seed" or "--ticks":
                    return UsageError(stderr, $"{args[i]} needs a positive whole number");
                case var other when !other.StartsWith('-'):
                    return UsageError(stderr, $"scenario files arrive in M1; 'run' takes no scenario yet (got '{other}')");
                default:
                    return UsageError(stderr, $"unknown option '{args[i]}'");
            }
        }

        try
        {
            var sim = new BattleSimulation(new BattleState(seed)); // the empty battle: no fighters
            var clock = Stopwatch.StartNew();
            sim.StepTicks(ticks);
            clock.Stop();
            var hash = sim.ComputeHash();
            double elapsedMs = clock.Elapsed.TotalMilliseconds;

            if (json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new
                {
                    scenario = "empty",
                    seed,
                    ticks = sim.State.Tick,
                    finalHash = Hex(hash.Total),
                    sections = new { core = Hex(hash.Core), rng = Hex(hash.Rng), fighters = Hex(hash.Fighters) },
                    elapsedMs = Math.Round(elapsedMs, 3),
                }));
            }
            else
            {
                var inv = CultureInfo.InvariantCulture;
                stdout.WriteLine("scenario:   empty");
                stdout.WriteLine($"seed:       {seed}");
                stdout.WriteLine(string.Format(inv, "ticks:      {0} ({1:0.0} s of battle)", sim.State.Tick, sim.State.Tick / (double)BattleState.TicksPerSecond));
                stdout.WriteLine($"final hash: {hash}");
                stdout.WriteLine(string.Format(inv, "elapsed:    {0:0.###} ms ({1:0.####} ms/tick)", elapsedMs, elapsedMs / sim.State.Tick));
            }

            return ExitOk;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"simulation error: {ex.GetType().Name}: {ex.Message}");
            return ExitSimulationError;
        }
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"knez-sim: {message}");
        stderr.WriteLine("Run 'help' for usage.");
        return ExitUsage;
    }

    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}
