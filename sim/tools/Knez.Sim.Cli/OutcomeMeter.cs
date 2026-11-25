using Knez.Sim.Combat;
using Knez.Sim.Events;
using Knez.Sim.Units;

namespace Knez.Sim.Cli;

/// <summary>How a run ended, for the D-026 measures (battle-sim.md §11).</summary>
public enum BattleEnd
{
    /// <summary>Still undecided when the run stopped (the time limit).</summary>
    Time,

    /// <summary>Decided while the losing side still had living men, all of them running or gone.</summary>
    Rout,

    /// <summary>Decided with every man of the losing side down.</summary>
    Annihilation,

    /// <summary>Decided with nobody left in the fight on any side.</summary>
    Nobody,
}

/// <summary>
/// The D-026 balance measures of one run (battle-sim.md §11, M3 step 7): when the first blow fell; when, how and for
/// whom the battle was decided; the share of its men each squad had lost when it first broke; the fallen on each side.
/// Everything stops at the decision tick — the sim keeps running for the view, but what happens after isn't the
/// battle's result. Call <see cref="Observe"/> after every step, always with the same battle. Reads events and state
/// only; never part of the simulation.
/// </summary>
public sealed class OutcomeMeter
{
    private readonly Dictionary<EntityId, EntityId> _squadOf = [];
    private readonly Dictionary<EntityId, int> _startCount = [];
    private readonly HashSet<EntityId> _broken = [];
    private readonly List<double> _lossAtBreak = [];
    private readonly List<int> _breakSides = [];
    private readonly Dictionary<EntityId, int> _sideOf = [];
    private readonly int[] _start = new int[Defs.SquadSpawnDef.MaxSideId + 1];
    private readonly int[] _fallen = new int[Defs.SquadSpawnDef.MaxSideId + 1];

    /// <summary>Starts measuring a battle as it is before its first step.</summary>
    public OutcomeMeter(BattleState start)
    {
        foreach (Fighter man in start.Fighters)
        {
            _start[man.SideId]++;
            _squadOf[man.Id] = man.SquadId;
        }

        foreach (Squad squad in start.Squads)
        {
            _sideOf[squad.Id] = squad.SideId;
            _startCount[squad.Id] = squad.StartCount; // the sim's own starting strength, right even mid-battle
        }
    }

    /// <summary>The tick of the first blow struck, or −1.</summary>
    public int FirstBlowTick { get; private set; } = -1;

    /// <summary>The tick the battle was decided, or −1 while undecided.</summary>
    public int EndTick { get; private set; } = -1;

    /// <summary>The last tick observed: the decision tick once decided.</summary>
    public int LastTick { get; private set; }

    /// <summary>The side that holds the field, −1 for nobody, <see cref="BattleState.Undecided"/> while undecided.</summary>
    public int Winner { get; private set; } = BattleState.Undecided;

    /// <summary>How it ended; <see cref="BattleEnd.Time"/> until decided.</summary>
    public BattleEnd End { get; private set; } = BattleEnd.Time;

    /// <summary>Blows struck up to the decision.</summary>
    public int Blows { get; private set; }

    /// <summary>Blows that got through, up to the decision.</summary>
    public int Landed { get; private set; }

    /// <summary>Whether the battle has been decided.</summary>
    public bool Decided => EndTick >= 0;

    /// <summary>Per squad that broke (its first break only, up to the decision), the share of its starting men down
    /// at that tick, 0..1, in the order they broke.</summary>
    public IReadOnlyList<double> LossAtBreak => _lossAtBreak;

    /// <summary>The side of each squad in <see cref="LossAtBreak"/>, in the same order.</summary>
    public IReadOnlyList<int> LossAtBreakSides => _breakSides;

    /// <summary>Men a side started with.</summary>
    public int StartOf(int side) => _start[side];

    /// <summary>Men of a side down at the decision (or at the last tick observed, while undecided).</summary>
    public int FallenOf(int side) => _fallen[side];

    /// <summary>Records the tick just stepped; does nothing once the battle is decided.</summary>
    public void Observe(BattleState state)
    {
        if (Decided)
        {
            return;
        }

        LastTick = state.Tick;
        Array.Clear(_fallen);
        foreach (Fighter man in state.Fighters)
        {
            if (man.Wound == WoundLevel.Down)
            {
                _fallen[man.SideId]++;
            }
        }

        foreach (SimEvent e in state.Events)
        {
            switch (e)
            {
                case BlowStruck blow:
                    FirstBlowTick = FirstBlowTick < 0 ? e.Tick : FirstBlowTick;
                    Blows++;
                    Landed += blow.Landed ? 1 : 0;
                    break;
                case SquadBroke { Reformed: false } broke when _broken.Add(broke.Squad):
                    _lossAtBreak.Add(DownIn(state, broke.Squad) / (double)_startCount[broke.Squad]);
                    _breakSides.Add(_sideOf[broke.Squad]);
                    break;
                case BattleEnded ended:
                    EndTick = e.Tick;
                    Winner = ended.Winner;
                    End = ended.Winner < 0 ? BattleEnd.Nobody : LosersAllDown(state, ended.Winner) ? BattleEnd.Annihilation : BattleEnd.Rout;
                    break;
            }
        }
    }

    /// <summary>Men of a squad as it started who are down now.</summary>
    private int DownIn(BattleState state, EntityId squad)
    {
        int down = 0;
        foreach (Fighter man in state.Fighters)
        {
            if (man.Wound == WoundLevel.Down && _squadOf.TryGetValue(man.Id, out EntityId his) && his == squad)
            {
                down++;
            }
        }

        return down;
    }

    /// <summary>Every man not of the winning side is down.</summary>
    private static bool LosersAllDown(BattleState state, int winner)
    {
        foreach (Fighter man in state.Fighters)
        {
            if (man.SideId != winner && man.Wound != WoundLevel.Down)
            {
                return false;
            }
        }

        return true;
    }
}
