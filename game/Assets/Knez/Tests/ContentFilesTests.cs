using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Orders;
using NUnit.Framework;

namespace Knez.Game.Tests
{
    public class ContentFilesTests
    {
        [Test]
        public void Drill_LoadsFromTheRepoContent_AndRunsToTheEndInUnity()
        {
            var scenario = ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/drill.json");
            var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
            sim.StepTicks(1000);
            Assert.That(scenario.Id, Is.EqualTo("drill"));
            Assert.That(sim.IsFaulted, Is.False);
            foreach (var squad in sim.State.Squads)
            {
                Assert.That(squad.Order.Kind, Is.EqualTo(SquadOrderKind.None), $"{squad.Name} finished its orders");
            }
        }

        [Test]
        public void MissingScenario_IsAFileNotFound()
        {
            Assert.Throws<System.IO.FileNotFoundException>(
                () => ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/nope.json"));
        }
    }
}
