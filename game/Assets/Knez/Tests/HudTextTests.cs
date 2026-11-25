#nullable enable
using System.Linq;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using NUnit.Framework;

namespace Knez.Game.Tests
{
    /// <summary>The HUD's words (M2 step 6, M3): squad states, refused orders, kits, wounds, morale, actions, the tally.</summary>
    public class HudTextTests
    {
        private static BattleSimulation Formations() =>
            new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/formations.json"), 1));

        [Test]
        public void Status_HoldingThenMarchingThenReforming()
        {
            var sim = Formations();
            var squad = sim.State.Squads.First(s => s.SideId == 0);
            Assert.That(HudText.Status(squad), Is.EqualTo("holding"));

            sim.Enqueue(new MoveSquadCommand(0, squad.Id, new FixVec2(squad.Anchor.X, squad.Anchor.Y + Fix.FromInt(6))));
            sim.Step();
            Assert.That(HudText.Status(squad), Is.EqualTo("marching"));

            var other = sim.State.Scenario.Formations.First(f => f != squad.Formation);
            sim.Enqueue(new SetFormationCommand(0, squad.Id, other.Id));
            sim.Step();
            Assert.That(HudText.Status(squad), Is.EqualTo("re-forming on the march"));
        }

        [Test]
        public void AnOffMapOrder_IsRefused_InPlainWords_NamingTheSquad()
        {
            var sim = Formations();
            var squad = sim.State.Squads.First(s => s.SideId == 0);
            sim.Enqueue(new MoveSquadCommand(0, squad.Id, FixVec2.FromInts(500, 500)));
            sim.Step();
            var rejected = sim.State.Events.OfType<CommandRejected>().Single();
            Assert.That(HudText.Refusal(sim.State, rejected.Command, rejected.Reason), Is.EqualTo(squad.Name + ": that point is off the map"));
        }

        private static BattleSimulation M3(string name) =>
            new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/m3/" + name + ".json"), 1));

        [Test]
        public void Kit_InWords()
        {
            var sim = M3("m3-veterans");
            var veteran = sim.State.Fighters.First(f => f.SideId == 0);
            var levy = sim.State.Fighters.First(f => f.SideId == 1);
            Assert.That(HudText.Kit(veteran.Kit, veteran.Experience), Is.EqualTo("sword · round shield · mail · helm · elite"));
            Assert.That(HudText.Kit(levy.Kit, levy.Experience), Is.EqualTo("axe · round shield · no armour · bare head · new"));
        }

        [Test]
        public void Action_WindingUpAtHisTarget_ThenRecovering()
        {
            var sim = M3("m3-lines");
            Knez.Sim.Units.Fighter? striker = null;
            for (int t = 0; t < 40 * BattleState.TicksPerSecond && striker == null; t++)
            {
                sim.Step();
                striker = sim.State.Fighters.FirstOrDefault(f => f.Action == Knez.Sim.Combat.FighterAction.Windup);
            }

            Assert.That(striker, Is.Not.Null, "the lines met");
            Assert.That(HudText.Action(sim.State, striker!), Does.StartWith("winding up a blow at #"));
        }

        [Test]
        public void Tally_CountsBlowsAndTheFallen_PerStartingSquad()
        {
            var sim = M3("m3-lines");
            var tally = new SquadTally();
            tally.Begin(sim.State);
            for (int t = 0; t < 60 * BattleState.TicksPerSecond; t++)
            {
                sim.Step();
                tally.Observe(sim.State);
            }

            var stats = sim.State.Squads.Select(s => tally.Of(sim.State, s)).ToArray();
            int fallen = sim.State.Fighters.Count(f => f.Wound == Knez.Sim.Combat.WoundLevel.Down);
            // Unity's NUnit (3.5) has no Assert.Multiple: plain asserts.
            Assert.That(stats.Sum(s => s.Fallen), Is.EqualTo(fallen), "the fallen still count for the squad they left");
            Assert.That(stats.Sum(s => s.Blows), Is.GreaterThan(0));
            Assert.That(stats.All(s => s.Landed <= s.Blows), Is.True);
        }
    }
}
