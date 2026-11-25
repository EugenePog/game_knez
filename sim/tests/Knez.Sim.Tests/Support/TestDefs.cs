using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Support;

/// <summary>Defs built in code, independent of the repo's content files (those are covered by RepoContentTests).</summary>
public static class TestDefs
{
    /// <summary>A man's body: walks 1.4 m/s, runs 3.5 m/s, turns 360°/s (the agreed content values).</summary>
    public static readonly BodyDef Man = Body("test-man", walkTenths: 14);

    /// <summary>The standard rules' "experienced" (defence 40, morale 750).</summary>
    public static readonly ExperienceDef Experienced = CombatDef.Standard.FindExperience("experienced")!;

    /// <summary>The standard rules' "sword-shield-padded": sword, round shield, padded coat, cap — what a fighter carried
    /// by default before M3 step 9.</summary>
    public static readonly KitDef Kit = CombatDef.Standard.FindKit("sword-shield-padded")!;

    /// <summary>One group of <paramref name="count"/> experienced men in <see cref="Kit"/> (or the given ones).</summary>
    public static SquadMenDef[] Men(int count, ExperienceDef? experience = null, KitDef? kit = null) =>
        [new SquadMenDef(count, experience ?? Experienced, kit ?? Kit)];

    /// <summary>A kit of the standard rules by id.</summary>
    public static KitDef KitOf(string id) => CombatDef.Standard.FindKit(id) ?? throw new ArgumentException($"no kit '{id}'");

    /// <summary>An experience level of the standard rules by id.</summary>
    public static ExperienceDef Level(string id) => CombatDef.Standard.FindExperience(id) ?? throw new ArgumentException($"no level '{id}'");

    /// <summary>Two ranks, 1 m apart sideways, 1.5 m between ranks, turns 45°/s: round numbers for exact assertions.</summary>
    public static readonly FormationDef Line = new("test-line", Fix.One, Fix.Ratio(3, 2), ranks: 2, width: 0, turnRate: Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3));

    /// <summary>Two files wide, ranks follow from the squad size; turns 90°/s.</summary>
    public static readonly FormationDef Column = new("test-column", Fix.One, Fix.Ratio(3, 2), ranks: 0, width: 2, turnRate: Fix.FromInt(90), holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3));

    public static BodyDef Body(string id, int walkTenths, int radiusHundredths = 30) =>
        new(id, Fix.Ratio(radiusHundredths, 100), Fix.Ratio(walkTenths, 10), Fix.Ratio(35, 10), Fix.FromInt(360));

    public static readonly MapDef Map = new(Fix.FromInt(60), Fix.FromInt(40));

    /// <summary>The standard movement rules with closing in off (only men who touch fight): for tests of pushing.</summary>
    public static readonly MovementDef NoCloseIn = WithoutCloseIn(MovementDef.Standard);

    private static MovementDef WithoutCloseIn(MovementDef s) => new(s.PivotTolerance, Fix.Ratio(s.SlotCatchUpTicks, 20),
        s.PersonalSpace, Fix.Ratio(s.SeparationTicks, 20), s.HoldingReach, s.FormedUpReach, s.CollisionPasses, s.AllyPartingDepth,
        s.PathBudget, s.RegroupLead, Fix.Ratio(s.RegroupMaxWaitTicks, 20), s.RegroupCost, s.ReformPace, s.StepBackPace,
        s.WithdrawClearance, closeInRange: Fix.Zero, turnLagTime: Fix.Ratio(s.TurnLagTicks, 20));

    /// <summary><paramref name="s"/> with <paramref name="turnLagTime"/> seconds of turn lag (0 = men swing rigidly with their slots).</summary>
    public static MovementDef WithTurnLag(MovementDef s, Fix turnLagTime) => new(s.PivotTolerance, Fix.Ratio(s.SlotCatchUpTicks, 20),
        s.PersonalSpace, Fix.Ratio(s.SeparationTicks, 20), s.HoldingReach, s.FormedUpReach, s.CollisionPasses, s.AllyPartingDepth,
        s.PathBudget, s.RegroupLead, Fix.Ratio(s.RegroupMaxWaitTicks, 20), s.RegroupCost, s.ReformPace, s.StepBackPace,
        s.WithdrawClearance, s.CloseInRange, turnLagTime);

    /// <summary><paramref name="scenario"/> with <see cref="NoCloseIn"/> movement rules.</summary>
    public static ScenarioDef WithoutCloseIn(ScenarioDef scenario) =>
        new(scenario.Id, scenario.Description, scenario.Map, scenario.Squads, scenario.Script, NoCloseIn, scenario.Formations,
            scenario.Combat, scenario.Morale);

    /// <summary>
    /// The standard combat rules with every hit chance 0 and no floor: men engage, lock and swing, but nobody is ever
    /// hurt — for tests of movement and the melee lock that must not turn into a fight (M3 step 5 onward).
    /// </summary>
    public static readonly CombatDef Harmless = MakeHarmless();

    private static CombatDef MakeHarmless()
    {
        var s = CombatDef.Standard;
        var zeros = s.Weapons.Select(_ => (IReadOnlyList<int>)new int[s.ExperienceLevels.Count]).ToArray();
        return new CombatDef(s.Weapons, s.Shields, s.BodyArmours, s.HeadCovers, s.ExperienceLevels, s.Kits, zeros,
            minHit: 0, maxHit: s.MaxHit, s.FlankBonus, s.RearBonus, s.ShieldFromFlank, s.ShieldFromRear,
            s.ShieldWallBonus, s.LightWoundPenalty, s.HeavyWoundPenalty, s.LightWoundPace, s.HeavyWoundPace, s.FrontArc,
            s.RearArc, s.StrikeArc);
    }

    public static SquadSpawnDef Squad(string name, int side, int count, FixVec2 position, int facingDegrees,
        FormationDef? formation = null) =>
        new(name, side, Man, Men(count), formation ?? Line, position, Angle.FromDegrees(facingDegrees));

    /// <summary>
    /// The determinism-test battle: two squads of two (a short file each), 10 m apart. Fighters 0–1 are side 0,
    /// fighters 2–3 side 1 — DeterminismTests' command script relies on that. The facings (100°, 280°) fall between sine
    /// table entries on purpose, so the golden hash also pins the interpolated sin/cos.
    /// </summary>
    public static ScenarioDef SmallBattle() => new("test-small", "test", Map,
    [
        Squad("Blue", 0, 2, FixVec2.FromInts(10, 10), 100),
        Squad("Red", 1, 2, FixVec2.FromInts(10, 20), 280),
    ]);

    /// <summary>One line of 8 at (20, 10) facing north on the 60 × 40 map, with an optional script.</summary>
    public static ScenarioDef OneLine(params ScriptedOrderDef[] script) =>
        new("test-one-line", "test", Map, [Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90)], script);

    /// <summary>
    /// The determinism-test march: a line advances and then turns east, a column turns about and marches back. Covers
    /// marching, turning in place, arrival facing and slot following with interpolated sines (odd angles).
    /// </summary>
    public static ScenarioDef March() => new("test-march", "test", Map,
    [
        Squad("Line", 0, 8, FixVec2.FromInts(15, 8), 90),
        Squad("Column", 1, 6, FixVec2.FromInts(40, 12), 100, Column),
    ],
    [
        new ScriptedOrderDef(1, "Line", FixVec2.FromInts(19, 21), Angle.FromDegrees(0)),
        new ScriptedOrderDef(1, "Column", FixVec2.FromInts(37, 30), null),
    ]);

    /// <summary>
    /// The step 7 drill: a line ordered a quarter turn left (it wheels round its left end), re-forming into a column on the
    /// march and back into a line; a column ordered straight back (it faces about). The golden test also kills one man.
    /// </summary>
    public static ScenarioDef Drill() => new("test-drill", "test", Map,
    [
        Squad("Line", 0, 8, FixVec2.FromInts(40, 10), 90),
        Squad("Column", 1, 6, FixVec2.FromInts(20, 30), 100, Column),
    ],
    [
        new ScriptedOrderDef(1, "Line", FixVec2.FromInts(15, 12), null),
        new ScriptedOrderDef(1, "Column", FixVec2.FromInts(22, 14), null),
        ScriptedOrderDef.Formation(60, "Line", "test-column"),
        ScriptedOrderDef.Formation(260, "Line", "test-line"),
    ],
    formations: [Line, Column]);

    /// <summary>
    /// Benchmark S3 in miniature: two friendly lines of 8 cross at 90° — one marching east, one north — both 16 m from
    /// the crossing point, so they reach it together and their men have to jostle past each other.
    /// </summary>
    public static ScenarioDef Crossing() => new("test-crossing", "test", Map,
    [
        Squad("East", 0, 8, FixVec2.FromInts(14, 20), 0),
        Squad("North", 0, 8, FixVec2.FromInts(30, 4), 90),
    ],
    [
        new ScriptedOrderDef(1, "East", FixVec2.FromInts(46, 20), null),
        new ScriptedOrderDef(1, "North", FixVec2.FromInts(30, 36), null),
    ]);

    /// <summary>A line of 8 marches north straight through the spot where an enemy line of 8 holds its ground.</summary>
    public static ScenarioDef IntoAHoldingLine() => ThroughAHoldingLine("test-into-holding", moversSide: 0);

    /// <summary>The same march, but the holding line is on the movers' own side: it opens its ranks to them.</summary>
    public static ScenarioDef AlliesThroughAHoldingLine() => ThroughAHoldingLine("test-allies-through", moversSide: 1);

    /// <summary>
    /// Allies march through a holding allied line and stop just beyond it: their rear rank's slots are 1 m clear of the
    /// holders' rear rank, but when the anchor arrives that rank is still inside the line.
    /// </summary>
    public static ScenarioDef AlliesStopJustBeyondAHoldingLine() => ThroughAHoldingLine("test-allies-stop", moversSide: 1, targetY: 24);

    /// <summary>
    /// A holding Front line with an allied Reserve 4 m behind it; three enemy lines march into the Front, which is ordered
    /// half a metre forward into them at contact — so it stays on orders while being shoved back toward its reserve.
    /// </summary>
    public static ScenarioDef OrderedLineShovedIntoItsReserve() => new("test-shoved", "test", Map,
    [
        Squad("Front", 0, 8, FixVec2.FromInts(30, 20), 270),
        Squad("Reserve", 0, 8, FixVec2.FromInts(30, 24), 270),
        Squad("E1", 1, 8, FixVec2.FromInts(30, 10), 90),
        Squad("E2", 1, 8, FixVec2.FromInts(30, 7), 90),
        Squad("E3", 1, 8, FixVec2.FromInts(30, 4), 90),
    ],
    [
        new ScriptedOrderDef(1, "E1", FixVec2.FromInts(30, 34), null),
        new ScriptedOrderDef(1, "E2", FixVec2.FromInts(30, 31), null),
        new ScriptedOrderDef(1, "E3", FixVec2.FromInts(30, 28), null),
        new ScriptedOrderDef(140, "Front", new FixVec2(Fix.FromInt(30), Fix.Ratio(39, 2)), Angle.FromDegrees(270)),
    ]);

    /// <summary>Two allied lines side by side, both holding: "Front" at (20, 10) and "Rear" at (40, 10), facing north.</summary>
    public static ScenarioDef TwoAlliedLines() => new("test-two-allied", "test", Map,
    [
        Squad("Front", 0, 8, FixVec2.FromInts(20, 10), 90),
        Squad("Rear", 0, 8, FixVec2.FromInts(40, 10), 90),
    ]);

    private static ScenarioDef ThroughAHoldingLine(string id, int moversSide, int targetY = 32) => new(id, "test", Map,
    [
        Squad("Holders", 1, 8, FixVec2.FromInts(30, 20), 270),
        Squad("Movers", moversSide, 8, FixVec2.FromInts(30, 8), 90),
    ],
    [
        new ScriptedOrderDef(1, "Movers", FixVec2.FromInts(30, targetY), null),
    ]);

    /// <summary>Three lines of 8 march one behind another through a holding line of 8: a push four men deep.</summary>
    public static ScenarioDef DeepPush() => new("test-deep-push", "test", Map,
    [
        Squad("Holders", 1, 8, FixVec2.FromInts(30, 20), 270),
        Squad("M1", 0, 8, FixVec2.FromInts(30, 9), 90),
        Squad("M2", 0, 8, FixVec2.FromInts(30, 6), 90),
        Squad("M3", 0, 8, FixVec2.FromInts(30, 3), 90),
    ],
    [
        new ScriptedOrderDef(1, "M1", FixVec2.FromInts(30, 32), null),
        new ScriptedOrderDef(1, "M2", FixVec2.FromInts(30, 29), null),
        new ScriptedOrderDef(1, "M3", FixVec2.FromInts(30, 26), null),
    ]);

    /// <summary>
    /// Two lines ordered to stand against the east edge with their outer files past it, one just ahead of the other (their
    /// ranks touch, their slots don't clash), plus a column marching into the north edge: bodies pressed together against
    /// the map edge.
    /// </summary>
    public static ScenarioDef AgainstTheEdge() => new("test-edge", "test", Map,
    [
        Squad("L", 0, 8, FixVec2.FromInts(30, 20), 90),
        Squad("L2", 0, 8, FixVec2.FromInts(30, 10), 90),
        Squad("C", 1, 6, FixVec2.FromInts(10, 10), 90, Column),
    ],
    [
        new ScriptedOrderDef(1, "L", new FixVec2(Fix.Ratio(599, 10), Fix.FromInt(20)), Angle.FromDegrees(90)),
        new ScriptedOrderDef(1, "L2", new FixVec2(Fix.Ratio(599, 10), Fix.Ratio(225, 10)), Angle.FromDegrees(90)),
        new ScriptedOrderDef(1, "C", new FixVec2(Fix.FromInt(10), Fix.Ratio(399, 10)), null),
    ]);

    /// <summary>A wall across the 60 × 40 map at y 19–20 with a 3 m gate (x 28.5–31.5): benchmark S2 in miniature.</summary>
    public static MapDef GateMap() => new(Fix.FromInt(60), Fix.FromInt(40),
    [
        new WallDef(FixVec2.FromInts(0, 19), new FixVec2(Fix.Ratio(57, 2), Fix.FromInt(20))),
        new WallDef(new FixVec2(Fix.Ratio(63, 2), Fix.FromInt(19)), FixVec2.FromInts(60, 20)),
    ]);

    /// <summary>One squad of 8 (or 6 for a column) south of the gate, ordered through it to (30, 30).</summary>
    public static ScenarioDef ThroughTheGate(FormationDef formation, int count = 8) => new("test-gate", "test", GateMap(),
        [Squad("S", 0, count, FixVec2.FromInts(30, 10), 90, formation)],
        [new ScriptedOrderDef(1, "S", FixVec2.FromInts(30, 30), null)]);

    /// <summary>A player line facing north and an enemy column facing south.</summary>
    public static ScenarioDef TwoSquads() => new("test-two-squads", "test", Map,
    [
        Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90),
        Squad("Column", 1, 6, FixVec2.FromInts(30, 30), 270, Column),
    ]);
}
