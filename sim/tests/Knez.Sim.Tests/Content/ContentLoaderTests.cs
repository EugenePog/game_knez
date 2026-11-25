using Knez.Sim.Content;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Content;

/// <summary>DefLibrary and ScenarioLoader over small inline files: formats, strictness and located errors.</summary>
[TestFixture]
public class ContentLoaderTests
{
    private const string Bodies = """
        { "bodies": [ { "id": "man", "radius": 0.3, "walkSpeed": 1.4, "runSpeed": 3.5, "turnRate": 360 },
                      { "id": "boy", "radius": 0.25, "walkSpeed": 1.5, "runSpeed": 4, "turnRate": 360 } ] }
        """;

    private const string Formations = """
        {
          "description": "test formations",
          "formations": [
            { "id": "line", "spacing": 1.0, "rankSpacing": 1.2, "ranks": 2, "turnRate": 45, "holdWeight": 3, "allyPassSpeed": 0.333333333 },
            { "id": "column", "spacing": 1.0, "rankSpacing": 1.2, "width": 2, "turnRate": 90, "holdWeight": 2, "allyPassSpeed": 0.5 }
          ]
        }
        """;

    private static DefLibrary Defs() =>
        DefLibrary.Parse([new SourceText("formations.json", Formations), new SourceText("bodies.json", Bodies)]);

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
        """{ "name": "A", "side": 0, "body": "man", "men": [{ "count": 8, "experience": "experienced", "kit": "sword-shield-padded" }], "formation": "line", "position": [20, 10.5], "facing": 90 }""";

    [Test]
    public void DefLibrary_Parse_ReadsAllFilesSortedById()
    {
        var defs = Defs();
        Assert.Multiple(() =>
        {
            Assert.That(defs.Bodies.Select(f => f.Id), Is.EqualTo(new[] { "boy", "man" }));
            Assert.That(defs.Formations.Select(f => f.Id), Is.EqualTo(new[] { "column", "line" }));
            Assert.That(defs.FindBody("man")!.Radius.Raw, Is.EqualTo(19661L));
            Assert.That(defs.FindFormation("column")!.Width, Is.EqualTo(2));
            Assert.That(defs.FindFormation("line")!.RankSpacing, Is.EqualTo(Fix.Ratio(6, 5)));
            Assert.That(defs.FindBody("nobody"), Is.Null);
            Assert.That(defs.Rules, Is.SameAs(CombatDef.Standard), "no combat file: scenarios use the standard rules' kits");
        });
    }

    [Test]
    public void DefLibrary_Parse_DuplicateIdAcrossFiles_NamesBothPlaces()
    {
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse(
        [
            new SourceText("a.json", Bodies),
            new SourceText("b.json", """{ "bodies": [ { "id": "man", "radius": 0.4, "walkSpeed": 1, "runSpeed": 2, "turnRate": 90 } ] }"""),
        ]));
        Assert.That(ex!.Message, Is.EqualTo("b.json:1:15: bodies[0].id: body 'man' is already defined at a.json:1:15"));
    }

    [TestCase("""{ "bodies": [ { "id": "w", "radius": 0.3, "walkSpeed": 1, "runSpeed": 2, "turnRate": 90, "radiu": 1 } ] }""",
        "f.json:1:99: bodies[0].radiu: unknown member 'radiu' (allowed: id, radius, walkSpeed, runSpeed, turnRate)")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": 0.3, "walkSpeed": 1, "runSpeed": 2, "turnRate": 90, "weapon": "axe" } ] }""",
        "f.json:1:100: bodies[0].weapon: unknown member 'weapon' (allowed: id, radius, walkSpeed, runSpeed, turnRate)")]
    [TestCase("""{ "bodies": [ { "id": "w" } ] }""",
        "f.json:1:15: bodies[0]: missing member 'radius'")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": "0.3" } ] }""",
        "f.json:1:38: bodies[0].radius: expected a number, got a string")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": 3e-1 } ] }""",
        "f.json:1:38: bodies[0].radius: 3e-1: exponents are not allowed in content numbers; write the number out")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": -0.3, "walkSpeed": 1, "runSpeed": 2, "turnRate": 90 } ] }""",
        "f.json:1:15: bodies[0]: radius must be > 0 and <= 2 m (got -0.30000).")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": 0.3, "walkSpeed": 2, "runSpeed": 1.5, "turnRate": 90 } ] }""",
        "f.json:1:15: bodies[0]: runSpeed must be at least walkSpeed (got 1.50000 < 2.00000).")]
    [TestCase("""{ "bodies": [ { "id": "w", "radius": 0.3, "walkSpeed": 1 } ] }""",
        "f.json:1:15: bodies[0]: missing member 'runSpeed'")]
    [TestCase("""{ "fighter": [] }""",
        "f.json:1:14: fighter: unknown member 'fighter' (allowed: description, bodies, formations, movement, combat, morale)")]
    [TestCase("""{ "fighters": [] }""",
        "f.json:1:15: fighters: fighter types are gone (M3 step 9, ADR-0005): bodies go in 'bodies', kits in combat 'kits', and a scenario's squads name experience and kit per group of men")]
    [TestCase("""[]""",
        "f.json:1:1: expected an object, got an array")]
    [TestCase("""{ "formations": [ { "id": "w", "spacing": 1, "rankSpacing": 1, "ranks": 2, "turnRate": 45, "holdWeight": 3, "allyPassSpeed": 0.5, "shieldWall": 1 } ] }""",
        "f.json:1:145: formations[0].shieldWall: expected a boolean, got a number")]
    [TestCase("""{ "formations": [ { "id": "w", "spacing": 1, "rankSpacing": 1, "ranks": 2, "turnRate": 45, "holdWeight": 3, "allyPassSpeed": 0.5, "shieldWall": "true" } ] }""",
        "f.json:1:145: formations[0].shieldWall: expected a boolean, got a string")]
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
            Assert.That(squad.Body.Id, Is.EqualTo("man"));
            Assert.That(squad.Men, Has.Count.EqualTo(1));
            Assert.That(squad.Men[0].Experience, Is.SameAs(CombatDef.Standard.FindExperience("experienced")));
            Assert.That(squad.Men[0].Kit, Is.SameAs(CombatDef.Standard.FindKit("sword-shield-padded")));
            Assert.That(squad.Formation.Id, Is.EqualTo("line"));
            Assert.That(squad.Count, Is.EqualTo(8));
            Assert.That(squad.Position, Is.EqualTo(new FixVec2(Fix.FromInt(20), Fix.Ratio(21, 2))));
            Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(90)));
        });
    }

    [Test]
    public void ScenarioLoader_Parse_MixedSquad_FillsSlotsInGroupOrder()
    {
        // M3 step 9 (D-022): one squad, two groups — three new spearmen in front, five experienced axemen behind.
        string squad = GoodSquad.Replace("""[{ "count": 8, "experience": "experienced", "kit": "sword-shield-padded" }]""",
            """[{ "count": 3, "experience": "new", "kit": "spear-shield" }, { "count": 5, "experience": "experienced", "kit": "axe-shield-padded" }]""");
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(squad)), Defs());
        var state = BattleSetup.Create(scenario, 1);
        var men = state.Fighters.OrderBy(f => f.SlotIndex).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(scenario.Squads[0].Count, Is.EqualTo(8));
            Assert.That(men.Select(f => f.Kit.Id), Is.EqualTo(Enumerable.Repeat("spear-shield", 3).Concat(Enumerable.Repeat("axe-shield-padded", 5))));
            Assert.That(men.Select(f => f.Experience.Id), Is.EqualTo(Enumerable.Repeat("new", 3).Concat(Enumerable.Repeat("experienced", 5))));
            Assert.That(men.Select(f => f.Morale), Is.EqualTo(Enumerable.Repeat(600, 3).Concat(Enumerable.Repeat(750, 5))), "morale by experience");
            Assert.That(men.Select(f => f.Body.Id), Has.All.EqualTo("man"));
            Assert.That(scenario.Squads[0].GroupAt(2).Kit.Id, Is.EqualTo("spear-shield"));
            Assert.That(scenario.Squads[0].GroupAt(3).Kit.Id, Is.EqualTo("axe-shield-padded"));
        });
    }

    [Test]
    public void ScenarioLoader_Parse_FractionalFacing_IsExact()
    {
        string squad = GoodSquad.Replace("\"facing\": 90", "\"facing\": 22.5");
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(squad)), Defs());
        Assert.That(scenario.Squads[0].Facing.Raw, Is.EqualTo(4096));
    }

    [Test]
    public void ScenarioLoader_Parse_ReadsWalls()
    {
        string json = Scenario(GoodSquad).Replace("\"map\": { \"width\": 60, \"height\": 40 }",
            "\"map\": { \"width\": 60, \"height\": 40, \"walls\": [ { \"from\": [40, 20.5], \"to\": [35, 22] } ] }");
        var walls = ScenarioLoader.Parse(new SourceText("s.json", json), Defs()).Map.Walls;
        Assert.Multiple(() =>
        {
            Assert.That(walls, Has.Count.EqualTo(1));
            Assert.That(walls[0].Min, Is.EqualTo(new FixVec2(Fix.FromInt(35), Fix.Ratio(41, 2))));
            Assert.That(walls[0].Max, Is.EqualTo(FixVec2.FromInts(40, 22)));
        });
    }

    [TestCase("{ \"from\": [40, 20], \"to\": [35, 20.05] }", "map.walls[0]: a wall must be at least 0.1 m thick")]
    [TestCase("{ \"from\": [40, 20], \"to\": [65, 22] }", "reaches past the")]
    [TestCase("{ \"from\": [40, 20], \"to\": [45, 22], \"height\": 3 }", "map.walls[0].height: unknown member 'height' (allowed: from, to)")]
    [TestCase("{ \"from\": [5, 8], \"to\": [35, 10] }", "squads[0] 'A': slot 4 at")]
    public void ScenarioLoader_Parse_BadWall_LocatedError(string wall, string expected)
    {
        string json = Scenario(GoodSquad).Replace("\"map\": { \"width\": 60, \"height\": 40 }",
            "\"map\": { \"width\": 60, \"height\": 40, \"walls\": [ " + wall + " ] }");
        var ex = Assert.Throws<ContentException>(() => ScenarioLoader.Parse(new SourceText("s.json", json), Defs()));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void ScenarioLoader_Parse_ReadsTheScriptInOrder()
    {
        string json = Scenario(GoodSquad).Replace("\n  ]\n}", """
          ],
          "script": [
            { "tick": 5, "order": "move", "squad": "A", "target": [30, 20] },
            { "tick": 9, "order": "move", "squad": "A", "target": [30.5, 25], "facing": -90 }
          ]
        }
        """.TrimEnd());
        var script = ScenarioLoader.Parse(new SourceText("s.json", json), Defs()).Script;
        Assert.Multiple(() =>
        {
            Assert.That(script.Select(e => e.Tick), Is.EqualTo(new[] { 5, 9 }));
            Assert.That(script[0].SquadName, Is.EqualTo("A"));
            Assert.That(script[0].Facing, Is.Null);
            Assert.That(script[1].Target, Is.EqualTo(new FixVec2(Fix.Ratio(61, 2), Fix.FromInt(25))));
            Assert.That(script[1].Facing, Is.EqualTo(Angle.FromDegrees(270)));
        });
    }

    [TestCase("""{ "tick": 5, "order": "charge", "squad": "A", "target": [30, 20] }""", "script[0].order: unknown order 'charge' (known: move, formation)")]
    [TestCase("""{ "tick": 5, "order": "move", "squad": "Nobody", "target": [30, 20] }""", "script[0]: no squad is named 'Nobody'.")]
    [TestCase("""{ "tick": 0, "order": "move", "squad": "A", "target": [30, 20] }""", "script[0]: tick must be 1..2147483647 (got 0).")]
    [TestCase("""{ "tick": 5, "order": "move", "squad": "A", "target": [99, 20] }""", "script[0]: target (99.00000, 20.00000) is off the")]
    [TestCase("""{ "tick": 5, "order": "move", "squad": "A", "target": [30, 20], "speed": 2 }""", "script[0].speed: unknown member 'speed'")]
    public void ScenarioLoader_Parse_BadScriptEntry_LocatedError(string entry, string expected)
    {
        string json = Scenario(GoodSquad).Replace("\n  ]\n}", "\n  ],\n  \"script\": [ " + entry + " ]\n}");
        var ex = Assert.Throws<ContentException>(() => ScenarioLoader.Parse(new SourceText("s.json", json), Defs()));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void ScenarioLoader_Parse_ScriptOutOfTickOrder_IsAnError()
    {
        string json = Scenario(GoodSquad).Replace("\n  ]\n}", """
          ],
          "script": [
            { "tick": 9, "order": "move", "squad": "A", "target": [30, 20] },
            { "tick": 5, "order": "move", "squad": "A", "target": [30, 25] }
          ]
        }
        """.TrimEnd());
        var ex = Assert.Throws<ContentException>(() => ScenarioLoader.Parse(new SourceText("s.json", json), Defs()));
        Assert.That(ex!.Message, Does.Contain("script[1]: tick 5 comes before the previous entry's tick 9"));
    }

    [TestCase("\"formation\": \"line\"", "\"formation\": \"wedge\"",
        "s.json:5:143: squads[0].formation: unknown formation 'wedge' (known: column, line)")]
    [TestCase("\"body\": \"man\"", "\"body\": \"horse\"",
        "s.json:5:39: squads[0].body: unknown body 'horse' (known: boy, man)")]
    [TestCase("\"count\": 8", "\"count\": 8.5",
        "s.json:5:65: squads[0].men[0].count: expected a whole number, got 8.5")]
    [TestCase("\"count\": 8", "\"count\": 0",
        "s.json:5:54: squads[0].men[0]: count must be 1..64 (got 0).")]
    [TestCase("\"count\": 8", "\"count\": 65",
        "s.json:5:54: squads[0].men[0]: count must be 1..64 (got 65).")]
    [TestCase("[{ \"count\": 8, \"experience\": \"experienced\", \"kit\": \"sword-shield-padded\" }]",
        "[{ \"count\": 40, \"experience\": \"new\", \"kit\": \"axe-shield\" }, { \"count\": 40, \"experience\": \"new\", \"kit\": \"axe-shield\" }]",
        "s.json:5:5: squads[0]: the squad's size (men counts summed) must be 1..64 (got 80).")]
    [TestCase("\"experienced\"", "\"veteran\"",
        "s.json:5:82: squads[0].men[0].experience: unknown experience level 'veteran' (known: new, experienced, professional, elite)")]
    [TestCase("\"sword-shield-padded\"", "\"sword-padded\"",
        "s.json:5:104: squads[0].men[0].kit: unknown kit 'sword-padded' (known: axe-shield, spear-shield, axe-shield-padded, spear-shield-padded, sword-shield-padded, sword-shield-mail, axe-shield-mail)")]
    [TestCase("\"kit\": \"sword-shield-padded\" }", "\"kit\": \"sword-shield-padded\", \"weapon\": \"axe\" }",
        "s.json:5:137: squads[0].men[0].weapon: unknown member 'weapon' (allowed: count, experience, kit)")]
    [TestCase("[{ \"count\": 8, \"experience\": \"experienced\", \"kit\": \"sword-shield-padded\" }]", "[]",
        "s.json:5:53: squads[0].men: a squad needs at least one group of men")]
    [TestCase("\"body\": \"man\", \"men\": [{ \"count\": 8, \"experience\": \"experienced\", \"kit\": \"sword-shield-padded\" }]",
        "\"fighter\": \"warrior\", \"count\": 8",
        "s.json:5:42: squads[0].fighter: 'fighter' and 'count' became 'body' and 'men' (M3 step 9, ADR-0005), e.g. \"body\": \"man\", \"men\": [{ \"count\": 8, \"experience\": \"experienced\", \"kit\": \"sword-shield-padded\" }]")]
    [TestCase("[20, 10.5]", "[20]",
        "s.json:5:163: squads[0].position: expected [x, y], got an array")]
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

    private const string Movement = """
        { "movement": { "pivotTolerance": 45, "slotCatchUpTime": 0.35, "personalSpace": 0.12, "separationTime": 0.25,
          "holdingReach": 1.5, "formedUpReach": 0.4, "collisionPasses": 4, "allyPartingDepth": 0.25, "pathBudget": 2000,
          "regroupLead": 2, "regroupMaxWait": 4, "regroupCost": 0.5, "reformPace": 0.5, "stepBackPace": 0.5, "withdrawClearance": 3, "closeInRange": 3,
          "turnLagTime": 2.5 } }
        """;

    [Test]
    public void DefLibrary_Parse_MovementRules_ReachTheScenarioAndItsFingerprint()
    {
        var defs = DefLibrary.Parse([new SourceText("formations.json", Formations), new SourceText("bodies.json", Bodies),
            new SourceText("movement.json", Movement)]);
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(GoodSquad)), defs);
        var standard = ScenarioLoader.Parse(new SourceText("s.json", Scenario(GoodSquad)), Defs());
        var rules = scenario.Movement;
        Assert.Multiple(() =>
        {
            Assert.That(rules.PivotToleranceUnits, Is.EqualTo(Angle.UnitsPerTurn / 8));
            Assert.That(rules.SlotCatchUpTicks, Is.EqualTo(7), "0.35 s = 7 ticks");
            Assert.That(rules.FormedUpReach, Is.EqualTo(Fix.Ratio(4, 10)));
            Assert.That(rules.CollisionPasses, Is.EqualTo(4));
            Assert.That(rules.PathBudget, Is.EqualTo(2000));
            Assert.That(rules.RegroupMaxWaitTicks, Is.EqualTo(80));
            Assert.That(rules.RegroupCost, Is.EqualTo(Fix.Ratio(1, 2)));
            Assert.That(rules.TurnLagTicks, Is.EqualTo(50), "2.5 s = 50 ticks");
            Assert.That(standard.Movement, Is.SameAs(MovementDef.Standard), "no movement file: the standard rules");
            Assert.That(scenario.Fingerprint, Is.Not.EqualTo(standard.Fingerprint));
        });
    }

    [TestCase("\"collisionPasses\": 4", "\"collisionPasses\": 0", "movement: collisionPasses must be 1..20 (got 0)")]
    [TestCase("\"regroupMaxWait\": 4", "\"regroupMaxWait\": 0.01", "movement: regroupMaxWait must be 0.05..60 s")]
    [TestCase("\"regroupCost\": 0.5", "\"regroupCost\": 0.5, \"regroupCosts\": 1", "movement.regroupCosts: unknown member 'regroupCosts'")]
    public void DefLibrary_Parse_BadMovementRules_LocatedError(string from, string to, string expected)
    {
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("m.json", Movement.Replace(from, to))]));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void DefLibrary_Parse_MovementRulesInTwoFiles_IsAnError()
    {
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("a.json", Movement), new SourceText("b.json", Movement)]));
        Assert.That(ex!.Message, Does.Contain("movement 'rules' is already defined at a.json"));
    }

    private static string RepoCombat() => File.ReadAllText(Path.Combine(Support.RepoPaths.Content, "defs", "combat.json"));

    private static DefLibrary WithCombat(string combat) =>
        DefLibrary.Parse([new SourceText("formations.json", Formations), new SourceText("bodies.json", Bodies),
            new SourceText("combat.json", combat)]);

    [Test]
    public void DefLibrary_Parse_CombatRules_ReachTheScenarioAndItsFingerprint()
    {
        var defs = WithCombat(RepoCombat().Replace("\"elite\": 660", "\"elite\": 661"));
        var scenario = ScenarioLoader.Parse(new SourceText("s.json", Scenario(GoodSquad)), defs);
        var standard = ScenarioLoader.Parse(new SourceText("s.json", Scenario(GoodSquad)), Defs());
        var combat = scenario.Combat;
        Assert.Multiple(() =>
        {
            Assert.That(combat.BaseHit(combat.FindWeapon("sword")!, combat.FindExperience("elite")!), Is.EqualTo(661));
            Assert.That(combat.FindWeapon("seax")!.Reach.Raw, Is.EqualTo(19661L), "0.3 rounded to the nearest raw unit");
            Assert.That(combat.FindWeapon("axe")!.WindupTicks, Is.EqualTo(18));
            Assert.That(combat.LightWoundPace.Raw, Is.EqualTo(58982L), "0.9 rounded to the nearest raw unit");
            Assert.That(scenario.Squads[0].Men[0].Kit, Is.SameAs(combat.FindKit("sword-shield-padded")), "kits from the file's own rules");
            Assert.That(scenario.Squads[0].Men[0].Experience, Is.SameAs(combat.FindExperience("experienced")));
            Assert.That(standard.Combat, Is.SameAs(CombatDef.Standard), "no combat file: the standard rules");
            Assert.That(standard.Squads[0].Men[0].Kit, Is.SameAs(CombatDef.Standard.FindKit("sword-shield-padded")));
            Assert.That(scenario.Fingerprint, Is.Not.EqualTo(standard.Fingerprint));
        });
    }

    [Test]
    public void DefLibrary_Parse_KitsAndWeaponNumbers()
    {
        var combat = WithCombat(RepoCombat()).Combat!;
        var kit = combat.FindKit("spear-shield-padded")!;
        Assert.Multiple(() =>
        {
            Assert.That(new[] { kit.Weapon.Id, kit.Shield.Id, kit.BodyArmour.Id, kit.HeadCover.Id }, Is.EqualTo(new[] { "spear", "round", "padded", "cap" }));
            Assert.That(kit.Weapon, Is.SameAs(combat.FindWeapon("spear")), "a kit holds the rules' own parts");
            Assert.That(combat.Kits, Has.Count.EqualTo(7));
            Assert.That(combat.FindWeapon("axe")!.Penetration, Is.EqualTo(300));
            Assert.That(combat.FindWeapon("spear")!.Defence, Is.EqualTo(30));
            Assert.That(combat.Fingerprint, Is.EqualTo(CombatDef.Standard.Fingerprint), "combat.json and the code's standard rules agree");
        });
    }

    [TestCase("\"weapon\": \"axe\",   \"shield\"", "\"weapon\": \"pike\",   \"shield\"", "combat.kits[0].weapon: no weapon 'pike' (known: sword, axe, spear, seax)")]
    [TestCase("\"bodyArmour\": \"none\",   \"headCover\": \"none\" }", "\"bodyArmour\": \"none\",   \"headCover\": \"crown\" }", "combat.kits[0].headCover: no head cover 'crown' (known: none, cap, helm)")]
    [TestCase("\"headCover\": \"none\" },\n      { \"id\": \"spear-shield\"", "\"headCover\": \"none\", \"experience\": \"new\" },\n      { \"id\": \"spear-shield\"", "combat.kits[0].experience: unknown member 'experience' (allowed: id, weapon, shield, bodyArmour, headCover)")]
    [TestCase("{ \"id\": \"spear-shield\",", "{ \"id\": \"axe-shield\",", "combat.kits[1].id: kit 'axe-shield' is listed twice")]
    public void DefLibrary_Parse_BadKit_LocatedError(string from, string to, string expected)
    {
        string text = RepoCombat();
        Assert.That(text, Does.Contain(from), "the test's anchor is still in combat.json");
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("combat.json", text.Replace(from, to))]));
        Assert.That(ex!.Message, Does.StartWith("combat.json:").And.Contain(expected));
    }

    [TestCase("\"professional\": 560, \"elite\": 640", "\"professional\": 560", "combat.hitChance.axe: missing member 'elite'")]
    [TestCase("\"seax\":  { \"new\"", "\"pike\": { \"new\": 1 }, \"seax\":  { \"new\"", "combat.hitChance.pike: unknown member 'pike' (allowed: sword, axe, spear, seax)")]
    [TestCase("\"down\": 170", "\"down\": 160", "combat.weapons[1]: wounds: light + heavy + down must be 1000 (got 990)")]
    [TestCase("\"penetration\": 0, \"defence\": 0 },\n      { \"id\": \"axe\"", "\"defence\": 0 },\n      { \"id\": \"axe\"", "combat.weapons[0]: missing member 'penetration'")]
    [TestCase("\"penetration\": 300", "\"penetration\": 1001", "combat.weapons[1].penetration: must be 0..1000 (got 1001)")]
    [TestCase("\"kits\": [", "\"warriorTypes\": [], \"kits\": [", "combat.warriorTypes: unknown member 'warriorTypes'")]
    [TestCase("\"min\": 20", "\"min\": 960", "combat: hitLimits.min must be 0..hitLimits.max (950) (got 960)")]
    [TestCase("\"shieldWall\": 80", "\"shieldWall\": 80, \"shieldwall\": 1", "combat.position.shieldwall: unknown member 'shieldwall'")]
    [TestCase("\"elite\": 660", "\"elite\": 1660", "combat.hitChance.sword.elite: must be 0..1000 (got 1660)")]
    [TestCase("\"shieldWall\": 80", "\"shieldWall\": 1080", "combat.position.shieldWall: must be 0..1000 (got 1080)")]
    [TestCase("\"strikeArc\": 60,", "", "combat: missing member 'strikeArc'")]
    [TestCase("\"frontArc\": 60, ", "", "combat.position: missing member 'frontArc'")]
    [TestCase("\"rearArc\": 120", "\"rearArc\": 50", "combat: position.rearArc must be at least position.frontArc")]
    [TestCase("\"strikeArc\": 60", "\"strikeArc\": 0", "combat: strikeArc must be > 0 and <= 180")]
    [TestCase("\"rearArc\": 120", "\"rearArc\": 181", "combat: position.rearArc must be > 0 and <= 180")]
    [TestCase("{ \"id\": \"mail\"", "{ \"id\": \"padded\"", "combat.bodyArmour[2].id: body armour 'padded' is listed twice")]
    [TestCase("{ \"id\": \"seax\"", "{ \"id\": \"axe\"", "combat.weapons[3].id: weapon 'axe' is listed twice")]
    public void DefLibrary_Parse_BadCombatRules_LocatedError(string from, string to, string expected)
    {
        string text = RepoCombat();
        Assert.That(text, Does.Contain(from), "the test's anchor is still in combat.json");
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("combat.json", text.Replace(from, to))]));
        Assert.That(ex!.Message, Does.StartWith("combat.json:").And.Contain(expected));
    }

    [Test]
    public void DefLibrary_Parse_EmptyCatalogue_LocatedError()
    {
        string text = System.Text.RegularExpressions.Regex.Replace(RepoCombat(), "\"experience\": \\[[^\\]]*\\]", "\"experience\": []");
        var ex = Assert.Throws<ContentException>(() => DefLibrary.Parse([new SourceText("combat.json", text)]));
        Assert.That(ex!.Message, Does.StartWith("combat.json:").And.Contain("combat.experience: the rules need at least one experience level"));
    }

    [Test]
    public void DefLibrary_Parse_CombatRulesInTwoFiles_IsAnError()
    {
        var ex = Assert.Throws<ContentException>(() =>
            DefLibrary.Parse([new SourceText("a.json", RepoCombat()), new SourceText("b.json", RepoCombat())]));
        Assert.That(ex!.Message, Does.Contain("combat 'rules' is already defined at a.json"));
    }
}
