using System.Globalization;
using System.Text;
using Knez.Sim.Combat;
using Knez.Sim.Events;
using Knez.Sim.Morale;

namespace Knez.Sim.Cli;

/// <summary>
/// What happened in a run, for people (M3): blows and wounds, squads breaking and re-forming, and the result. Reads each
/// tick's events after the step; view-side, never part of the simulation.
/// </summary>
public sealed class BattleLog
{
    private readonly List<string> _morale = [];

    public int Blows { get; private set; }

    public int Landed { get; private set; }

    public int Light { get; private set; }

    public int Heavy { get; private set; }

    public int Fell { get; private set; }

    public int EndTick { get; private set; } = -1;

    public int Winner { get; private set; } = BattleState.Undecided;

    /// <summary>Squads breaking and re-forming, in order, as "Name broke at 27.1 s".</summary>
    public IReadOnlyList<string> MoraleLines => _morale;

    /// <summary>Records the tick just stepped.</summary>
    public void Observe(BattleState state)
    {
        foreach (SimEvent e in state.Events)
        {
            switch (e)
            {
                case BlowStruck blow:
                    Blows++;
                    Landed += blow.Landed ? 1 : 0;
                    break;
                case Wounded wounded:
                    if (wounded.Now == WoundLevel.Down)
                    {
                        Fell++;
                    }
                    else if (wounded.Now == WoundLevel.Heavy)
                    {
                        Heavy++;
                    }
                    else
                    {
                        Light++;
                    }

                    break;
                case SquadBroke broke:
                    string name = state.FindSquad(broke.Squad)?.Name ?? $"#{broke.Squad.Value}";
                    _morale.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} at {2:0.0} s", name,
                        broke.Reformed ? "re-formed" : "broke", e.Tick / (double)BattleState.TicksPerSecond));
                    break;
                case BattleEnded ended:
                    EndTick = e.Tick;
                    Winner = ended.Winner;
                    break;
            }
        }
    }

    /// <summary>"side 0: 9 standing, 1 fallen, 0 fled; side 1: …" — standing means in the fight and not running.</summary>
    public static string Sides(BattleState state)
    {
        var sb = new StringBuilder();
        foreach (int side in state.Fighters.Select(f => f.SideId).Distinct().Order())
        {
            var men = state.Fighters.Where(f => f.SideId == side).ToArray();
            if (sb.Length > 0)
            {
                sb.Append("; ");
            }

            sb.Append(CultureInfo.InvariantCulture, $"side {side}: {men.Count(f => f.IsAlive && !f.IsRouting)} standing, ");
            sb.Append(CultureInfo.InvariantCulture, $"{men.Count(f => f.IsAlive && f.IsRouting)} running, ");
            sb.Append(CultureInfo.InvariantCulture, $"{men.Count(f => f.Wound == WoundLevel.Down)} fallen, ");
            sb.Append(CultureInfo.InvariantCulture, $"{men.Count(f => f.MoraleState == MoraleState.Fled)} fled");
        }

        return sb.ToString();
    }
}
