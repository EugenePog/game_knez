using System.Text.Json;
using Knez.Sim.Cli;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Cli;

[TestFixture]
public class CliAppTests
{
    private static readonly string Formations = RepoPaths.Scenario("formations");

    [Test]
    public void Run_PrintsForcesTickCountAndTheSameFinalHashAsTheSimulation()
    {
        var (exit, stdout, stderr) = Invoke("run", Formations, "--seed", "42", "--ticks", "300");

        var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(Formations), 42));
        sim.StepTicks(300);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk), stderr);
            Assert.That(stdout, Does.Contain("scenario:   formations ("));
            Assert.That(stdout, Does.Contain("forces:     side 0: 2 squads, 16 fighters; side 1: 3 squads, 19 fighters"));
            Assert.That(stdout, Does.Contain("ticks:      300 (15.0 s of battle)"));
            Assert.That(stdout, Does.Contain("crowding:   0.00% of fighter-ticks overlap > 5 cm (worst tick: 0 fighters); 0.00% passing through allied ranks; 0.00% inside walls > 5 cm"));
            Assert.That(stdout, Does.Contain($"final hash: {sim.ComputeHash()}"));
        });
    }

    [Test]
    public void Run_Json_IsOneParsableObjectWithTheHash()
    {
        var (exit, stdout, stderr) = Invoke("run", Formations, "--seed", "7", "--ticks", "20", "--json");

        Assert.That(exit, Is.EqualTo(CliApp.ExitOk), stderr);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("scenario").GetString(), Is.EqualTo("formations"));
            Assert.That(root.GetProperty("seed").GetUInt64(), Is.EqualTo(7UL));
            Assert.That(root.GetProperty("ticks").GetInt32(), Is.EqualTo(20));
            Assert.That(root.GetProperty("squads").GetInt32(), Is.EqualTo(5));
            Assert.That(root.GetProperty("fighters").GetInt32(), Is.EqualTo(35));
            Assert.That(root.GetProperty("finalHash").GetString(), Has.Length.EqualTo(16));
            Assert.That(root.GetProperty("sections").GetProperty("squads").GetString(), Has.Length.EqualTo(16));
            Assert.That(root.GetProperty("overlap").GetProperty("ratio").GetDouble(), Is.Zero);
            Assert.That(root.GetProperty("overlap").GetProperty("worstTickFighters").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public void Run_Defaults_AreSeedOneAndThirtySeconds()
    {
        var (_, stdout, _) = Invoke("run", Formations);
        Assert.Multiple(() =>
        {
            Assert.That(stdout, Does.Contain("seed:       1"));
            Assert.That(stdout, Does.Contain("ticks:      600 (30.0 s of battle)"));
        });
    }

    [TestCase(new[] { "frobnicate" }, "unknown command")]
    [TestCase(new[] { "run" }, "'run' needs a scenario file")]
    [TestCase(new[] { "run", "" }, "'run' needs a scenario file")]
    [TestCase(new[] { "run", "a.json", "b.json" }, "'run' takes one scenario file")]
    [TestCase(new[] { "run", "x.json", "--ticks", "0" }, "--ticks needs a positive whole number")]
    [TestCase(new[] { "run", "x.json", "--seed", "-3" }, "--seed needs a positive whole number")]
    [TestCase(new[] { "run", "x.json", "--speed", "2" }, "unknown option")]
    [TestCase(new[] { "run", "no/such/scenario.json" }, "scenario file not found: no/such/scenario.json")]
    [TestCase(new[] { "batch" }, "'batch' needs a scenario file")]
    [TestCase(new[] { "batch", "x.json", "--seeds", "9-3" }, "--seeds needs a seed or a range like 1-200 (at most 100 000 seeds)")]
    [TestCase(new[] { "batch", "x.json", "--runs", "3" }, "unknown option")]
    [TestCase(new[] { "bench", "x.json", "--runs", "0" }, "--runs needs a positive whole number")]
    [TestCase(new[] { "bench", "x.json", "--seeds", "1-2" }, "unknown option")]
    public void BadArguments_ExitWithUsageCodeAndExplain(string[] args, string message)
    {
        var (exit, _, stderr) = Invoke(args);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitUsage));
            Assert.That(stderr, Does.Contain(message));
        });
    }

    [Test]
    public void Run_BrokenScenario_ExitsWithUsageCodeAndALocatedMessage()
    {
        string dir = Directory.CreateTempSubdirectory("knez-cli-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "defs"));
            Directory.CreateDirectory(Path.Combine(dir, "scenarios"));
            File.Copy(Path.Combine(RepoPaths.Content, "defs", "bodies.json"), Path.Combine(dir, "defs", "bodies.json"));
            File.Copy(Path.Combine(RepoPaths.Content, "defs", "formations.json"), Path.Combine(dir, "defs", "formations.json"));
            string scenario = Path.Combine(dir, "scenarios", "broken.json");
            File.WriteAllText(scenario, File.ReadAllText(Formations).Replace("\"shieldwall\"", "\"testudo\""));

            var (exit, _, stderr) = Invoke("run", scenario);
            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.EqualTo(CliApp.ExitUsage));
                Assert.That(stderr, Does.Match(@"broken\.json:\d+:\d+: squads\[1\]\.formation: unknown formation 'testudo'"));
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Help_PrintsUsageAndSucceeds()
    {
        var (exit, stdout, _) = Invoke("help");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(stdout, Does.Contain("Usage:"));
        });
    }

    [Test]
    public void Run_ReportsStuckAndSlotError()
    {
        var (exit, stdout, _) = Invoke("run", RepoPaths.Scenario("march"), "--ticks", "100");
        var (_, json, _) = Invoke("run", RepoPaths.Scenario("march"), "--ticks", "100", "--json");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(stdout, Does.Contain("stuck:      0 fighters stuck > 2 s"));
            Assert.That(stdout, Does.Contain("slot error: mean "));
            Assert.That(stdout, Does.Contain("orders still open"));
            Assert.That(root.GetProperty("stuck").GetProperty("fighters").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("slotError").GetProperty("worst").GetDouble(), Is.GreaterThanOrEqualTo(0));
            Assert.That(root.GetProperty("engaged").GetProperty("openOrders").GetInt32(), Is.GreaterThan(0), "100 ticks: still marching");
            Assert.That(root.TryGetProperty("longestFarFromSlotTicks", out _), Is.True);
        });
    }

    [Test]
    public void Batch_RunsEverySeed_AndAggregates()
    {
        var (exit, stdout, _) = Invoke("batch", RepoPaths.Scenario("march"), "--seeds", "3-5", "--ticks", "60", "--json");
        using var doc = System.Text.Json.JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(root.GetProperty("runs").GetInt32(), Is.EqualTo(3));
            Assert.That(root.GetProperty("seeds").GetString(), Is.EqualTo("3-5"));
            Assert.That(root.GetProperty("stuck").GetProperty("worstRunFighters").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("distinctOutcomes").GetInt32(), Is.EqualTo(1), "movement draws no random numbers yet: every seed, the same battle");
            Assert.That(root.GetProperty("battle").ValueKind, Is.EqualTo(JsonValueKind.Null), "a march has no blows and no decision");
        });
    }

    [Test]
    public void Batch_AFight_PrintsTheBalanceMeasures()
    {
        string s6 = Path.Combine(RepoPaths.Content, "scenarios", "bench", "s6-lines.json");
        var (exit, stdout, stderr) = Invoke("batch", s6, "--seeds", "1-4", "--ticks", "1200");
        var (jsonExit, json, _) = Invoke("batch", s6, "--seeds", "1-4", "--ticks", "1200", "--json");
        using var doc = JsonDocument.Parse(json);
        var battle = doc.RootElement.GetProperty("battle");
        var ended = battle.GetProperty("ended");
        double wins = battle.GetProperty("wins").EnumerateObject().Sum(p => p.Value.GetDouble());
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk), stderr);
            Assert.That(jsonExit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(stdout, Does.Contain("wins:       side 0 "));
            Assert.That(stdout, Does.Contain("ended:      "));
            Assert.That(stdout, Does.Contain("length:     decided at "));
            Assert.That(stdout, Does.Contain("breaks:     "));
            Assert.That(stdout, Does.Contain("fallen:     side 0 "));
            Assert.That(ended.GetProperty("rout").GetInt32() + ended.GetProperty("annihilation").GetInt32()
                + ended.GetProperty("nobody").GetInt32() + ended.GetProperty("undecided").GetInt32(), Is.EqualTo(4));
            Assert.That(ended.GetProperty("limitSeconds").GetDouble(), Is.EqualTo(60));
            Assert.That(wins, Is.EqualTo(1 - (ended.GetProperty("nobody").GetInt32() + ended.GetProperty("undecided").GetInt32()) / 4.0).Within(1e-9));
            Assert.That(battle.GetProperty("lossAtBreak").GetProperty("breaks").GetInt32(), Is.GreaterThan(0));
            var bySide = battle.GetProperty("lossAtBreak").GetProperty("bySide");
            Assert.That(bySide.GetProperty("0").GetProperty("breaks").GetInt32() + bySide.GetProperty("1").GetProperty("breaks").GetInt32(),
                Is.EqualTo(battle.GetProperty("lossAtBreak").GetProperty("breaks").GetInt32()), "every break belongs to a side");
            Assert.That(stdout, Does.Contain("% of their men mean"));
            Assert.That(stdout, Does.Match("side [01]: [0-9]+ at [0-9.]+%"));
        });
    }

    [Test]
    public void Bench_PrintsTickTimePercentiles()
    {
        var (exit, stdout, _) = Invoke("bench", RepoPaths.Scenario("march"), "--ticks", "50", "--runs", "2", "--json");
        using var doc = System.Text.Json.JsonDocument.Parse(stdout);
        var times = doc.RootElement.GetProperty("tickMs");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(doc.RootElement.GetProperty("fighters").GetInt32(), Is.GreaterThan(0));
            Assert.That(times.GetProperty("p50").GetDouble(), Is.LessThanOrEqualTo(times.GetProperty("p99").GetDouble()));
            Assert.That(times.GetProperty("p99").GetDouble(), Is.LessThanOrEqualTo(times.GetProperty("max").GetDouble()));
        });
    }

    private static (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
