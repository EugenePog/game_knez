using Knez.Sim.Content;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Content;

/// <summary>DefLibrary and ScenarioLoader over small inline files: formats, strictness and located errors.</summary>
[TestFixture]
public class ContentLoaderTests
{
    private const string Fighters = """
        { "fighters": [ { "id": "warrior", "radius": 0.3 }, { "id": "archer", "radius": 0.25 } ] }
        """;

    private const string Formations = """
        {
          "description": "test formations",
          "formations": [
            { "id": "line", "spacing": 1.0, "rankSpacing": 1.2, "ranks": 2 },
            { "id": "column", "spacing": 1.0, "rankSpacing": 1.2, "width": 2 }
          ]
        }
        """;

    private static DefLibrary Defs() =>
        DefLibrary.Parse([new SourceText("formations.json", Formations), new SourceText("fighters.json", Fighters)]);

    private static string Scenario(string squad) => $$"""
        {
          "id": "test",
          "map": { "width": 60, "height": 40 },
          "squads": [
            {{squad}}
          ]
        }
        """;

    private const string GoodSquad =
        """{ "name": "A", "side": 0, "fighter": "warrior", "count": 8, "formation": "line", "position": [20, 10.5], "facing": 90 }""";

    [Test]
    public void DefLibrary_Parse_ReadsAllFilesSortedById()
    {
        var defs = Defs();
        Assert.Multiple(() =>
        {
            Assert.That(defs.Fighters.Select(f => f.Id), Is.EqualTo(new[] { "archer", "warrior" }));
            Assert.That(defs.Formations.Select(f => f.Id), Is.EqualTo(new[] { "column", "line" }));
            Assert.That(defs.FindFighter("warrior")!.Radius.Raw, Is.EqualTo(19661L));
            Assert.That(defs.FindFormation("column")!.Width, Is.EqualTo(2));
            Assert.That(defs.FindFormation("line")!.RankSpacing, Is.EqualTo(Fix.Ratio(6, 5)));
            Assert.That(defs.FindFighter("nobody"), Is.Null);
        });
    }

    [Test]
    public void DefLibrary_Parse_DuplicateIdAcrossFiles_NamesBothPlaces()
    {
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse(
        [
            new SourceText("a.json", Fighters),
            new SourceText("b.json", """{ "fighters": [ { "id": "warrior", "radius": 0.4 } ] }"""),
        ]));
        Assert.That(ex!.Message, Is.EqualTo("b.json:1:17: fighters[0].id: fighter 'warrior' is already defined at a.json:1:17"));
    }

    [TestCase("""{ "fighters": [ { "id": "w", "radius": 0.3, "radiu": 1 } ] }""",
        "f.json:1:54: fighters[0].radiu: unknown member 'radiu' (allowed: id, radius)")]
    [TestCase("""{ "fighters": [ { "id": "w" } ] }""",
        "f.json:1:17: fighters[0]: missing member 'radius'")]
    [TestCase("""{ "fighters": [ { "id": "w", "radius": "0.3" } ] }""",
        "f.json:1:40: fighters[0].radius: expected a number, got a string")]
    [TestCase("""{ "fighters": [ { "id": "w", "radius": 3e-1 } ] }""",
        "f.json:1:40: fighters[0].radius: 3e-1: exponents are not allowed in content numbers; write the number out")]
    [TestCase("""{ "fighters": [ { "id": "w", "radius": -0.3 } ] }""",
        "f.json:1:17: fighters[0]: radius must be > 0 and <= 2 m (got -0.30000).")]
    [TestCase("""{ "fighter": [] }""",
        "f.json:1:14: fighter: unknown member 'fighter' (allowed: description, fighters, formations)")]
    [TestCase("""[]""",
        "f.json:1:1: expected an object, got an array")]
    public void DefLibrary_Parse_BadDef_LocatedError(string json, string expected)
    {
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("f.json", json)]));
        Assert.That(ex!.Message, Is.EqualTo(expected));
    }

    [Test]
    public void ScenarioLoader_Parse_BuildsTheScenario()
    {
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(GoodSquad)), Defs());
        var squad = scenario.Squads[0];
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Id, Is.EqualTo("test"));
            Assert.That(scenario.Map.Width, Is.EqualTo(Fix.FromInt(60)));
            Assert.That(squad.Name, Is.EqualTo("A"));
            Assert.That(squad.Fighter.Id, Is.EqualTo("warrior"));
            Assert.That(squad.Formation.Id, Is.EqualTo("line"));
            Assert.That(squad.Count, Is.EqualTo(8));
            Assert.That(squad.Position, Is.EqualTo(new FixVec2(Fix.FromInt(20), Fix.Ratio(21, 2))));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(90)));
        });
    }

    [Test]
    public void ScenarioLoader_Parse_FractionalFacing_IsExact()
    {
        string squad = GoodSquad.Replace("\"facing\": 90", "\"facing\": 22.5");
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(squad)), Defs());
        Assert.That(scenario.Squads[0].Facing.Raw, Is.EqualTo(4096));
    }

    [TestCase("\"formation\": \"line\"", "\"formation\": \"wedge\"",
        "s.json:5:78: squads[0].formation: unknown formation 'wedge' (known: column, line)")]
    [TestCase("\"fighter\": \"warrior\"", "\"fighter\": \"berserker\"",
        "s.json:5:42: squads[0].fighter: unknown fighter 'berserker' (known: archer, warrior)")]
    [TestCase("\"count\": 8", "\"count\": 8.5",
        "s.json:5:62: squads[0].count: expected a whole number, got 8.5")]
    [TestCase("[20, 10.5]", "[20]",
        "s.json:5:98: squads[0].position: expected [x, y], got an array")]
    [TestCase("[20, 10.5]", "[20, 0.5]",
        "s.json:4:13: squads[0] 'A': slot 4 at (18.50000, -0.69999) is off the 60.00000 × 40.00000 m map; move the squad inward.")]
    [TestCase("\"side\": 0", "\"side\": -1",
        "s.json:5:5: squads[0]: side must be 0..7 (got -1).")]
    public void ScenarioLoader_Parse_BadSquad_LocatedError(string from, string to, string expected)
    {
        string squad = GoodSquad.Replace(from, to);
        var ex = Assert.Throws<ContentException>(() => ScenarioLoader.Parse(new SourceText("s.json", Scenario(squad)), Defs()));
        Assert.That(ex!.Message, Is.EqualTo(expected));
    }
}
