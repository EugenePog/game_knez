using Knez.Sim.Content;

namespace Knez.Sim.Tests.Content;

/// <summary>
/// Golden values for the whole content path — JSON text → ContentNumbers → defs → fingerprint → spawned state — on a
/// fixture owned by this test (content/ keeps being tuned, this must not). CI runs it on Linux x64 against values
/// recorded on ARM64 macOS, so it is the cross-architecture check for parsing and slot placement. Fractional numbers
/// and a facing between sine-table entries are there on purpose.
/// </summary>
[TestFixture]
[Category("Determinism")]
public class ContentGoldenTests
{
    private const string Defs = """
        {
          "fighters": [
            { "id": "warrior", "radius": 0.3, "walkSpeed": 1.4, "runSpeed": 3.5, "turnRate": 360 },
            { "id": "veteran", "radius": 0.325, "walkSpeed": 1.35, "runSpeed": 3.25, "turnRate": 300.5 }
          ],
          "formations": [
            { "id": "line", "spacing": 1.1, "rankSpacing": 1.25, "ranks": 2, "turnRate": 45 },
            { "id": "column", "spacing": 0.9, "rankSpacing": 1.3, "width": 2, "turnRate": 87.5 }
          ]
        }
        """;

    private const string Scenario = """
        {
          "id": "golden",
          "description": "Fixture for ContentGoldenTests; never edit — re-record the constants on purpose instead.",
          "map": { "width": 80.5, "height": 50.25 },
          "squads": [
            { "name": "A", "side": 0, "fighter": "warrior", "count": 7, "formation": "line", "position": [20.125, 10.7], "facing": 100.3 },
            { "name": "B", "side": 1, "fighter": "veteran", "count": 5, "formation": "column", "position": [40.333, 30.1], "facing": -37.25 }
          ],
          "script": [
            { "tick": 3, "order": "move", "squad": "A", "target": [25.5, 18.25] },
            { "tick": 3, "order": "move", "squad": "B", "target": [35, 22.75], "facing": 191.5 }
          ]
        }
        """;

    /// <summary>A change means content parsing, def hashing or slot placement changed: on purpose → re-record and say why.</summary>
    private const ulong GoldenFingerprint = 0xc529c228a7bd555fUL;

    private const ulong GoldenSpawnHash = 0xb656959b03f1c68eUL;

    [Test]
    public void Fixture_FingerprintAndSpawnedState_MatchRecordedValues()
    {
        var defs = DefLibrary.Parse([new SourceText("golden-defs.json", Defs)]);
        var scenario = ScenarioLoader.Parse(new SourceText("golden.json", Scenario), defs);
        var state = BattleSetup.Create(scenario, seed: 42);
        ulong spawn = StateHasher.Compute(state).Total;

        Assert.Multiple(() =>
        {
            Assert.That(scenario.Fingerprint, Is.EqualTo(GoldenFingerprint), $"fingerprint is 0x{scenario.Fingerprint:x16}");
            Assert.That(spawn, Is.EqualTo(GoldenSpawnHash), $"spawn hash is 0x{spawn:x16}");
        });
    }
}
