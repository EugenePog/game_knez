using System.Text.Json;
using Knez.Sim.Cli;
using Knez.Sim.Events;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Tests.Support;

namespace Knez.Sim.Tests.Cli;

/// <summary>The frames file the replay viewer plays (battle-sim.md §17): content, accuracy, and no effect on the battle.</summary>
[TestFixture]
public class FrameRecorderTests
{
    private static readonly string Formations = RepoPaths.Scenario("formations");

    private string _dir = "";

    [SetUp]
    public void CreateTempDirectory() => _dir = Directory.CreateTempSubdirectory("knez-frames-test-").FullName;

    [TearDown]
    public void DeleteTempDirectory() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void Run_WithFrames_WritesTheStartAndOneFramePerTick()
    {
        string path = Path.Combine(_dir, "replays", "formations.frames.json"); // the folder doesn't exist yet
        var (exit, stdout, stderr) = Invoke("run", Formations, "--seed", "3", "--ticks", "20", "--frames", path);

        Assert.That(exit, Is.EqualTo(CliApp.ExitOk), stderr);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var frames = root.GetProperty("frames");
        Assert.Multiple(() =>
        {
            Assert.That(stdout, Does.Contain("frames:     " + path + " (21 frames)"));
            Assert.That(root.GetProperty("format").GetString(), Is.EqualTo(FrameRecorder.Format));
            Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(FrameRecorder.Version));
            Assert.That(root.GetProperty("scenario").GetString(), Is.EqualTo("formations"));
            Assert.That(root.GetProperty("seed").GetString(), Is.EqualTo("3"));
            Assert.That(root.GetProperty("ticksPerSecond").GetInt32(), Is.EqualTo(BattleState.TicksPerSecond));
            Assert.That(root.GetProperty("map").GetProperty("width").GetDouble(), Is.EqualTo(60.0));
            Assert.That(root.GetProperty("squads").GetArrayLength(), Is.EqualTo(5));
            Assert.That(root.GetProperty("fighters").GetArrayLength(), Is.EqualTo(35));
            Assert.That(frames.GetArrayLength(), Is.EqualTo(21));
            Assert.That(frames[0].GetProperty("tick").GetInt32(), Is.EqualTo(0));
            Assert.That(frames[20].GetProperty("tick").GetInt32(), Is.EqualTo(20));
            Assert.That(frames.EnumerateArray().Select(f => f.GetProperty("fighters").GetArrayLength()), Has.All.EqualTo(35));
        });
    }

    [Test]
    public void Frames_MatchTheSimulationTickByTick()
    {
        var stream = new MemoryStream();
        var sim = TestBattle.Create(5); // wandering fighters: positions change every tick
        var expected = new List<(int Tick, string Hash, double X, double Y, double Facing, int Holds)>();
        using (var recorder = new FrameRecorder(stream, sim.State))
        {
            for (int i = 0; i <= 10; i++)
            {
                if (i > 0)
                {
                    sim.Step();
                }

                recorder.Capture(sim.State);
                var f = sim.State.Fighters[1];
                expected.Add((sim.State.Tick, sim.ComputeHash().Total.ToString("x16"),
                    f.Position.X.Raw / 65536.0, f.Position.Y.Raw / 65536.0, f.Facing.Raw * 360.0 / 65536,
                    MovementMetrics.HoldsGround(sim.State, f) ? 1 : 0));
            }

            recorder.Complete();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var frames = doc.RootElement.GetProperty("frames");
        for (int i = 0; i < expected.Count; i++)
        {
            var frame = frames[i];
            var fighter = frame.GetProperty("fighters")[1];
            Assert.Multiple(() =>
            {
                Assert.That(frame.GetProperty("tick").GetInt32(), Is.EqualTo(expected[i].Tick));
                Assert.That(frame.GetProperty("hash").GetString(), Is.EqualTo(expected[i].Hash));
                Assert.That(fighter[0].GetInt32(), Is.EqualTo(sim.State.Fighters[1].Id.Value));
                Assert.That(fighter[1].GetDouble(), Is.EqualTo(expected[i].X).Within(0.00005)); // written to 0.1 mm
                Assert.That(fighter[2].GetDouble(), Is.EqualTo(expected[i].Y).Within(0.00005));
                Assert.That(fighter[3].GetDouble(), Is.EqualTo(expected[i].Facing).Within(0.005));
                Assert.That(fighter[4].GetInt32(), Is.EqualTo(1));
                Assert.That(fighter[5].GetInt32(), Is.EqualTo(expected[i].Holds), "holds his ground, as of that tick");
            });
        }
    }

    [Test]
    public void Frames_CarryTheEventsOfTheirTick()
    {
        var stream = new MemoryStream();
        var sim = TestBattle.Create(5, wander: false);
        using (var recorder = new FrameRecorder(stream, sim.State))
        {
            recorder.Capture(sim.State);
            sim.Enqueue(new RejectedCommand());
            sim.Step();
            recorder.Capture(sim.State);
            sim.Step();
            recorder.Capture(sim.State);
            recorder.Complete();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var frames = doc.RootElement.GetProperty("frames");
        var rejected = frames[1].GetProperty("events")[0];
        Assert.Multiple(() =>
        {
            Assert.That(frames[0].GetProperty("events").GetArrayLength(), Is.EqualTo(0));
            Assert.That(rejected.GetProperty("type").GetString(), Is.EqualTo("CommandRejected"));
            Assert.That(rejected.GetProperty("command").GetString(), Is.EqualTo(nameof(RejectedCommand)));
            Assert.That(rejected.GetProperty("side").GetInt32(), Is.EqualTo(0));
            Assert.That(rejected.GetProperty("reason").GetString(), Is.EqualTo("always-rejected"));
            Assert.That(frames[2].GetProperty("events").GetArrayLength(), Is.EqualTo(0), "events last one tick");
        });
    }

    [Test]
    public void Frames_StaticTablesDescribeSquadsAndFighters()
    {
        var stream = new MemoryStream();
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        using (var recorder = new FrameRecorder(stream, state))
        {
            recorder.Capture(state);
            recorder.Complete();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var root = doc.RootElement;
        var squad = root.GetProperty("squads")[1];
        var fighter = root.GetProperty("fighters")[0];
        var anchor = root.GetProperty("frames")[0].GetProperty("squads")[1];
        Assert.Multiple(() =>
        {
            Assert.That(squad.GetProperty("id").GetInt32(), Is.EqualTo(10));
            Assert.That(squad.GetProperty("name").GetString(), Is.EqualTo("Column"));
            Assert.That(squad.GetProperty("side").GetInt32(), Is.EqualTo(1));
            Assert.That(squad.GetProperty("formation").GetString(), Is.EqualTo("test-column"));
            Assert.That(fighter.GetProperty("id").GetInt32(), Is.EqualTo(2));
            Assert.That(fighter.GetProperty("squad").GetInt32(), Is.EqualTo(1));
            Assert.That(fighter.GetProperty("slot").GetInt32(), Is.EqualTo(0));
            Assert.That(fighter.GetProperty("side").GetInt32(), Is.EqualTo(0));
            Assert.That(fighter.GetProperty("def").GetString(), Is.EqualTo("test-warrior"));
            Assert.That(fighter.GetProperty("radius").GetDouble(), Is.EqualTo(0.3).Within(0.0001));
            Assert.That(anchor[0].GetInt32(), Is.EqualTo(10));
            Assert.That(anchor[1].GetDouble(), Is.EqualTo(30.0));
            Assert.That(anchor[2].GetDouble(), Is.EqualTo(30.0));
            Assert.That(anchor[3].GetDouble(), Is.EqualTo(270.0));
            Assert.That(anchor[4].GetInt32(), Is.EqualTo(0), "no order");
        });
    }

    [Test]
    public void Frames_SquadRowsCarryTheMoveOrder()
    {
        var stream = new MemoryStream();
        var sim = new BattleSimulation(BattleSetup.Create(TestDefs.OneLine(), seed: 1));
        sim.Enqueue(new Knez.Sim.Commands.MoveSquadCommand(0, sim.State.Squads[0].Id, FixVec2.FromInts(30, 20), Angle.FromDegrees(180)));
        sim.Step();
        using (var recorder = new FrameRecorder(stream, sim.State))
        {
            recorder.Capture(sim.State);
            recorder.Complete();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var row = doc.RootElement.GetProperty("frames")[0].GetProperty("squads")[0];
        var fighterRow = doc.RootElement.GetProperty("frames")[0].GetProperty("fighters")[0];
        Assert.Multiple(() =>
        {
            Assert.That(fighterRow[5].GetInt32(), Is.EqualTo(0), "his squad has a move order: not holding his ground");
            Assert.That(row.GetArrayLength(), Is.EqualTo(8));
            Assert.That(row[4].GetInt32(), Is.EqualTo(1));
            Assert.That(row[5].GetDouble(), Is.EqualTo(30.0));
            Assert.That(row[6].GetDouble(), Is.EqualTo(20.0));
            Assert.That(row[7].GetDouble(), Is.EqualTo(180.0));
        });
    }

    [Test]
    public void Run_WithAndWithoutFrames_SameFinalHash()
    {
        string path = Path.Combine(_dir, "f.json");
        var (_, withFrames, _) = Invoke("run", Formations, "--ticks", "40", "--json", "--frames", path);
        var (_, without, _) = Invoke("run", Formations, "--ticks", "40", "--json");
        using var a = JsonDocument.Parse(withFrames);
        using var b = JsonDocument.Parse(without);
        Assert.Multiple(() =>
        {
            Assert.That(a.RootElement.GetProperty("finalHash").GetString(), Is.EqualTo(b.RootElement.GetProperty("finalHash").GetString()));
            Assert.That(a.RootElement.GetProperty("frames").GetProperty("file").GetString(), Is.EqualTo(path));
            Assert.That(a.RootElement.GetProperty("frames").GetProperty("count").GetInt32(), Is.EqualTo(41));
            Assert.That(b.RootElement.GetProperty("frames").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    [Category("Determinism")]
    public void Recording_EveryTickOfAMovingBattle_ChangesNoHash()
    {
        var recorded = TestBattle.Create(9);
        var plain = TestBattle.Create(9);
        using var recorder = new FrameRecorder(new MemoryStream(), recorded.State);
        recorder.Capture(recorded.State);
        for (int i = 0; i < 200; i++)
        {
            recorded.Step();
            recorder.Capture(recorded.State);
            plain.Step();
            Assert.That(recorded.ComputeHash(), Is.EqualTo(plain.ComputeHash()), $"tick {plain.State.Tick}");
        }
    }

    [Test]
    public void CompleteFaulted_AfterACrash_LeavesValidJsonWithTheFramesSoFar()
    {
        var stream = new MemoryStream();
        var sim = TestBattle.Create(5, wander: false);
        using (var recorder = new FrameRecorder(stream, sim.State))
        {
            recorder.Capture(sim.State);
            sim.Step();
            recorder.Capture(sim.State);
            sim.Enqueue(new ThrowingCommand());
            var crash = Assert.Throws<InvalidOperationException>(() => sim.Step());
            recorder.CompleteFaulted(sim.State.Tick, crash!.Message);
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("frames").GetArrayLength(), Is.EqualTo(2));
            Assert.That(root.GetProperty("faulted").GetProperty("tick").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("faulted").GetProperty("error").GetString(), Is.EqualTo("boom"));
        });
    }

    [Test]
    public void Capture_DeadFighter_AliveColumnIsZero()
    {
        var stream = new MemoryStream();
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        state.FighterList[3].IsAlive = false;
        using (var recorder = new FrameRecorder(stream, state))
        {
            recorder.Capture(state);
            recorder.Complete();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        var rows = doc.RootElement.GetProperty("frames")[0].GetProperty("fighters");
        Assert.That(Enumerable.Range(0, rows.GetArrayLength()).Select(i => rows[i][4].GetInt32()),
            Is.EqualTo(Enumerable.Range(0, 14).Select(i => i == 3 ? 0 : 1)));
    }

    [Test]
    public void Capture_LongBattle_StreamsToTheOutputInsteadOfBufferingEverything()
    {
        var stream = new MemoryStream();
        var state = BattleSetup.Create(TestDefs.TwoSquads(), seed: 1);
        using var recorder = new FrameRecorder(stream, state);
        for (int i = 0; i < 400; i++)
        {
            recorder.Capture(state);
        }

        Assert.That(stream.Length, Is.GreaterThan(0), "frames are flushed as they accumulate, not only at Complete");
    }

    [Test]
    public void EverySimEventType_HasItsFieldsDescribed()
    {
        var eventTypes = typeof(SimEvent).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(SimEvent)) && !t.IsAbstract);
        Assert.That(FrameRecorder.DescribedEventTypes, Is.SupersetOf(eventTypes),
            "a new SimEvent needs a case in FrameRecorder.WriteEvent (and battle-sim.md §17), then an entry in DescribedEventTypes");
    }

    [Test]
    public void Run_LargestSeed_SurvivesAsTextForTheViewer()
    {
        string path = Path.Combine(_dir, "seed.json");
        var (exit, _, stderr) = Invoke("run", Formations, "--seed", "18446744073709551615", "--ticks", "1", "--frames", path);
        Assert.That(exit, Is.EqualTo(CliApp.ExitOk), stderr);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.That(doc.RootElement.GetProperty("seed").GetString(), Is.EqualTo("18446744073709551615"));
    }

    [Test]
    public void Run_FramesOntoTheScenarioFile_IsRefused()
    {
        var (exit, _, stderr) = Invoke("run", Formations, "--frames", Formations);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitUsage));
            Assert.That(stderr, Does.Contain("would overwrite the scenario file"));
            Assert.That(File.ReadAllText(Formations), Does.Contain("\"id\": \"formations\""), "the scenario is untouched");
        });
    }

    [Test]
    public void Run_FramesPathUnwritable_ExitsWithUsageCodeBeforeSimulating()
    {
        string file = Path.Combine(_dir, "a-file");
        File.WriteAllText(file, "");
        var (exit, stdout, stderr) = Invoke("run", Formations, "--frames", Path.Combine(file, "x.json")); // a file as a folder
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitUsage));
            Assert.That(stderr, Does.Contain("cannot write frames"));
            Assert.That(stdout, Is.Empty);
        });
    }

    [Test]
    public void Run_FramesWithoutPath_IsAUsageError()
    {
        var (exit, _, stderr) = Invoke("run", Formations, "--frames");
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(CliApp.ExitUsage));
            Assert.That(stderr, Does.Contain("--frames needs a file path"));
        });
    }

    private static (int Exit, string Stdout, string Stderr) Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = CliApp.Run(args, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
