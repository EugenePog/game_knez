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
            File.Copy(Path.Combine(RepoPaths.Content, "defs", "fighters.json"), Path.Combine(dir, "defs", "fighters.json"));
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

    private static (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
