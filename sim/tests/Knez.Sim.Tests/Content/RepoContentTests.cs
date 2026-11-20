using Knez.Sim.Cli;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Content;

/// <summary>Every content file in the repository loads and spawns, so broken content fails CI, not a playtest.</summary>
[TestFixture]
public class RepoContentTests
{
    private static IEnumerable<string> ScenarioFilesInRepo() =>
        Directory.GetFiles(Path.Combine(RepoPaths.Content, "scenarios"), "*.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(RepoPaths.Root, path));

    [TestCaseSource(nameof(ScenarioFilesInRepo))]
    public void Scenario_LoadsAndSpawns(string relativePath)
    {
        var scenario = ScenarioFiles.Load(Path.Combine(RepoPaths.Root, relativePath));
        var state = BattleSetup.Create(scenario, seed: 1);
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileNameWithoutExtension(relativePath), Is.EqualTo(scenario.Id),
                "a scenario's id matches its file name");
            Assert.That(state.Squads, Has.Count.EqualTo(scenario.Squads.Count));
            Assert.That(state.Fighters, Has.Count.EqualTo(scenario.Squads.Sum(s => s.Count)));
        });
    }

    [Test]
    public void Repo_HasAtLeastOneScenario()
    {
        Assert.That(ScenarioFilesInRepo(), Is.Not.Empty);
    }
}
