using System.Globalization;
using System.Text;
using System.Text.Json;
using Knez.Sim.Cli;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Combat;

/// <summary>
/// M3 step 7: the balance reference (D-026, battle-sim.md §11). Today's fights over many seeds, each run to its decision
/// or <see cref="LimitTicks"/>, are pinned in <c>sim/tests/Golden/balance-reference.json</c> with tolerances of
/// <see cref="ToleranceSe"/> standard errors (at least a floor per measure). A change that only reshuffles which seed
/// wins stays inside them; a large shift in balance doesn't (how large: <see cref="Scenarios"/>; S6 also has to stay in
/// the D-026 band for equal forces). The content-trace goldens already catch any change at all; this
/// says whether a change that re-recorded them moved the balance. A step that changes balance on purpose (M3 step 10,
/// 11) re-records with <see cref="Record_WritesTheReferenceFile"/> and says why in its commit.
/// </summary>
[TestFixture]
public class BalanceReferenceTests
{
    /// <summary>The time limit of a run: 3 minutes. A fight still undecided then counts as ended by time.</summary>
    public const int LimitTicks = 180 * BattleState.TicksPerSecond;

    /// <summary>A pinned value may move this many of its standard errors (as recorded) before the test fails.</summary>
    public const double ToleranceSe = 5;

    /// <summary>The D-026 band for equal forces: each side wins 45–55 % (battle-sim.md §11).</summary>
    public const double EqualForcesLow = 0.45;

    /// <summary>The top of the D-026 band for equal forces.</summary>
    public const double EqualForcesHigh = 0.55;

    /// <summary>
    /// The reference scenarios, their seeds (1..n) and whether they are equal forces. A 50 % win share has a standard
    /// error of 50/√n points, so the 5-SE gate is ±8 points at 1000 seeds — it catches a lurch, not a nudge. S6, the
    /// equal fight D-026 measures, gets 4000 seeds (±4 points) and must also stay inside the D-026 band. The other small
    /// fights get 1000; the sandbox's 76 men mostly fight to the time limit, so it gets 50 and is a smoke check (its
    /// tolerances are wide). <c>m3-lines</c> is left out: it is the same battle as S6.
    /// </summary>
    public static readonly (string Scenario, int Seeds, bool EqualForces)[] Scenarios =
    [
        ("scenarios/bench/s6-lines.json", 4000, true),
        ("scenarios/bench/s7-flank.json", 1000, false),
        ("scenarios/bench/s8-leader.json", 1000, false),
        ("scenarios/bench/s9-battle-cry.json", 1000, false), // M3 step 11: one per built ability, against S8
        ("scenarios/bench/s10-close-ranks.json", 1000, false),
        ("scenarios/bench/s11-stand-fast.json", 1000, false),
        ("scenarios/m3/m3-flank.json", 1000, false),
        ("scenarios/m3/m3-spears.json", 1000, false),
        ("scenarios/m3/m3-shieldwall.json", 1000, false),
        ("scenarios/m3/m3-withdraw.json", 1000, false),
        ("scenarios/m3/m3-veterans.json", 1000, false),
        ("scenarios/m3/m3-rally.json", 1000, false),
        ("scenarios/sandbox/villages-assault.json", 50, false),
    ];

    /// <summary>Seeds each scenario runs in <see cref="Fight_FirstSeeds_RunToTheirDecision"/>.</summary>
    public const int SmokeSeeds = 4;

    /// <summary>The measures pinned, by their key in the file: how each is read from a summary (value and standard
    /// error) and its least tolerance. A share's error never drops to zero near 0 or 1 (<see cref="OutcomeSummary.RateError"/>),
    /// so its floor is small: no annihilations in 1000 runs allows at most ~9, not 30.</summary>
    private static readonly Measure[] Measures =
    [
        new("side0Wins", s => Rate(s.WinRate(0), s.Runs), 0.005),
        new("side1Wins", s => Rate(s.WinRate(1), s.Runs), 0.005),
        new("undecided", s => Rate(s.EndRate(BattleEnd.Time), s.Runs), 0.005),
        new("annihilations", s => Rate(s.EndRate(BattleEnd.Annihilation), s.Runs), 0.005),
        new("decidedAtSeconds", s => MeanSe(s.EndSeconds), 1.0),
        new("lossAtBreak", s => MeanSe(s.LossAtBreak), 0.02),
        new("fallenSide0", s => MeanSe(s.FallenShares(0)), 0.02),
        new("fallenSide1", s => MeanSe(s.FallenShares(1)), 0.02),
        new("landed", s => (s.LandedRate, s.LandedRateError), 0.005),
    ];

    public static string ReferencePath => Path.Combine(RepoPaths.Root, "sim", "tests", "Golden", "balance-reference.json");

    /// <summary>The reference is checked in Release builds only: Debug is several times slower (the whole reference took
    /// 2½ minutes on 8 cores instead of 15 s). CI runs the suite in both, so Release covers it; locally run
    /// <c>dotnet test sim/Knez.Sim.sln -c Release --filter FullyQualifiedName~BalanceReferenceTests</c>. Debug still runs
    /// the first seeds of each (<see cref="Fight_FirstSeeds_RunToTheirDecision"/>) under its overflow checks.</summary>
    private static readonly bool OptimizedBuild =
#if DEBUG
        false;
#else
        true;
#endif

    private static IEnumerable<TestCaseData> Cases() =>
        Scenarios.Select(s => new TestCaseData(s.Scenario, s.Seeds, s.EqualForces).SetName($"Reference_{s.Scenario}"));

    private static IEnumerable<TestCaseData> SmokeCases() =>
        Scenarios.Select(s => new TestCaseData(s.Scenario).SetName($"Smoke_{s.Scenario}"));

    [TestCaseSource(nameof(Cases))]
    [Category("Scenario")]
    [Category("Slow")]
    public void Fight_MeasuresAsPinned(string scenario, int seeds, bool equalForces)
    {
        if (!OptimizedBuild)
        {
            Assert.Ignore("Release only (several times slower in Debug): dotnet test sim/Knez.Sim.sln -c Release --filter FullyQualifiedName~BalanceReferenceTests");
        }

        var pinned = Load()[scenario];
        Assert.That(pinned.Seeds, Is.EqualTo(seeds), "the file was recorded with another seed count: re-record it");
        var summary = Run(scenario, seeds);
        Assert.Multiple(() =>
        {
            foreach (var measure in Measures)
            {
                double value = measure.Of(summary).Value;
                var (pin, tolerance) = pinned.Values[measure.Key];
                Assert.That(value, Is.EqualTo(pin).Within(tolerance),
                    string.Format(CultureInfo.InvariantCulture, "{0} {1}: measured {2:0.####}, pinned {3} ± {4}", scenario, measure.Key, value, pin, tolerance));
            }

            if (equalForces)
            {
                Assert.That(summary.WinRate(0), Is.InRange(EqualForcesLow, EqualForcesHigh), $"{scenario}: side 0 wins, D-026 equal forces");
                Assert.That(summary.WinRate(1), Is.InRange(EqualForcesLow, EqualForcesHigh), $"{scenario}: side 1 wins, D-026 equal forces");
            }
        });
    }

    [TestCaseSource(nameof(SmokeCases))]
    [Category("Scenario")]
    public void Fight_FirstSeeds_RunToTheirDecision(string scenario)
    {
        // Debug builds check arithmetic for overflow (Knez.Sim.csproj); the content goldens stop at 30 s, so this is
        // what runs the long fights (m3-withdraw, the sandbox's 3 minutes) under those checks. Release just runs them.
        var summary = Run(scenario, SmokeSeeds);
        Assert.That(summary.Runs, Is.EqualTo(SmokeSeeds));
    }

    [Test]
    public void File_PinsEveryReferenceScenarioAndMeasure()
    {
        var pinned = Load();
        Assert.Multiple(() =>
        {
            Assert.That(pinned.Keys, Is.EquivalentTo(Scenarios.Select(s => s.Scenario)));
            foreach (var entry in pinned.Values)
            {
                Assert.That(entry.Values.Keys, Is.EquivalentTo(Measures.Select(m => m.Key)), entry.Scenario);
            }

            foreach (var (scenario, seeds, _) in Scenarios.Where(s => pinned.ContainsKey(s.Scenario)))
            {
                Assert.That(pinned[scenario].Seeds, Is.EqualTo(seeds), $"{scenario}: recorded with another seed count, re-record");
            }
        });
    }

    [Test]
    public void Run_SameSeeds_SameSummary_InParallel()
    {
        // The reference runs seeds in parallel; each battle is its own state, so the order they finish in can't matter.
        var first = Run("scenarios/bench/s6-lines.json", 40);
        var second = Run("scenarios/bench/s6-lines.json", 40);
        Assert.Multiple(() =>
        {
            Assert.That(second.EndSeconds, Is.EqualTo(first.EndSeconds));
            Assert.That(second.LossAtBreak, Is.EqualTo(first.LossAtBreak));
            Assert.That(second.Blows, Is.EqualTo(first.Blows));
        });
    }

    /// <summary>Re-records the reference on purpose (a step that changes balance): measures every scenario and writes
    /// the file. Run it alone, in Release: <c>dotnet test sim/Knez.Sim.sln -c Release --filter
    /// Name=Record_WritesTheReferenceFile</c>.</summary>
    [Test]
    [Explicit("re-records sim/tests/Golden/balance-reference.json; only when balance changes on purpose")]
    public void Record_WritesTheReferenceFile()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("{\n  \"about\": \"Balance reference (D-026, battle-sim.md §11, M3 step 7): each scenario run for seeds 1..n to its decision or ")
            .Append(LimitTicks / BattleState.TicksPerSecond)
            .Append(" s; value = the measure over those runs, tolerance = ")
            .Append(ToleranceSe.ToString(inv))
            .Append(" standard errors or the measure's floor. Shares are 0..1. Written by BalanceReferenceTests.Record_WritesTheReferenceFile; re-record only when balance changes on purpose, and say why in the commit.\",\n")
            .Append("  \"entries\": [\n");
        for (int i = 0; i < Scenarios.Length; i++)
        {
            var (scenario, seeds, _) = Scenarios[i];
            var summary = Run(scenario, seeds);
            sb.Append(inv, $"    {{\n      \"scenario\": \"{scenario}\",\n      \"seeds\": {seeds},\n      \"measures\": {{\n");
            for (int m = 0; m < Measures.Length; m++)
            {
                var (value, se) = Measures[m].Of(summary);
                double tolerance = Math.Max(ToleranceSe * se, Measures[m].Floor);
                sb.Append(inv, $"        \"{Measures[m].Key}\": {{ \"value\": {Math.Round(value, 4)}, \"tolerance\": {Math.Ceiling(tolerance * 10000) / 10000} }}");
                sb.Append(m + 1 < Measures.Length ? ",\n" : "\n");
            }

            sb.Append(i + 1 < Scenarios.Length ? "      }\n    },\n" : "      }\n    }\n");
        }

        sb.Append("  ]\n}\n");
        File.WriteAllText(ReferencePath, sb.ToString());
        TestContext.Out.WriteLine(sb.ToString());
    }

    /// <summary>Runs seeds 1..n of a scenario, each to its decision or the time limit, in parallel; summed in seed order.</summary>
    public static OutcomeSummary Run(string scenario, int seeds)
    {
        var def = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, scenario));
        var runs = new OutcomeMeter[seeds];
        Parallel.For(0, seeds, i =>
        {
            var sim = new BattleSimulation(BattleSetup.Create(def, (ulong)(i + 1)));
            var meter = new OutcomeMeter(sim.State);
            for (int t = 0; t < LimitTicks && !meter.Decided; t++)
            {
                sim.Step();
                meter.Observe(sim.State);
            }

            runs[i] = meter;
        });

        var summary = new OutcomeSummary();
        foreach (var run in runs)
        {
            summary.Add(run);
        }

        return summary;
    }

    private static (double, double) Rate(double p, int n) => (p, OutcomeSummary.RateError(p, n));

    private static (double, double) MeanSe(IReadOnlyList<double> values) => (OutcomeSummary.Mean(values), OutcomeSummary.StandardError(values));

    private static Dictionary<string, Pinned> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ReferencePath));
        return doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => new Pinned(
                e.GetProperty("scenario").GetString()!,
                e.GetProperty("seeds").GetInt32(),
                e.GetProperty("measures").EnumerateObject().ToDictionary(
                    p => p.Name, p => (p.Value.GetProperty("value").GetDouble(), p.Value.GetProperty("tolerance").GetDouble()))))
            .ToDictionary(p => p.Scenario);
    }

    private sealed record Measure(string Key, Func<OutcomeSummary, (double Value, double Se)> Of, double Floor);

    private sealed record Pinned(string Scenario, int Seeds, Dictionary<string, (double Value, double Tolerance)> Values);
}
