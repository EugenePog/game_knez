using System.Linq;
using Knez.Game.Input;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Commands;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;
using NUnit.Framework;
using UnityEngine;
using EntityId = Knez.Sim.EntityId; // Unity 6 has a UnityEngine.EntityId too

namespace Knez.Game.Tests
{
    /// <summary>Picking squads and building move orders from mouse gestures (M2 step 5), on drill.json; a Retainer's drag
    /// and orders (M3 step 10), on the sandbox's villages.json.</summary>
    public class InputTests
    {
        private static BattleSimulation Villages() =>
            new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/sandbox/villages.json"), 1));

        private static Fighter Retainer(BattleSimulation sim, string name) => sim.State.Fighters.Single(f => f.Retainer?.Name == name);

        [Test]
        public void PressOnARetainer_PicksHim_NotTheEnemysNorAnOrdinaryMan()
        {
            var sim = Villages();
            var hrafn = Retainer(sim, "Hrafn");
            var ulfr = Retainer(sim, "Ulfr");
            Vector2 at = hrafn.Position.ToVector2();
            var farthest = sim.State.Fighters.Where(f => f.SquadId == hrafn.SquadId && !f.IsRetainer)
                .OrderByDescending(f => Vector2.Distance(f.Position.ToVector2(), at)).First();
            Assert.That(RetainerOrders.At(sim.State, at + new Vector2(0.2f, 0), 0), Is.EqualTo(hrafn.Id));
            Assert.That(RetainerOrders.At(sim.State, farthest.Position.ToVector2(), 0), Is.EqualTo(EntityId.None));
            Assert.That(RetainerOrders.At(sim.State, ulfr.Position.ToVector2(), 0), Is.EqualTo(EntityId.None), "not side 0's");
            Assert.That(RetainerOrders.At(sim.State, ulfr.Position.ToVector2(), 1), Is.EqualTo(ulfr.Id));
        }

        [Test]
        public void DragOntoAnotherSquad_JoinsIt_OntoHisOwn_Nothing_OntoGround_LeavesAndWalks()
        {
            var sim = Villages();
            var hrafn = Retainer(sim, "Hrafn");
            var hird = sim.State.Squads.Single(s => s.Name == "Hird line");
            Vector2 from = hrafn.Position.ToVector2();
            Vector2 onHird = sim.State.FindFighter(hird.Members[0])!.Position.ToVector2();
            Vector2 onOwn = sim.State.FindFighter(sim.State.FindSquad(hrafn.SquadId)!.Members.First(m => m != hrafn.Id))!.Position.ToVector2();
            Vector2 ground = new Vector2(70, 80);

            var join = RetainerOrders.Drop(sim.State, hrafn.Id, from, onHird, 0).Cast<AttachRetainerCommand>().Single();
            Assert.That(join.SquadId, Is.EqualTo(hird.Id));
            Assert.That(RetainerOrders.Drop(sim.State, hrafn.Id, from, onOwn, 0), Is.Empty, "his own squad: he stays");
            Assert.That(RetainerOrders.Drop(sim.State, hrafn.Id, from, from + new Vector2(0.3f, 0), 0), Is.Empty, "too short to mean a drag");

            var walk = RetainerOrders.Drop(sim.State, hrafn.Id, from, ground, 0);
            Assert.That(walk.Select(c => c.GetType()).ToArray(), Is.EqualTo(new[] { typeof(DetachRetainerCommand), typeof(MoveRetainerCommand) }));
            foreach (var command in walk)
            {
                sim.Enqueue(command);
            }

            sim.Step();
            Assert.That(hrafn.SquadId, Is.EqualTo(EntityId.None));
            Assert.That(hrafn.LoneOrder.Kind, Is.EqualTo(LoneOrderKind.Move));
            Assert.That(hrafn.LoneOrder.Target, Is.EqualTo(ground.ToFixVec2()));
            Assert.That(sim.State.Events.OfType<Knez.Sim.Events.CommandRejected>(), Is.Empty);

            // Alone now: dropped on open ground again he just walks, no leaving first.
            Assert.That(RetainerOrders.Drop(sim.State, hrafn.Id, hrafn.Position.ToVector2(), new Vector2(60, 80), 0).Single(), Is.TypeOf<MoveRetainerCommand>());
        }

        [Test]
        public void RightClickWithHimSelected_EnemyGoesAtHim_GroundWalksFacingTheDrag_SquadJoins()
        {
            var sim = Villages();
            var hrafn = Retainer(sim, "Hrafn");
            var enemy = sim.State.Fighters.First(f => f.SideId == 1 && !f.IsRetainer);
            var hird = sim.State.Squads.Single(s => s.Name == "Hird line");
            Vector2 onEnemy = enemy.Position.ToVector2();

            var attack = RetainerOrders.Order(sim.State, hrafn.Id, onEnemy, onEnemy, 0);
            Assert.That(attack.Select(c => c.GetType()).ToArray(), Is.EqualTo(new[] { typeof(DetachRetainerCommand), typeof(RetainerAttackCommand) }));
            Assert.That(((RetainerAttackCommand)attack[1]).EnemyId, Is.EqualTo(enemy.Id));

            var walk = RetainerOrders.Order(sim.State, hrafn.Id, new Vector2(70, 80), new Vector2(70, 85), 0);
            var move = (MoveRetainerCommand)walk[1];
            Assert.That(move.Target, Is.EqualTo(new Vector2(70, 80).ToFixVec2()));
            Assert.That(move.Facing, Is.EqualTo(Angle.FromDegrees(90)), "dragged north: face north");
            var click = (MoveRetainerCommand)RetainerOrders.Order(sim.State, hrafn.Id, new Vector2(70, 80), new Vector2(70.1f, 80), 0)[1];
            Assert.That(click.Facing, Is.Null);

            Vector2 onHird = sim.State.FindFighter(hird.Members[0])!.Position.ToVector2();
            Assert.That(RetainerOrders.Order(sim.State, hrafn.Id, onHird, onHird, 0).Cast<AttachRetainerCommand>().Single().SquadId, Is.EqualTo(hird.Id));
        }

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
