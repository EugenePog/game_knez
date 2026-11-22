using System.Linq;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using NUnit.Framework;

namespace Knez.Game.Tests
{
    /// <summary>The HUD's words (M2 step 6): squad states and refused orders.</summary>
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
    }
}
