using Knez.Sim.Cli;
using Knez.Sim.Commands;
using Knez.Sim.Content;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;
using Knez.Sim.Units;

namespace Knez.Sim.Tests.Content;

/// <summary>
/// A battle played with commands, written out as a scenario whose script holds them, replays to the same state
/// (the session log Unity writes, battle-sim.md §8).
/// </summary>
[TestFixture]
[Category("Determinism")]
public class SessionRecorderTests
{
    private const int Ticks = 900;

    [Test]
    public void Number_AnyFix_ParsesBackToTheSameRaw()
    {
        var raws = new List<long> { 0, 1, -1, 32767, 32768, 65535, 65536, -65536, 6554, -6554, 179L * 65536 + 65535 };
        ulong x = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 20000; i++)
        {
            x = (x * 6364136223846793005UL) + 1442695040888963407UL;
            raws.Add((long)(x >> 34) - (1L << 29)); // about ±8192 m with every fraction
        }

        foreach (long raw in raws)
        {
            string text = SessionRecorder.Number(Fix.FromRaw(raw));
            Assert.That(ContentNumbers.TryParseFix(text, out Fix back, out string error), Is.True, $"{raw} → {text}: {error}");
            Assert.That(back.Raw, Is.EqualTo(raw), text);
        }
    }

    [Test]
    public void Degrees_EveryAngle_ReadsBackToTheSameAngle()
    {
        for (int raw = 0; raw < 65536; raw++)
        {
            Angle facing = Angle.FromRaw(raw);
            string text = SessionRecorder.Degrees(facing);
            Assert.That(ContentNumbers.TryParseFix(text, out Fix degrees, out _), Is.True, text);
            if (Angle.FromDegrees(degrees).Raw != facing.Raw)
            {
                Assert.Fail($"angle {raw} → {text}° → {Angle.FromDegrees(degrees).Raw}");
            }
        }
    }

    [Test]
    public void Write_PlayedBattle_ReplaysToTheSameState()
    {
        string file = RepoPaths.Scenario("sandbox/villages");
        var (live, recorder) = Play(file, seed: 3, Orders);

        // Only the applied orders: the off-map move and the wrong-side move were rejected.
        Assert.That(recorder.Orders, Has.Count.EqualTo(7));

        ScenarioDef replayDef = ParseWritten(recorder, file);
        Assert.That(replayDef.Script, Has.Count.EqualTo(7));
        var replay = new BattleSimulation(BattleSetup.Create(replayDef, 3));
        replay.StepTicks(Ticks);

        AssertSameBattle(live, replay);
    }

    [Test]
    public void Write_ScenarioWithAScript_KeepsItsOrdersFirstWithinATick()
    {
        string file = RepoPaths.Scenario("sandbox/villages-assault"); // scripted moves at tick 20
        var (live, recorder) = Play(file, seed: 5, OrdersAtTwenty);

        ScenarioDef original = ScenarioFiles.Load(file);
        ScenarioDef replayDef = ParseWritten(recorder, file);
        Assert.That(replayDef.Script, Has.Count.EqualTo(original.Script.Count + 2));
        Assert.That(replayDef.Script.Take(original.Script.Count).Select(o => o.SquadName),
            Is.EqualTo(original.Script.Select(o => o.SquadName)), "the scenario's own tick-20 orders run first");
        Assert.That(replayDef.Script[^1].FormationId, Is.EqualTo("column"));

        var replay = new BattleSimulation(BattleSetup.Create(replayDef, 5));
        replay.StepTicks(Ticks);
        AssertSameBattle(live, replay);
    }

    [Test]
    public void Write_NoOrders_LeavesTheScenarioAsItWas()
    {
        string file = RepoPaths.Scenario("sandbox/villages");
        var recorder = new SessionRecorder();
        string text = recorder.Write(new SourceText("villages.json", File.ReadAllText(file)), "villages", "Nothing ordered.");

        ScenarioDef original = ScenarioFiles.Load(file);
        ScenarioDef written = ScenarioLoader.Parse(new SourceText("session.json", text), Defs());
        Assert.That(written.Description, Is.EqualTo("Nothing ordered."));
        Assert.That(written.Fingerprint, Is.EqualTo(original.Fingerprint)); // the id is in it, so it's kept here
    }

    [Test]
    public void Load_ScenarioOutsideContent_FindsContentDefsAbove()
    {
        string dir = Directory.CreateTempSubdirectory("knez-session-test-").FullName;
        try
        {
            string defs = Path.Combine(dir, "content", "defs");
            Directory.CreateDirectory(defs);
            foreach (string def in Directory.GetFiles(Path.Combine(RepoPaths.Content, "defs"), "*.json"))
            {
                File.Copy(def, Path.Combine(defs, Path.GetFileName(def)));
            }

            string sessions = Path.Combine(dir, "artifacts", "sessions");
            Directory.CreateDirectory(sessions);
            string session = Path.Combine(sessions, "villages.json");
            File.Copy(RepoPaths.Scenario("sandbox/villages"), session);

            Assert.That(ScenarioFiles.Load(session).Id, Is.EqualTo("villages"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Orders through a battle: moves with odd fractions and facings, a formation change, two rejected ones.</summary>
    private static void Orders(BattleSimulation sim, List<ICommand> enqueue)
    {
        BattleState state = sim.State;
        Squad Named(string name) => state.Squads.First(s => s.Name == name);
        switch (state.Tick)
        {
            case 9:
                enqueue.Add(new MoveSquadCommand(0, Named("Hird line").Id, new FixVec2(Fix.Ratio(1453, 17), Fix.Ratio(1171, 23)), Angle.FromRaw(1234)));
                enqueue.Add(new MoveSquadCommand(0, Named("Shield wall").Id, new FixVec2(Fix.FromRaw(5_000_001), Fix.FromRaw(3_000_007))));
                enqueue.Add(new MoveSquadCommand(0, Named("Spearmen").Id, new FixVec2(Fix.FromInt(500), Fix.FromInt(10)))); // off the map
                enqueue.Add(new MoveSquadCommand(0, Named("Raider line").Id, new FixVec2(Fix.FromInt(90), Fix.FromInt(50)))); // not ours
                break;
            case 40:
                enqueue.Add(new SetFormationCommand(0, Named("Veterans").Id, "shieldwall"));
                enqueue.Add(new MoveSquadCommand(0, Named("Veterans").Id, new FixVec2(Fix.Ratio(2001, 20), Fix.Ratio(997, 20)), Angle.FromRaw(65000)));
                break;
            case 300:
                enqueue.Add(new MoveSquadCommand(0, Named("Hird line").Id, new FixVec2(Fix.FromInt(140), Fix.Ratio(1001, 19))));
                enqueue.Add(new MoveSquadCommand(1, Named("Raider levy").Id, new FixVec2(Fix.FromInt(120), Fix.FromInt(40)), Angle.FromRaw(32768)));
                enqueue.Add(new MoveSquadCommand(0, Named("Levy").Id, new FixVec2(Fix.FromInt(130), Fix.FromInt(45))));
                break;
        }
    }

    private static void OrdersAtTwenty(BattleSimulation sim, List<ICommand> enqueue)
    {
        BattleState state = sim.State;
        if (state.Tick == 19)
        {
            Squad levy = state.Squads.First(s => s.Name == "Levy");
            enqueue.Add(new MoveSquadCommand(0, levy.Id, new FixVec2(Fix.Ratio(2003, 20), Fix.FromInt(30)), Angle.FromRaw(7000)));
            enqueue.Add(new SetFormationCommand(0, levy.Id, "column"));
        }
    }

    private static (BattleSimulation Live, SessionRecorder Recorder) Play(string file, ulong seed,
        Action<BattleSimulation, List<ICommand>> orders)
    {
        var sim = new BattleSimulation(BattleSetup.Create(ScenarioFiles.Load(file), seed));
        var recorder = new SessionRecorder();
        var enqueue = new List<ICommand>();
        for (int i = 0; i < Ticks; i++)
        {
            enqueue.Clear();
            orders(sim, enqueue);
            foreach (ICommand command in enqueue)
            {
                sim.Enqueue(command);
                recorder.Enqueued(sim.State, command);
            }

            sim.Step();
            recorder.Stepped(sim.State);
        }

        return (sim, recorder);
    }

    private static ScenarioDef ParseWritten(SessionRecorder recorder, string file)
    {
        string text = recorder.Write(new SourceText(Path.GetFileName(file), File.ReadAllText(file)), "session", "Recorded.");
        return ScenarioLoader.Parse(new SourceText("session.json", text), Defs());
    }

    private static DefLibrary Defs() =>
        DefLibrary.Parse(Directory.GetFiles(Path.Combine(RepoPaths.Content, "defs"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(f => new SourceText(Path.GetFileName(f), File.ReadAllText(f)))
            .ToArray());

    private static void AssertSameBattle(BattleSimulation live, BattleSimulation replay)
    {
        StateHash a = live.ComputeHash();
        StateHash b = replay.ComputeHash();
        Assert.That(replay.State.Tick, Is.EqualTo(live.State.Tick));
        // Core differs on purpose: it hashes the scenario, whose id and script are new. Everything that moves matches.
        Assert.That(b.Fighters, Is.EqualTo(a.Fighters), "fighters");
        Assert.That(b.Squads, Is.EqualTo(a.Squads), "squads");
        Assert.That(b.Rng, Is.EqualTo(a.Rng), "rng");
        Assert.That(replay.State.Winner, Is.EqualTo(live.State.Winner), "winner");
        Assert.That(replay.State.NextEntityIdValue, Is.EqualTo(live.State.NextEntityIdValue), "next id");
    }
}
