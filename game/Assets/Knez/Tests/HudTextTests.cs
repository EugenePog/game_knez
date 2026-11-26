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
    /// <summary>The HUD's words (M2 step 6, M3): squad states, refused orders, kits, wounds, morale, actions, the tally,
    /// Retainers (M3 step 10).</summary>
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
            var veteran = sim.State.Fighters.First(f => f.SideId == 0 && !f.IsRetainer);
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            var levy = sim.State.Fighters.First(f => f.SideId == 1);
            Assert.That(HudText.Kit(veteran.Kit, veteran.Experience), Is.EqualTo("sword · round shield · mail · helm · experienced"));
            Assert.That(HudText.Kit(hrafn.Kit, hrafn.Experience), Is.EqualTo("sword · round shield · mail · helm · elite"));
            Assert.That(HudText.Kit(levy.Kit, levy.Experience), Is.EqualTo("axe · round shield · no armour · bare head · new"));
        }

        [Test]
        public void Retainer_InWords_NameTraitLeadershipAndHisLoneOrder()
        {
            // M3 step 10.
            var sim = M3("m3-veterans");
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            Assert.That(HudText.Retainer(hrafn), Is.EqualTo("Hrafn (loyal, elite)"));
            Assert.That(HudText.Name(sim.State, hrafn.Id), Is.EqualTo("Hrafn (Veterans)"));
            Assert.That(HudText.Leadership(hrafn.Experience), Is.EqualTo("leads: losses \u221220%, recovers +20%"));
            Assert.That(HudText.Leadership(sim.State.Scenario.Combat.FindExperience("new")!), Is.EqualTo("no leadership"));
            Assert.That(HudText.Reason(Knez.Sim.Morale.MoraleReason.LeaderFell), Is.EqualTo("their leader fell"));

            sim.Enqueue(new DetachRetainerCommand(0, hrafn.Id));
            sim.Enqueue(new MoveRetainerCommand(0, hrafn.Id, FixVec2.FromInts(30, 10)));
            sim.Step();
            Assert.That(HudText.Name(sim.State, hrafn.Id), Is.EqualTo("Hrafn"));
            Assert.That(HudText.LoneOrder(sim.State, hrafn), Is.EqualTo("walking to (30.0, 10.0)"));
            var levy = sim.State.Fighters.First(f => f.SideId == 1);
            sim.Enqueue(new RetainerAttackCommand(0, hrafn.Id, levy.Id));
            sim.Step();
            Assert.That(HudText.LoneOrder(sim.State, hrafn), Is.EqualTo($"going at #{levy.Id.Value} (Levy)"));
        }

        [Test]
        public void Ability_InWords_ReadyThenCoolingDown_TheSquadsEffect_TheRefusals()
        {
            // M3 step 11: Hrafn (loyal) has the Veterans stand to the death.
            var sim = M3("m3-veterans");
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            var veterans = sim.State.Squads.First(s => s.SideId == 0);
            Assert.That(HudText.AbilityState(sim.State, hrafn), Is.EqualTo("stand to the death: ready"));
            Assert.That(HudText.AbilityReady(sim.State, hrafn), Is.True);
            Assert.That(HudText.Effects(sim.State, veterans), Is.Empty);

            sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
            sim.Step();
            Assert.That(HudText.AbilityState(sim.State, hrafn), Is.EqualTo("stand to the death: in 120 s"));
            Assert.That(HudText.AbilityReady(sim.State, hrafn), Is.False);
            Assert.That(HudText.Effects(sim.State, veterans), Is.EqualTo("standing to the death 5.0 s"));

            sim.Enqueue(new UseAbilityCommand(0, hrafn.Id));
            sim.Step();
            var rejected = sim.State.Events.OfType<CommandRejected>().Single();
            Assert.That(HudText.Refusal(sim.State, rejected.Command, rejected.Reason), Is.EqualTo("Hrafn: his ability is still cooling down"));
            Assert.That(HudText.Reason(Knez.Sim.Morale.MoraleReason.BattleCry), Is.EqualTo("a battle cry"));
            Assert.That(HudText.Reason(Knez.Sim.Morale.MoraleReason.EnemyBattleCry), Is.EqualTo("an enemy's battle cry"));
            Assert.That(HudText.AbilityName(Knez.Sim.Defs.TraitDef.StandardOf("bold").Ability), Is.EqualTo("front-rank rush"));
        }

        [Test]
        public void AttackAndStance_InWords()
        {
            // M4 step 1.
            var sim = Formations();
            var mine = sim.State.Squads.First(s => s.SideId == 0);
            var enemy = sim.State.Squads.FirstOrDefault(s => s.SideId == 1);
            Assert.That(HudText.Stance(mine.Stance), Is.EqualTo("hold"));
            Assert.That(HudText.Fight(sim.State, mine), Is.Empty);
            sim.Enqueue(new SetStanceCommand(0, mine.Id, Knez.Sim.Orders.SquadStance.Pursue));
            sim.Step();
            Assert.That(HudText.Stance(mine.Stance), Is.EqualTo("pursue"));
            sim.Enqueue(new AttackSquadCommand(0, mine.Id, mine.Id));
            sim.Step();
            var rejected = sim.State.Events.OfType<CommandRejected>().Single();
            Assert.That(HudText.Refusal(sim.State, rejected.Command, rejected.Reason), Is.EqualTo(mine.Name + ": that squad is yours"));
            if (enemy != null)
            {
                sim.Enqueue(new AttackSquadCommand(0, mine.Id, enemy.Id));
                sim.Step();
                Assert.That(HudText.Fight(sim.State, mine), Is.EqualTo("attacking " + enemy.Name));
            }
        }

        [Test]
        public void Ai_InWords_WhatItHasEachSquadDoing()
        {
            // M4 step 2: in m4-ai (seed 1) the raiders decide within their first second.
            var sim = new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/m4/m4-ai.json"), 1));
            var levy = sim.State.Squads.Single(s => s.Name == "Raider levy");
            var mine = sim.State.Squads.Single(s => s.Name == "Hird line");
            Assert.That(HudText.Ai(sim.State, levy), Is.Empty, "no decision yet");
            for (int t = 0; t < BattleState.TicksPerSecond; t++)
            {
                sim.Step();
            }

            Assert.That(HudText.Ai(sim.State, levy), Is.EqualTo("AI: going round Hird line's flank"));
            Assert.That(HudText.Ai(sim.State, sim.State.Squads.Single(s => s.Name == "Raider line")), Is.EqualTo("AI: attacking Hird line"));
            Assert.That(HudText.Ai(sim.State, mine), Is.Empty, "the player's own squads have no AI");

            sim.Enqueue(new SetStanceCommand(1, levy.Id, Knez.Sim.Orders.SquadStance.Hold));
            sim.Step();
            var rejected = sim.State.Events.OfType<CommandRejected>().Single();
            Assert.That(HudText.Refusal(sim.State, rejected.Command, rejected.Reason), Is.EqualTo("Raider levy: the AI commands that side"));
        }

        [Test]
        public void ARetainersRefusedOrder_NamesHim_InPlainWords()
        {
            var sim = M3("m3-veterans");
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            var mate = sim.State.Fighters.First(f => f.SideId == 0 && !f.IsRetainer);
            var levy = sim.State.Squads.Single(s => s.SideId == 1);
            string RefusalOf(ICommand command)
            {
                sim.Enqueue(command);
                sim.Step();
                var rejected = sim.State.Events.OfType<CommandRejected>().Single();
                return HudText.Refusal(sim.State, rejected.Command, rejected.Reason);
            }

            Assert.That(RefusalOf(new MoveRetainerCommand(0, hrafn.Id, FixVec2.FromInts(30, 10))),
                Is.EqualTo("Hrafn: he is in a squad: take him out of it first (X)"));
            Assert.That(RefusalOf(new AttachRetainerCommand(0, hrafn.Id, hrafn.SquadId)), Is.EqualTo("Hrafn: he is in that squad already"));
            Assert.That(RefusalOf(new AttachRetainerCommand(0, hrafn.Id, levy.Id)), Is.EqualTo("Hrafn: that squad isn't yours"));
            sim.Enqueue(new DetachRetainerCommand(0, hrafn.Id));
            sim.Step();
            Assert.That(RefusalOf(new DetachRetainerCommand(0, hrafn.Id)), Is.EqualTo("Hrafn: he isn't in a squad"));
            Assert.That(RefusalOf(new RetainerAttackCommand(0, hrafn.Id, mate.Id)), Is.EqualTo("Hrafn: that man is on his side"));
            Assert.That(RefusalOf(new DetachRetainerCommand(1, hrafn.Id)), Is.EqualTo("Hrafn: not your Retainer"));
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
