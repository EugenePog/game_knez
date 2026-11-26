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
          "bodies": [
            { "id": "man", "radius": 0.3, "walkSpeed": 1.4, "runSpeed": 3.5, "turnRate": 360 },
            { "id": "big", "radius": 0.325, "walkSpeed": 1.35, "runSpeed": 3.25, "turnRate": 300.5 }
          ],
          "formations": [
            { "id": "line", "spacing": 1.1, "rankSpacing": 1.25, "ranks": 2, "turnRate": 45, "holdWeight": 3, "allyPassSpeed": 0.333333333 },
            { "id": "column", "spacing": 0.9, "rankSpacing": 1.3, "width": 2, "turnRate": 87.5, "holdWeight": 4, "allyPassSpeed": 0.25 }
          ],
          "traits": [ { "id": "proud", "ability": { "id": "battle-cry", "cooldown": 120, "radius": 15, "ownMorale": 100, "enemyMorale": 50 } }, { "id": "bold", "ability": { "id": "front-rank-rush", "cooldown": 120 } } ]
        }
        """;

    private const string Scenario = """
        {
          "id": "golden",
          "description": "Fixture for ContentGoldenTests; never edit — re-record the constants on purpose instead.",
          "map": { "width": 80.5, "height": 50.25 },
          "squads": [
            { "name": "A", "side": 0, "body": "man", "men": [{ "count": 7, "experience": "experienced", "kit": "sword-shield-padded" }],
              "formation": "line", "position": [20.125, 10.7], "facing": 100.3 },
            { "name": "B", "side": 1, "body": "big", "men": [{ "count": 2, "experience": "new", "kit": "spear-shield" },
              { "count": 3, "experience": "experienced", "kit": "axe-shield-mail" }], "formation": "column", "position": [40.333, 30.1], "facing": -37.25 }
          ],
          "retainers": [
            { "name": "Eirik", "side": 1, "trait": "bold", "body": "man", "experience": "elite", "kit": "axe-shield-mail", "squad": "B" },
            { "name": "Vesti", "side": 0, "trait": "proud", "body": "big", "experience": "professional", "kit": "sword-shield-mail",
              "position": [18.5, 14.25], "facing": 33.3 }
          ],
          "script": [
            { "tick": 3, "order": "move", "squad": "A", "target": [25.5, 18.25] },
            { "tick": 3, "order": "move", "squad": "B", "target": [35, 22.75], "facing": 191.5 }
          ]
        }
        """;

    /// <summary>A change means content parsing, def hashing or slot placement changed: on purpose → re-record and say why.</summary>
    private const ulong GoldenFingerprint = 0x8a45225b453c0244UL;

    private const ulong GoldenSpawnHash = 0x598cb0a994258d18UL;

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
