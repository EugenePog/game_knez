using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Knez.Sim.Events;
using Knez.Sim.Movement;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;
using Knez.Sim.Units;

namespace Knez.Sim.Cli;

/// <summary>
/// Writes a frames file (format <c>knez-frames</c> v1, specified in battle-sim.md §17 — keep that the only copy) for
/// <c>tools/replay-viewer/</c>: a header, static tables of squads and fighters, then one frame per captured tick with
/// positions, facings, anchors, the state hash and that tick's events. View-side code: converting <see cref="Fix"/> to
/// floating point here is fine, the sim never reads it back. Reads only the public surface of the state, so recording
/// can't change the battle. Does not own <c>output</c>: the caller closes the stream.
/// </summary>
public sealed class FrameRecorder : IDisposable
{
    public const string Format = "knez-frames";
    public const int Version = 1;

    /// <summary>Event types whose fields <see cref="WriteEvent"/> writes; any other type is written as its name only.
    /// A test fails when a <see cref="SimEvent"/> subclass is missing here.</summary>
    public static readonly IReadOnlyList<Type> DescribedEventTypes = [typeof(CommandRejected), typeof(BlowStruck), typeof(Wounded),
        typeof(MoraleChanged), typeof(MoraleStateChanged), typeof(SquadBroke), typeof(BattleEnded)];

    /// <summary>Flush to the stream once this much is buffered, so long battles don't sit in memory.</summary>
    private const int FlushThresholdBytes = 64 * 1024;

    private readonly Utf8JsonWriter _json;
    private bool _completed;

    /// <summary>Writes the header and the static tables of <paramref name="initial"/>; frames follow with <see cref="Capture"/>.</summary>
    public FrameRecorder(Stream output, BattleState initial)
    {
        // Relaxed escaping keeps names like "Ярослав" readable; the file is data, never embedded in HTML.
        _json = new Utf8JsonWriter(output, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        _json.WriteStartObject();
        _json.WriteString("format", Format);
        _json.WriteNumber("version", Version);
        _json.WriteString("scenario", initial.Scenario.Id);
        _json.WriteString("description", initial.Scenario.Description);
        _json.WriteString("seed", initial.Seed.ToString(CultureInfo.InvariantCulture)); // a string: JS numbers lose ulongs above 2^53
        _json.WriteNumber("ticksPerSecond", BattleState.TicksPerSecond);

        _json.WriteStartObject("map");
        _json.WriteNumber("width", Metres(initial.Scenario.Map.Width));
        _json.WriteNumber("height", Metres(initial.Scenario.Map.Height));
        _json.WriteStartArray("walls");
        foreach (var wall in initial.Scenario.Map.Walls)
        {
            _json.WriteStartArray();
            _json.WriteNumberValue(Metres(wall.Min.X));
            _json.WriteNumberValue(Metres(wall.Min.Y));
            _json.WriteNumberValue(Metres(wall.Max.X));
            _json.WriteNumberValue(Metres(wall.Max.Y));
            _json.WriteEndArray();
        }

        _json.WriteEndArray();
        _json.WriteEndObject();

        _json.WriteStartArray("squads");
        foreach (Squad squad in initial.Squads)
        {
            _json.WriteStartObject();
            _json.WriteNumber("id", squad.Id.Value);
            _json.WriteString("name", squad.Name);
            _json.WriteNumber("side", squad.SideId);
            _json.WriteString("formation", squad.Formation.Id);
            _json.WriteEndObject();
        }

        _json.WriteEndArray();

        _json.WriteStartArray("fighters");
        foreach (Fighter fighter in initial.Fighters)
        {
            _json.WriteStartObject();
            _json.WriteNumber("id", fighter.Id.Value);
            _json.WriteNumber("squad", fighter.SquadId.Value);
            _json.WriteNumber("slot", fighter.SlotIndex);
            _json.WriteNumber("side", fighter.SideId);
            _json.WriteString("def", fighter.Body.Id); // the body since M3 step 9 (it was the fighter type)
            _json.WriteNumber("radius", Metres(fighter.Radius));
            _json.WriteString("experience", fighter.Experience.Id);
            _json.WriteString("kit", fighter.Kit.Id);
            _json.WriteEndObject();
        }

        _json.WriteEndArray();
        _json.WriteStartArray("frames");
    }

    /// <summary>Frames written so far.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Appends a frame for the state as it is now (call after setup and after every step).</summary>
    public void Capture(BattleState state)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The frames file is already complete.");
        }

        _json.WriteStartObject();
        _json.WriteNumber("tick", state.Tick);
        _json.WriteString("hash", StateHasher.Compute(state).Total.ToString("x16", CultureInfo.InvariantCulture));

        _json.WriteStartArray("squads");
        foreach (Squad squad in state.Squads)
        {
            _json.WriteStartArray();
            _json.WriteNumberValue(squad.Id.Value);
            _json.WriteNumberValue(Metres(squad.Anchor.X));
            _json.WriteNumberValue(Metres(squad.Anchor.Y));
            _json.WriteNumberValue(Degrees(squad.Facing));
            bool moving = squad.Order.Kind == SquadOrderKind.Move;
            _json.WriteNumberValue(moving ? 1 : 0);
            _json.WriteNumberValue(moving ? Metres(squad.Order.Target.X) : 0);
            _json.WriteNumberValue(moving ? Metres(squad.Order.Target.Y) : 0);
            _json.WriteNumberValue(moving ? Degrees(squad.Order.Facing) : 0);
            _json.WriteStringValue(squad.Formation.Id); // changes with SetFormation (step 7)
            _json.WriteNumberValue(squad.Files); // narrowed for a gap, 0 = its own width
            _json.WriteNumberValue(squad.Broken ? 1 : 0); // M3: its men are running
            _json.WriteEndArray();
        }

        _json.WriteEndArray();

        // Planned routes still ahead: [squad, x, y, x, y, …] from the waypoint the anchor heads for to the target.
        _json.WriteStartArray("paths");
        foreach (Squad squad in state.Squads)
        {
            if (squad.Order.Kind != SquadOrderKind.Move || squad.Path.Count == 0)
            {
                continue;
            }

            _json.WriteStartArray();
            _json.WriteNumberValue(squad.Id.Value);
            for (int p = squad.PathIndex; p < squad.Path.Count; p++)
            {
                _json.WriteNumberValue(Metres(squad.Path[p].X));
                _json.WriteNumberValue(Metres(squad.Path[p].Y));
            }

            _json.WriteEndArray();
        }

        _json.WriteEndArray();

        _json.WriteStartArray("fighters");
        foreach (Fighter fighter in state.Fighters)
        {
            _json.WriteStartArray();
            _json.WriteNumberValue(fighter.Id.Value);
            _json.WriteNumberValue(Metres(fighter.Position.X));
            _json.WriteNumberValue(Metres(fighter.Position.Y));
            _json.WriteNumberValue(Degrees(fighter.Facing));
            _json.WriteNumberValue(fighter.IsAlive ? 1 : 0);
            _json.WriteNumberValue(MovementMetrics.HoldsGround(state, fighter) ? 1 : 0);
            _json.WriteNumberValue(fighter.PassingThrough ? 1 : 0);
            _json.WriteNumberValue(fighter.SquadId.Value); // 0 once he falls (step 7)
            _json.WriteNumberValue(fighter.SlotIndex); // changes as ranks close and squads re-form
            _json.WriteNumberValue(fighter.Morale); // M3: 0..1000
            _json.WriteStringValue(fighter.MoraleState.ToString().ToLowerInvariant());
            _json.WriteStringValue(fighter.Wound.ToString().ToLowerInvariant());
            _json.WriteEndArray();
        }

        _json.WriteEndArray();

        _json.WriteStartArray("events");
        foreach (SimEvent simEvent in state.Events)
        {
            WriteEvent(simEvent);
        }

        _json.WriteEndArray();
        _json.WriteEndObject();
        FrameCount++;
        if (_json.BytesPending > FlushThresholdBytes)
        {
            _json.Flush();
        }
    }

    /// <summary>Closes the frames array and the document and flushes. The file is valid JSON only after this.</summary>
    public void Complete() => Finish(null, 0);

    /// <summary>
    /// Like <see cref="Complete"/> after the simulation threw during <paramref name="tick"/>: the frames so far stay
    /// readable and a top-level <c>"faulted": { "tick", "error" }</c> says what happened. Call it between frames.
    /// </summary>
    public void CompleteFaulted(int tick, string error) => Finish(error, tick);

    private void Finish(string? error, int tick)
    {
        if (_completed)
        {
            return;
        }

        _json.WriteEndArray();
        if (error != null)
        {
            _json.WriteStartObject("faulted");
            _json.WriteNumber("tick", tick);
            _json.WriteString("error", error);
            _json.WriteEndObject();
        }

        _json.WriteEndObject();
        _json.Flush();
        _completed = true;
    }

    public void Dispose() => _json.Dispose();

    private void WriteEvent(SimEvent simEvent)
    {
        _json.WriteStartObject();
        _json.WriteString("type", simEvent.GetType().Name);
        switch (simEvent)
        {
            case CommandRejected rejected:
                _json.WriteString("command", rejected.Command.GetType().Name);
                _json.WriteNumber("side", rejected.Command.SideId);
                _json.WriteString("reason", rejected.Reason.Code);
                break;
            case BlowStruck blow:
                _json.WriteNumber("attacker", blow.Attacker.Value);
                _json.WriteNumber("defender", blow.Defender.Value);
                _json.WriteString("sector", blow.Sector.ToString().ToLowerInvariant());
                _json.WriteNumber("chance", blow.Chance);
                _json.WriteBoolean("landed", blow.Landed);
                break;
            case Wounded wounded:
                _json.WriteNumber("fighter", wounded.Fighter.Value);
                _json.WriteNumber("by", wounded.By.Value);
                _json.WriteString("blow", wounded.Blow.ToString().ToLowerInvariant());
                _json.WriteString("now", wounded.Now.ToString().ToLowerInvariant());
                break;
            case MoraleChanged morale:
                _json.WriteNumber("fighter", morale.Fighter.Value);
                _json.WriteString("reason", morale.Reason.ToString());
                _json.WriteNumber("delta", morale.Delta);
                _json.WriteNumber("now", morale.Now);
                break;
            case MoraleStateChanged changed:
                _json.WriteNumber("fighter", changed.Fighter.Value);
                _json.WriteString("from", changed.From.ToString().ToLowerInvariant());
                _json.WriteString("to", changed.To.ToString().ToLowerInvariant());
                break;
            case SquadBroke broke:
                _json.WriteNumber("squad", broke.Squad.Value);
                _json.WriteBoolean("reformed", broke.Reformed);
                _json.WriteNumber("inFight", broke.InFight);
                _json.WriteNumber("running", broke.Running);
                break;
            case BattleEnded ended:
                _json.WriteNumber("winner", ended.Winner);
                break;
        }

        _json.WriteEndObject();
    }

    private static double Metres(Fix value) => Math.Round(value.Raw / (double)Fix.OneRaw, 4);

    private static double Degrees(Angle angle) => Math.Round(angle.Raw * 360.0 / Angle.UnitsPerTurn, 2);
}
