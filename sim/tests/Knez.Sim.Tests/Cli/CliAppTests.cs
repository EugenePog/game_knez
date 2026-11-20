using System.Text.Json;
using Knez.Sim.Cli;

namespace Knez.Sim.Tests.Cli;

[TestFixture]
public class CliAppTests
{
    [Test]
    public void Run_PrintsTickCountAndTheSameFinalHashAsTheSimulation()
    {
        var (exit, stdout, _) = Invoke("run", "--seed", "42", "--ticks", "300");

        var sim = new BattleSimulation(new BattleState(42));
        sim.StepTicks(300);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(stdout, Does.Contain("ticks:      300 (15.0 s of battle)"));
            Assert.That(stdout, Does.Contain($"final hash: {sim.ComputeHash()}"));
        });
    }

    [Test]
    public void Run_Json_IsOneParsableObjectWithTheHash()
    {
        var (exit, stdout, _) = Invoke("run", "--seed", "7", "--ticks", "20", "--json");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitOk));
            Assert.That(root.GetProperty("seed").GetUInt64(), Is.EqualTo(7UL));
            Assert.That(root.GetProperty("ticks").GetInt32(), Is.EqualTo(20));
            Assert.That(root.GetProperty("finalHash").GetString(), Has.Length.EqualTo(16));
        });
    }

    [Test]
    public void Run_Defaults_AreSeedOneAndThirtySeconds()
    {
        var (_, stdout, _) = Invoke("run");
        Assert.Multiple(() =>
        {
            Assert.That(stdout, Does.Contain("seed:       1"));
            Assert.That(stdout, Does.Contain("ticks:      600 (30.0 s of battle)"));
        });
    }

    [TestCase(new[] { "frobnicate" }, "unknown command")]
    [TestCase(new[] { "run", "--ticks", "0" }, "--ticks needs a positive whole number")]
    [TestCase(new[] { "run", "--seed", "-3" }, "--seed needs a positive whole number")]
    [TestCase(new[] { "run", "--speed", "2" }, "unknown option")]
    [TestCase(new[] { "run", "battle.json" }, "scenario files arrive in M1")]
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
