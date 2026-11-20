using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Tests.Support;

/// <summary>Defs built in code, independent of the repo's content files (those are covered by RepoContentTests).</summary>
public static class TestDefs
{
    public static readonly FighterDef Warrior = new("test-warrior", Fix.Ratio(3, 10));

    /// <summary>Two ranks, 1 m apart sideways, 1.5 m between ranks: round numbers for exact assertions.</summary>
    public static readonly FormationDef Line = new("test-line", Fix.One, Fix.Ratio(3, 2), ranks: 2, width: 0);

    /// <summary>Two files wide, ranks follow from the squad size.</summary>
    public static readonly FormationDef Column = new("test-column", Fix.One, Fix.Ratio(3, 2), ranks: 0, width: 2);

    public static readonly MapDef Map = new(Fix.FromInt(60), Fix.FromInt(40));

    public static SquadSpawnDef Squad(string name, int side, int count, FixVec2 position, int facingDegrees,
        FormationDef? formation = null) =>
        new(name, side, Warrior, count, formation ?? Line, position, Angle.FromDegrees(facingDegrees));

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

    /// <summary>A player line facing north and an enemy column facing south.</summary>
    public static ScenarioDef TwoSquads() => new("test-two-squads", "test", Map,
    [
        Squad("Line", 0, 8, FixVec2.FromInts(20, 10), 90),
        Squad("Column", 1, 6, FixVec2.FromInts(30, 30), 270, Column),
    ]);
}
