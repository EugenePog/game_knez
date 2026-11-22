using System.Text.Json;
using Knez.Sim.Cli;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Determinism;

/// <summary>
/// Cross-runtime goldens (battle-sim.md §15, M2 step 3): <c>sim/tests/Golden/content-traces.json</c> holds, for every
/// content scenario, the fold of each tick's state hash and the final hash. This side checks them under .NET; Unity's
/// EditMode test (<c>game/Assets/Knez/Tests/CrossRuntimeGoldenTests.cs</c>) checks the very same file under Mono, so a
/// mismatch between runtimes shows as one side failing. A scenario added to content must be added to the file.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class ContentTraceGoldenTests
{
    public static string File => Path.Combine(RepoPaths.Root, "sim", "tests", "Golden", "content-traces.json");

    public record Entry(string Scenario, int Seed, int Ticks, string Trace, string Final);

    private static Entry[] Entries()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(File));
        return doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => new Entry(
            e.GetProperty("scenario").GetString()!, e.GetProperty("seed").GetInt32(), e.GetProperty("ticks").GetInt32(),
            e.GetProperty("trace").GetString()!, e.GetProperty("final").GetString()!)).ToArray();
    }

    private static IEnumerable<TestCaseData> Cases() => Entries().Select(e => new TestCaseData(e).SetName($"Golden_{e.Scenario}"));

    /// <summary>Runs an entry: the fold of every tick's total hash and the final total hash, as 16 hex digits.</summary>
    public static (string Trace, string Final) Run(Entry entry)
    {
        var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(Path.Combine(RepoPaths.Content, entry.Scenario)), (ulong)entry.Seed));
        ulong trace = 14695981039346656037UL;
        for (int i = 0; i < entry.Ticks; i++)
        {
            sim.Step();
            trace = unchecked((trace ^ sim.ComputeHash().Total) * 1099511628211UL);
        }

        return (trace.ToString("x16"), sim.ComputeHash().Total.ToString("x16"));
    }

    [TestCaseSource(nameof(Cases))]
    public void ContentScenario_MatchesTheRecordedTrace(Entry entry)
    {
        var (trace, final) = Run(entry);
        Assert.Multiple(() =>
        {
            Assert.That(trace, Is.EqualTo(entry.Trace), $"{entry.Scenario}: trace is {trace}");
            Assert.That(final, Is.EqualTo(entry.Final), $"{entry.Scenario}: final hash is {final}");
        });
    }

    [Test]
    public void EveryContentScenario_IsInTheFile()
    {
        var listed = Entries().Select(e => e.Scenario).ToHashSet();
        var onDisk = Directory.GetFiles(Path.Combine(RepoPaths.Content, "scenarios"), "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(RepoPaths.Content, f).Replace('\\', '/'));
        Assert.That(onDisk.Where(f => !listed.Contains(f)), Is.Empty, "add new scenarios to sim/tests/Golden/content-traces.json");
    }
}
