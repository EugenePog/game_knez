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
    public void Repo_DefsCarryTheMovementRules()
    {
        // Content, not code, owns the movement rules: a scenario from the repo must never fall back to the code's copy.
        var scenario = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, "scenarios", "march.json"));
        Assert.That(scenario.Movement, Is.Not.SameAs(Knez.Sim.Defs.MovementDef.Standard));
    }

    [Test]
    public void Repo_MovementRules_EqualTheCodesStandardCopy()
    {
        // Tests and code-built scenarios use MovementDef.Standard; content uses movement.json. Keep them identical, or
        // the goldens stop describing the game. Change both together.
        var file = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, "scenarios", "march.json")).Movement;
        var standard = Knez.Sim.Defs.MovementDef.Standard;
        foreach (var property in typeof(Knez.Sim.Defs.MovementDef).GetProperties())
        {
            Assert.That(property.GetValue(file), Is.EqualTo(property.GetValue(standard)), property.Name);
        }
    }

    [Test]
    public void Repo_CombatRules_ComeFromContent_AndEqualTheCodesStandardCopy()
    {
        // Same deal as the movement rules: content owns them, tests use CombatDef.Standard; the fingerprint covers every
        // number and id, in order.
        var combat = ScenarioFiles.Load(Path.Combine(RepoPaths.Content, "scenarios", "march.json")).Combat;
        Assert.Multiple(() =>
        {
            Assert.That(combat, Is.Not.SameAs(Knez.Sim.Defs.CombatDef.Standard));
            Assert.That(combat.Fingerprint, Is.EqualTo(Knez.Sim.Defs.CombatDef.Standard.Fingerprint),
                "combat.json and CombatDef.Standard differ; change both together");
        });
    }

    [Test]
    public void Repo_HasAtLeastOneScenario()
    {
        Assert.That(ScenarioFilesInRepo(), Is.Not.Empty);
    }
}
