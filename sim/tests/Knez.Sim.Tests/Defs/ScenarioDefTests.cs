using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Defs;

[TestFixture]
public class ScenarioDefTests
{
    [Test]
    public void Constructor_SquadNamesRepeat_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map,
        [
            TestDefs.Squad("A", 0, 5, FixVec2.FromInts(10, 10), 90),
            TestDefs.Squad("A", 1, 5, FixVec2.FromInts(30, 30), 270),
        ]));
        Assert.That(ex!.Message, Does.Contain("squads[1]").And.Contain("already used by squads[0]"));
    }

    [Test]
    public void Constructor_RearRankOffTheMap_ThrowsNamingTheSlot()
    {
        // Facing north at y = 1: the second rank stands 1.5 m behind, at y = -0.5.
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map,
            [TestDefs.Squad("Edge", 0, 8, FixVec2.FromInts(20, 1), 90)]));
        Assert.That(ex!.Message, Does.Contain("squads[0] 'Edge': slot 4").And.Contain("off the"));
    }

    [Test]
    public void Squads_CannotBeChangedThroughTheList()
    {
        var scenario = TestDefs.TwoSquads();
        Assert.That(scenario.Squads, Is.Not.InstanceOf<SquadSpawnDef[]>());
        Assert.That(scenario.Squads, Is.Not.InstanceOf<List<SquadSpawnDef>>());
    }

    [Test]
    public void Fingerprint_SameContent_SameValue()
    {
        var first = TestDefs.TwoSquads();
        var second = TestDefs.TwoSquads(); // separately built objects, equal content
        Assert.That(second.Fingerprint, Is.EqualTo(first.Fingerprint));
    }

    [Test]
    public void Fingerprint_AnyBattleRelevantChange_ChangesIt()
    {
        ulong baseline = TestDefs.TwoSquads().Fingerprint;
        var widerSpacing = new FormationDef("test-line", Fix.Ratio(11, 10), Fix.Ratio(3, 2), 2, 0, Fix.FromInt(45), 3, Fix.Ratio(1, 3));
        var heavierHold = new FormationDef("test-line", Fix.One, Fix.Ratio(3, 2), 2, 0, Fix.FromInt(45), 4, Fix.Ratio(1, 3));
        var easierPassage = new FormationDef("test-line", Fix.One, Fix.Ratio(3, 2), 2, 0, Fix.FromInt(45), 3, Fix.Ratio(1, 2));
        var biggerFighter = TestDefs.Body("test-warrior", walkTenths: 14, radiusHundredths: 35);

        ulong[] variants =
        [
            Scenario(squad0: TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(21, 10), 90)),                // position
            Scenario(squad0: TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 91)),                // facing
            Scenario(squad0: TestDefs.Squad("Line", 0, 7, FixVec2.FromInts(20, 10), 90)),                // count
            Scenario(squad0: TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90, widerSpacing)),  // formation def number
            Scenario(squad0: TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90, heavierHold)),   // hold weight
            Scenario(squad0: TestDefs.Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90, easierPassage)), // ally pass speed
            new ScenarioDef("test-two-squads", "test", TestDefs.Map,                                         // fighter def number
            [
                new SquadSpawnDef("Line", 0, biggerFighter, TestDefs.Men(8), TestDefs.Line, FixVec2.FromInts(20, 10), Angle.FromDegrees(90)),
                new SquadSpawnDef("Column", 1, biggerFighter, TestDefs.Men(6), TestDefs.Column, FixVec2.FromInts(30, 30), Angle.FromDegrees(270)),
            ]).Fingerprint,
            new ScenarioDef("test-two-squads", "test", new MapDef(Fix.FromInt(61), Fix.FromInt(40)),
                TestDefs.TwoSquads().Squads.ToArray()).Fingerprint,                                          // map
            Scenario(squad0: Line(TestDefs.Men(8, TestDefs.Level("new")))),                                 // experience
            Scenario(squad0: Line(TestDefs.Men(8, kit: TestDefs.KitOf("axe-shield-padded")))),              // kit
            Scenario(squad0: Line([.. TestDefs.Men(3), .. TestDefs.Men(5)])),                                // same men, two groups
        ];

        // M3 step 9: the order of a squad's groups is the order of its slots — a different battle.
        ulong spearsFirst = Scenario(squad0: Line([.. TestDefs.Men(3, kit: TestDefs.KitOf("spear-shield")), .. TestDefs.Men(5)]));
        ulong spearsLast = Scenario(squad0: Line([.. TestDefs.Men(5), .. TestDefs.Men(3, kit: TestDefs.KitOf("spear-shield"))]));
        Assert.Multiple(() =>
        {
            Assert.That(variants, Has.None.EqualTo(baseline));
            Assert.That(spearsLast, Is.Not.EqualTo(spearsFirst));
        });
    }

    private static SquadSpawnDef Line(SquadMenDef[] men) =>
        new("Line", 0, TestDefs.Man, men, TestDefs.Line, FixVec2.FromInts(20, 10), Angle.FromDegrees(90));

    [Test]
    public void Fingerprint_DescriptionIsForPeople_DoesNotChangeIt()
    {
        var a = TestDefs.TwoSquads();
        var b = new ScenarioDef(a.Id, "a different description", a.Map, a.Squads.ToArray());
        Assert.That(b.Fingerprint, Is.EqualTo(a.Fingerprint));
    }

    [Test]
    public void Constructor_TwoDifferentDefsWithOneId_Throws()
    {
        // State hashes name defs by id, so one id must mean one set of numbers.
        var otherLine = new FormationDef("test-line", Fix.FromInt(2), Fix.FromInt(2), ranks: 2, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3));
        var ex = Assert.Throws<ArgumentException>(() => new ScenarioDef("s", "", TestDefs.Map,
        [
            TestDefs.Squad("A", 0, 4, FixVec2.FromInts(10, 10), 90),
            TestDefs.Squad("B", 0, 4, FixVec2.FromInts(30, 10), 90, otherLine),
        ]));
        Assert.That(ex!.Message, Is.EqualTo("squads[1]: a different formation def with id 'test-line' is used by squads[0]."));
    }

    [Test]
    public void FormationDef_RanksAndWidthBothOrNeitherOrTooLarge_Throws()
    {
        Assert.Multiple(() =>
        {
            var turn = Fix.FromInt(45);
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 2, turn, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 0, width: 0, turn, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.Zero, Fix.One, ranks: 2, width: 0, turn, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: int.MaxValue, width: 0, turn, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 0, width: SquadSpawnDef.MaxCount + 1, turn, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 0, turn, 3, Fix.Zero), "allies must be able to pass");
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 0, turn, 3, Fix.Ratio(3, 2)), "not faster than walking");
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 0, Fix.Zero, 3, Fix.Ratio(1, 3)));
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 0, turn, 0, Fix.Ratio(1, 3)), "hold weight 0 would divide by zero");
            Assert.Throws<ArgumentException>(() => new FormationDef("f", Fix.One, Fix.One, ranks: 2, width: 0, turn, FormationDef.MaxHoldWeight + 1, Fix.Ratio(1, 3)));
        });
    }

    [Test]
    public void DefErrors_HaveNoParameterSuffix_SameTextOnEveryRuntime()
    {
        var ex = Assert.Throws<ArgumentException>(() => new BodyDef("w", Fix.Zero, Fix.One, Fix.FromInt(3), Fix.FromInt(360)));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Is.EqualTo("radius must be > 0 and <= 2 m (got 0.00000)."));
            Assert.That(ex.ParamName, Is.Null);
        });
    }

    private static ulong Scenario(SquadSpawnDef squad0) =>
        new ScenarioDef("test-two-squads", "test", TestDefs.Map, [squad0, TestDefs.TwoSquads().Squads[1]]).Fingerprint;
}
