using System.Linq;
using Knez.Game.Input;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using NUnit.Framework;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.Tests
{
    /// <summary>Picking squads and building move orders from mouse gestures (M2 step 5), on drill.json.</summary>
    public class InputTests
    {
        private static BattleSimulation Drill() =>
            new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/drill.json"), 1));

        [Test]
        public void ClickOnAMan_PicksHisSquad_OnlyYourSide_EmptyGroundNothing()
        {
            var sim = Drill();
            var line = sim.State.Squads.First(s => s.Name == "Hird line");
            var enemy = sim.State.Squads.First(s => s.SideId == 1);
            Vector2 onLine = sim.State.FindFighter(line.Members[0])!.Position.ToVector2();
            Vector2 onEnemy = sim.State.FindFighter(enemy.Members[0])!.Position.ToVector2();
            Assert.That(SquadPicker.At(sim.State, onLine + new Vector2(0.2f, 0), 0), Is.EqualTo(line.Id));
            Assert.That(SquadPicker.At(sim.State, onEnemy, 0), Is.EqualTo(EntityId.None));
            Assert.That(SquadPicker.At(sim.State, new Vector2(1, 1), 0), Is.EqualTo(EntityId.None));
        }

        [Test]
        public void BoxOverTheField_PicksAllYourSquads_InIdOrder()
        {
            var sim = Drill();
            var picked = SquadPicker.InBox(sim.State, new Rect(0, 0, 60, 40), 0);
            var mine = sim.State.Squads.Where(s => s.SideId == 0).Select(s => s.Id).ToArray();
            Assert.That(picked, Is.EqualTo(mine));
        }

        [Test]
        public void Click_MovesThereFacingTheMarch_Drag_FacesTheDrag()
        {
            var sim = Drill();
            var line = sim.State.Squads.First(s => s.Name == "Hird line");
            var click = OrderBuilder.Moves(sim.State, new[] { line.Id }, new Vector2(30, 5), new Vector2(30.1f, 5), 0).Cast<MoveSquadCommand>().Single();
            var drag = OrderBuilder.Moves(sim.State, new[] { line.Id }, new Vector2(30, 5), new Vector2(35, 5), 0).Cast<MoveSquadCommand>().Single();
            Assert.That(click.Facing, Is.Null);
            Assert.That(click.Target, Is.EqualTo(new Vector2(30, 5).ToFixVec2()));
            Assert.That(drag.Facing, Is.EqualTo(Angle.FromDegrees(0)), "dragged east: face east");
        }

        [Test]
        public void TwoSquadsOrderedTogether_StandSideBySide_ThenMarchThere()
        {
            var sim = Drill();
            sim.StepTicks(800); // let the scenario's own script finish first
            var mine = sim.State.Squads.Where(s => s.SideId == 0).Select(s => s.Id).ToArray();
            var moves = OrderBuilder.Moves(sim.State, mine, new Vector2(30, 6), new Vector2(30, 10), 0).Cast<MoveSquadCommand>().ToArray();
            float apart = Vector2.Distance(moves[0].Target.ToVector2(), moves[1].Target.ToVector2());
            Assert.That(moves[0].Target.Y, Is.EqualTo(moves[1].Target.Y), "facing north: side by side along east–west");
            Assert.That(apart, Is.GreaterThan(OrderBuilder.Gap + 3f), "fronts plus the gap apart");

            foreach (var move in moves)
            {
                sim.Enqueue(move);
            }

            sim.StepTicks(1200);
            foreach (var move in moves)
            {
                var squad = sim.State.FindSquad(move.SquadId)!;
                Assert.That(squad.Order.Kind, Is.EqualTo(SquadOrderKind.None), squad.Name + " arrived");
                Assert.That(squad.Anchor, Is.EqualTo(move.Target));
                Assert.That(squad.Facing, Is.EqualTo(Angle.FromDegrees(90)));
            }
        }
    }
}
