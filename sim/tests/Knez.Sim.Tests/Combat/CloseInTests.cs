using Knez.Sim.Commands;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Combat;

/// <summary>
/// Closing in (battle-sim.md §11, asked for 2026-11-23): in a squad standing in melee, men not yet fighting step toward
/// the nearest enemy within <see cref="MovementDef.CloseInRange"/> until they reach him, so more than the first pair fight.
/// </summary>
[TestFixture]
public class CloseInTests
{
    /// <summary>Two lines of 8 meet slightly offset, so only a few men touch at first. Harmless combat: nobody falls.</summary>
    private static BattleSimulation Offset()
    {
        var scenario = new ScenarioDef("test-offset", "test", TestDefs.Map,
        [
            TestDefs.Squad("Blue", 0, 8, FixVec2.FromInts(28, 12), 90),
            TestDefs.Squad("Red", 1, 8, FixVec2.FromInts(31, 20), 270),
        ], combat: TestDefs.Harmless);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.Enqueue(new MoveSquadCommand(1, sim.State.Squads[1].Id, FixVec2.FromInts(31, 4), Angle.FromDegrees(270)));
        return sim;
    }

    [Test]
    public void SquadsInMelee_MenNotYetFighting_CloseIn()
    {
        var sim = Offset();
        int firstContact = -1;
        int engagedAtContact = 0;
        for (int t = 0; t < 30 * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            int engaged = sim.State.Fighters.Count(f => f.IsEngaged);
            if (firstContact < 0 && engaged > 0)
            {
                firstContact = sim.State.Tick;
                engagedAtContact = engaged;
            }
        }

        int blue = sim.State.Fighters.Count(f => f.SideId == 0 && f.IsEngaged);
        int red = sim.State.Fighters.Count(f => f.SideId == 1 && f.IsEngaged);
        Assert.Multiple(() =>
        {
            Assert.That(firstContact, Is.GreaterThan(0), "the lines met");
            Assert.That(blue, Is.GreaterThanOrEqualTo(4), $"Blue men fighting (at first contact {engagedAtContact} on both sides)");
            Assert.That(red, Is.GreaterThanOrEqualTo(4), "Red men fighting");
            Assert.That(Knez.Sim.Movement.MovementMetrics.OverlappingFighters(sim.State), Is.LessThanOrEqualTo(2), "closing in doesn't pile men into each other");
        });
    }

    [Test]
    public void CloseInRangeZero_OnlyTheFirstContactsFight()
    {
        // The same with closing in off: the comparison that shows the rule does something.
        var scenario = new ScenarioDef("test-offset", "test", TestDefs.Map,
        [
            TestDefs.Squad("Blue", 0, 8, FixVec2.FromInts(28, 12), 90),
            TestDefs.Squad("Red", 1, 8, FixVec2.FromInts(31, 20), 270),
        ], movement: TestDefs.NoCloseIn, combat: TestDefs.Harmless);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.Enqueue(new MoveSquadCommand(1, sim.State.Squads[1].Id, FixVec2.FromInts(31, 4), Angle.FromDegrees(270)));
        sim.StepTicks(30 * BattleState.TicksPerSecond);
        Assert.That(sim.State.Fighters.Count(f => f.SideId == 0 && f.IsEngaged), Is.LessThan(4));
    }

    [Test]
    public void ClosersSettleInsideReach_EngagementDoesNotFlicker()
    {
        // Review finding 2026-11-23: men on the edge of their reach went in and out of it every tick. Count each man's
        // switches between engaged and not over the fight.
        var sim = Offset();
        var last = sim.State.Fighters.ToDictionary(f => f.Id, f => f.IsEngaged);
        var switches = sim.State.Fighters.ToDictionary(f => f.Id, _ => 0);
        for (int t = 0; t < 30 * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            foreach (var f in sim.State.Fighters)
            {
                if (f.IsEngaged != last[f.Id])
                {
                    switches[f.Id]++;
                    last[f.Id] = f.IsEngaged;
                }
            }
        }

        Assert.That(switches.Values.Max(), Is.LessThanOrEqualTo(4), string.Join(", ", switches.Where(p => p.Value > 4).Select(p => $"#{p.Key.Value}: {p.Value}")));
    }

    [Test]
    public void ShieldWall_KeepsItsRanks_DoesNotCloseIn()
    {
        // Decided 2026-11-23: a formation that keeps its ranks doesn't close in — its men stay at their places.
        var wall = new FormationDef("test-wall", Fix.Ratio(3, 4), Fix.One, ranks: 2, width: 0, turnRate: Fix.FromInt(30),
            holdWeight: 3, allyPassSpeed: Fix.Ratio(1, 3), shieldWall: true, keepsRanks: true);
        var scenario = new ScenarioDef("test-wall", "test", TestDefs.Map,
        [
            TestDefs.Squad("Wall", 0, 8, FixVec2.FromInts(30, 12), 90, wall),
            TestDefs.Squad("Red", 1, 10, FixVec2.FromInts(30, 20), 270),
        ], formations: [wall, TestDefs.Line], combat: TestDefs.Harmless);
        var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
        sim.Enqueue(new MoveSquadCommand(1, sim.State.Squads[1].Id, FixVec2.FromInts(30, 4), Angle.FromDegrees(270)));
        double worst = 0;
        int contact = -1;
        int until = 0;
        for (int t = 0; t < 30 * BattleState.TicksPerSecond; t++)
        {
            sim.Step();
            var squad = sim.State.Squads[0];
            if (sim.State.Fighters.Any(f => f.IsRouting))
            {
                break; // flank blows cost morale even when harmless; judge the wall only while it stands
            }

            until = sim.State.Tick;
            if (contact < 0 && sim.State.Fighters.Any(f => f.IsEngaged))
            {
                contact = sim.State.Tick;
            }

            foreach (var id in squad.Members)
            {
                var man = sim.State.FindFighter(id)!;
                worst = Math.Max(worst, Knez.Sim.Movement.Crowding.DistanceToSlot(sim.State, squad, man).Raw / 65536.0);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(sim.State.Fighters.Count(f => f.SideId == 1 && f.IsEngaged), Is.GreaterThanOrEqualTo(4), "the raiders close in on the wall");
            Assert.That(until - contact, Is.GreaterThanOrEqualTo(5 * BattleState.TicksPerSecond), "seconds of contact to judge");
            Assert.That(worst, Is.LessThan(1.0), "the wall's men stay at their places (m)");
        });
    }
}
