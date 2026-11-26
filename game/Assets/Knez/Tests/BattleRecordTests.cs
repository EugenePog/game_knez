#nullable enable
using System.Collections.Generic;
using System.Linq;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using NUnit.Framework;
using EntityId = Knez.Sim.EntityId;

namespace Knez.Game.Tests
{
    /// <summary>
    /// M3 step 8: the view's record of the battle — why morale moved (the ledger), the battle in words (the log), who a
    /// squad started with — and the words for a break. Unity's NUnit (3.5) has no Assert.Multiple: plain asserts.
    /// </summary>
    public class BattleRecordTests
    {
        private static BattleSimulation Load(string scenario) =>
            new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), scenario), 1));

        /// <summary>Steps until a squad breaks (or the limit), feeding the record; returns the break.</summary>
        private static SquadBroke? RunToBreak(BattleSimulation sim, BattleRecord record, int seconds)
        {
            for (int t = 0; t < seconds * BattleState.TicksPerSecond; t++)
            {
                sim.Step();
                record.Observe(sim.State);
                SquadBroke? broke = sim.State.Events.OfType<SquadBroke>().FirstOrDefault(e => !e.Reformed);
                if (broke != null)
                {
                    return broke;
                }
            }

            return null;
        }

        [Test]
        public void Break_TheLogSaysWhy_InTheSimsOwnCounts()
        {
            var sim = Load("scenarios/bench/s6-lines.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            SquadBroke? broke = RunToBreak(sim, record, 60);

            Assert.That(broke, Is.Not.Null, "an equal fight breaks a line within a minute");
            string name = sim.State.FindSquad(broke!.Squad)!.Name;
            LogLine line = record.Log.Lines.Last(l => l.Kind == LogKind.Broke);
            Assert.That(broke.Running * 1000, Is.GreaterThan(sim.State.Scenario.Morale.BreakShare * broke.InFight), "the sim's rule");
            string expected = broke.Running == broke.InFight
                ? $"{name} broke. All {broke.InFight} men left in the fight were running."
                : $"{name} broke. {broke.Running} of {broke.InFight} men in the fight were running — more than half — so the other {broke.InFight - broke.Running} ran with them.";
            Assert.That(line.Text, Does.StartWith(expected));
            Assert.That(line.Text, Does.Contain("Morale "));
            Assert.That(line.Text, Does.Contain("mates fell"), "a line breaks after losing men in S6");
            Assert.That(record.Log.WhyBroke(broke.Squad), Is.EqualTo(line.Text.Substring(name.Length + " broke. ".Length)));
            // Only the men swept along by the break lose their "runs" line; a man whose own morale gave way that tick keeps his.
            int setRunning = sim.State.Events.OfType<MoraleStateChanged>().Count(c =>
                c.To == MoraleState.Routing && record.Tally.HomeOf(c.Fighter) == broke.Squad);
            int ranLines = record.Log.Lines.Count(l => l.Tick == broke.Tick && l.Kind == LogKind.Ran && l.Text.Contains("(" + name + ")"));
            Assert.That(ranLines, Is.EqualTo(setRunning - (broke.InFight - broke.Running)));
            Assert.That(record.Log.Lines.First().Kind, Is.EqualTo(LogKind.FirstBlow));
        }

        [Test]
        public void Ledger_EveryMoraleChange_IsInHisAccount_AndAddsUp()
        {
            var sim = Load("scenarios/bench/s6-lines.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            var deltas = new Dictionary<int, int>();
            var reformed = new HashSet<EntityId>();
            for (int t = 0; t < 30 * BattleState.TicksPerSecond; t++)
            {
                sim.Step();
                record.Observe(sim.State);
                foreach (MoraleChanged changed in sim.State.Events.OfType<MoraleChanged>())
                {
                    deltas.TryGetValue(changed.Fighter.Value, out int sum);
                    deltas[changed.Fighter.Value] = sum + changed.Delta;
                }

                foreach (SquadBroke broke in sim.State.Events.OfType<SquadBroke>().Where(e => e.Reformed))
                {
                    reformed.Add(broke.Squad); // its men got new accounts: not comparable with the whole battle's sums
                }
            }

            int checkedMen = 0;
            foreach (var man in sim.State.Fighters.Where(f => f.IsAlive && !reformed.Contains(record.Tally.HomeOf(f))))
            {
                MoraleAccount account = record.Ledger.Of(man);
                deltas.TryGetValue(man.Id.Value, out int moved);
                Assert.That(account.Reasons.Sum(r => r.Average), Is.EqualTo(moved), $"#{man.Id.Value}: every reported change, by reason");
                Assert.That(account.Start + moved + account.Recovered, Is.EqualTo(man.Morale), $"#{man.Id.Value}: start + changes + recovery = now");
                Assert.That(account.Recovered, Is.GreaterThanOrEqualTo(0));
                checkedMen++;
            }

            Assert.That(checkedMen, Is.GreaterThan(0));
            Assert.That(deltas.Count, Is.GreaterThan(0), "morale moved in 30 s of fighting");
        }

        [Test]
        public void Reform_OpensANewAccount_AndTheLogSaysHowManyAreBack()
        {
            // m3-rally: the levy breaks, runs south, rallies and re-forms where it stopped.
            var sim = Load("scenarios/m3/m3-rally.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            SquadBroke? reformed = null;
            for (int t = 0; t < 90 * BattleState.TicksPerSecond && reformed == null; t++)
            {
                sim.Step();
                record.Observe(sim.State);
                reformed = sim.State.Events.OfType<SquadBroke>().FirstOrDefault(e => e.Reformed);
            }

            Assert.That(reformed, Is.Not.Null, "the levy re-forms");
            MoraleAccount account = record.Ledger.Of(sim.State, reformed!.Squad);
            Assert.That(account.Reasons, Is.Empty, "a new account starts when the squad stands again");
            Assert.That(account.Start, Is.EqualTo(account.Now));
            string name = sim.State.FindSquad(reformed.Squad)!.Name;
            Assert.That(record.Log.Lines.Last(l => l.Kind == LogKind.Reformed).Text,
                Is.EqualTo($"{name} re-formed: {reformed.InFight - reformed.Running} of {reformed.InFight} back"));
        }

        [Test]
        public void Ledger_AChangeLaterInTheReformTick_LandsInTheNewStart_NotTwice()
        {
            // A squad re-forms; later in the same tick an enemy squad nearby breaks and heartens its men (+100). The
            // account opens at their morale at the end of the tick, which already holds that +100, so it must not be
            // counted again as a reason (which would leave "recovered" at −100).
            var sim = Load("scenarios/m3/m3-rally.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            var levy = sim.State.Squads.First(s => s.Name == "Levy");
            var man = sim.State.Fighters.First(f => f.SquadId == levy.Id);
            var events = new List<SimEvent>
            {
                new SquadBroke(100, levy.Id, reformed: true, inFight: 6, running: 2),
                new MoraleChanged(100, man.Id, MoraleReason.EnemySquadBroke, 100, man.Morale),
            };
            record.Ledger.Observe(sim.State, events);
            MoraleAccount account = record.Ledger.Of(man);
            Assert.That(account.Reasons, Is.Empty);
            Assert.That(account.Recovered, Is.EqualTo(0));
            Assert.That(account.Start, Is.EqualTo(account.Now));
        }

        [Test]
        public void Composition_GroupsTheMenASquadStartedWith()
        {
            var sim = Load("scenarios/m3/m3-veterans.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            var veterans = sim.State.Squads.First(s => s.SideId == 0);
            var groups = record.Tally.Composition(sim.State, veterans);
            Assert.That(groups.Count, Is.EqualTo(1), "Hrafn, the Retainer leading them, is named apart");
            Assert.That(HudText.Group(groups[0].Sample, groups[0].Count, groups[0].InFight),
                Is.EqualTo("5 experienced: sword · round shield · mail · helm"));
            Assert.That(HudText.Group(groups[0].Sample, 5, 3), Does.StartWith("5 experienced (3 left): "));
            var retainers = record.Tally.RetainersOf(sim.State, veterans.Id);
            Assert.That(retainers.Select(HudText.Retainer).ToArray(), Is.EqualTo(new[] { "Hrafn (loyal, elite)" }));
        }

        [Test]
        public void AbilityUsed_TheLogSaysWhoAndWhat()
        {
            // M3 step 11.
            var sim = Load("scenarios/m3/m3-veterans.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            sim.Enqueue(new Knez.Sim.Commands.UseAbilityCommand(0, hrafn.Id));
            sim.Step();
            record.Observe(sim.State);
            LogLine line = record.Log.Lines.Single(l => l.Kind == LogKind.Ability);
            Assert.That(line.Text, Is.EqualTo("Veterans stand to the death for 5.0 s at Hrafn's word"));
            Assert.That(line.Side, Is.EqualTo(0));
        }

        [Test]
        public void AiDecisions_TheLogSaysWhatTheAiHasEachSquadDo_ASideLeavingOnce()
        {
            // M4 step 2.
            var sim = Load("scenarios/m4/m4-ai.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            for (int t = 0; t < BattleState.TicksPerSecond; t++)
            {
                sim.Step();
                record.Observe(sim.State);
            }

            var lines = record.Log.Lines.Where(l => l.Kind == LogKind.Ai).Select(l => l.Text).ToList();
            Assert.That(lines, Is.EquivalentTo(new[]
            {
                "Raider line attacks Hird line", "Raider levy goes round Hird line's flank", "Raider spears attacks Shield wall",
            }));

            // Two squads of a side leaving: one line for the side.
            var raiders = sim.State.Squads.Where(s => s.SideId == 1).ToList();
            foreach (var squad in raiders.Take(2))
            {
                record.Log.Apply(sim.State, new Knez.Sim.Events.SquadAiChanged(sim.State.Tick, squad.Id, Knez.Sim.Ai.SquadAiState.Engage,
                    Knez.Sim.Ai.SquadAiState.Withdraw, EntityId.None), sweptAlong: false);
            }

            Assert.That(record.Log.Lines.Last().Text, Is.EqualTo("Side 1 leaves the field: too few left to fight on"));
            Assert.That(record.Log.Lines.Count(l => l.Text.StartsWith("Side 1 leaves")), Is.EqualTo(1));
        }

        [Test]
        public void Retainer_CountsForTheSquadHeIsIn_StandingAloneForNone()
        {
            // M3 step 10: he leaves Veterans and stands alone, then joins them again; the tally follows him.
            var sim = Load("scenarios/m3/m3-veterans.json");
            var record = new BattleRecord();
            record.Begin(sim.State);
            var veterans = sim.State.Squads.First(s => s.SideId == 0);
            var hrafn = sim.State.Fighters.Single(f => f.IsRetainer);
            Assert.That(record.Tally.Of(sim.State, veterans).InFight, Is.EqualTo(6), "5 men and Hrafn");
            Assert.That(record.Log.Name(sim.State, hrafn.Id), Is.EqualTo("Hrafn (Veterans)"));

            sim.Enqueue(new Knez.Sim.Commands.DetachRetainerCommand(0, hrafn.Id));
            sim.Step();
            record.Observe(sim.State);
            Assert.That(record.Tally.HomeOf(hrafn), Is.EqualTo(EntityId.None));
            Assert.That(record.Tally.RetainersOf(sim.State, EntityId.None), Is.EqualTo(new[] { hrafn }));
            Assert.That(record.Tally.Of(sim.State, veterans).InFight, Is.EqualTo(5));
            Assert.That(record.Log.Name(sim.State, hrafn.Id), Is.EqualTo("Hrafn"));

            sim.Enqueue(new Knez.Sim.Commands.AttachRetainerCommand(0, hrafn.Id, veterans.Id));
            sim.Step();
            record.Observe(sim.State);
            Assert.That(record.Tally.RetainersOf(sim.State, veterans.Id), Is.EqualTo(new[] { hrafn }));
            Assert.That(record.Tally.Of(sim.State, veterans).InFight, Is.EqualTo(6));
        }

        [Test]
        public void Composition_AMixedSquad_ListsEachExperienceAndKit()
        {
            // M3 step 9: three new spearmen in front, five experienced axemen behind, in one squad.
            var rules = CombatDef.Standard;
            var body = new BodyDef("man", Knez.Sim.Numerics.Fix.Ratio(3, 10), Knez.Sim.Numerics.Fix.Ratio(14, 10),
                Knez.Sim.Numerics.Fix.Ratio(35, 10), Knez.Sim.Numerics.Fix.FromInt(360));
            var line = new FormationDef("line", Knez.Sim.Numerics.Fix.One, Knez.Sim.Numerics.Fix.Ratio(3, 2), ranks: 2, width: 0,
                turnRate: Knez.Sim.Numerics.Fix.FromInt(45), holdWeight: 3, allyPassSpeed: Knez.Sim.Numerics.Fix.Ratio(1, 3));
            var squad = new SquadSpawnDef("Mixed", 0, body,
                new[]
                {
                    new SquadMenDef(3, rules.FindExperience("new")!, rules.FindKit("spear-shield")!),
                    new SquadMenDef(5, rules.FindExperience("experienced")!, rules.FindKit("axe-shield-padded")!),
                },
                line, Knez.Sim.Numerics.FixVec2.FromInts(30, 20), Knez.Sim.Numerics.Angle.FromDegrees(90));
            var state = BattleSetup.Create(new ScenarioDef("mixed", "test", new MapDef(Knez.Sim.Numerics.Fix.FromInt(60),
                Knez.Sim.Numerics.Fix.FromInt(40)), new[] { squad }), 1);
            var record = new BattleRecord();
            record.Begin(state);
            var groups = record.Tally.Composition(state, state.Squads[0]);
            Assert.That(groups.Select(g => HudText.Group(g.Sample, g.Count, g.InFight)).ToArray(), Is.EqualTo(new[]
            {
                "3 new: spear · round shield · no armour · bare head",
                "5 experienced: axe · round shield · padded · cap",
            }));
        }

        [Test]
        public void Account_InWords_LargestFirst_SmallOnesLeftOut()
        {
            var account = new MoraleAccount(8, 750, 170, new List<(MoraleReason, int)>
            {
                (MoraleReason.SquadmateFell, -500),
                (MoraleReason.SquadmateRouted, -60),
                (MoraleReason.Wounded, -38),
                (MoraleReason.StruckFromFlank, -3),
            }, 18);
            Assert.That(HudText.Account(account),
                Is.EqualTo("750 → 170: mates fell −500, mates ran −60, own wounds −38, recovered out of reach +18"));
            Assert.That(HudText.Account(account, 1), Is.EqualTo("750 → 170: mates fell −500, recovered out of reach +18"));
        }

        [Test]
        public void WhyBroke_InWords()
        {
            var account = new MoraleAccount(7, 750, 170, new List<(MoraleReason, int)> { (MoraleReason.SquadmateFell, -250) }, 0);
            var broke = new SquadBroke(480, new EntityId(1), reformed: false, inFight: 7, running: 5);
            Assert.That(HudText.WhyBroke(MoraleDef.Standard, broke, 1, 8, account),
                Is.EqualTo("5 of 7 men in the fight were running — more than half — so the other 2 ran with them. 1 of 8 down. Morale 750 → 170: mates fell −250."));
            var all = new SquadBroke(480, new EntityId(1), reformed: false, inFight: 6, running: 6);
            Assert.That(HudText.WhyBroke(MoraleDef.Standard, all, 2, 8, account),
                Is.EqualTo("All 6 men left in the fight were running. 2 of 8 down. Morale 750 → 170: mates fell −250."));
            Assert.That(HudText.Seconds(481), Is.EqualTo("24.1 s"), "a point, whatever the locale");
        }
    }
}
