#nullable enable
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Events;
using Knez.Sim.Morale;

namespace Knez.Game.View
{
    /// <summary>
    /// Everything the view remembers about the battle so far (M3 step 8), fed from the sim's events after each tick: who
    /// started where and the blows (<see cref="SquadTally"/>), why morale moved (<see cref="MoraleLedger"/>) and the
    /// battle in words (<see cref="BattleLog"/>). One <see cref="Begin"/> and one <see cref="Observe"/> keep the three in
    /// step: the ledger takes the whole tick before the log explains it, so an account is always the end of the tick's.
    /// Plain C#; never touches the state.
    /// </summary>
    public sealed class BattleRecord
    {
        private readonly HashSet<int> _sweptAlong = new HashSet<int>();

        /// <summary>Creates an empty record.</summary>
        public BattleRecord()
        {
            Tally = new SquadTally();
            Ledger = new MoraleLedger(Tally);
            Log = new BattleLog(Tally, Ledger);
        }

        /// <summary>Who started in which squad, blows per squad, each man's last blow.</summary>
        public SquadTally Tally { get; }

        /// <summary>Why morale moved, per man, since the start or his squad's last re-form.</summary>
        public MoraleLedger Ledger { get; }

        /// <summary>The battle in words.</summary>
        public BattleLog Log { get; }

        /// <summary>Whether <see cref="Begin"/> has run.</summary>
        public bool Begun { get; private set; }

        /// <summary>Starts recording a battle as it stands before its first step.</summary>
        public void Begin(BattleState state)
        {
            Tally.Begin(state);
            Ledger.Begin(state);
            Log.Begin();
            Begun = true;
        }

        /// <summary>Takes the tick just stepped.</summary>
        public void Observe(BattleState state)
        {
            IReadOnlyList<SimEvent> events = state.Events;
            Tally.Observe(state);
            Ledger.Observe(state, events);

            // A break sweeps the men of the squad not yet running: the sim sets each to running right before the
            // SquadBroke event (battle-sim.md §12), InFight − Running of them. Those aren't logged one by one.
            _sweptAlong.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is SquadBroke broke && !broke.Reformed)
                {
                    int left = broke.InFight - broke.Running;
                    for (int j = i - 1; j >= 0 && left > 0; j--)
                    {
                        if (events[j] is MoraleStateChanged changed && changed.To == MoraleState.Routing
                            && Tally.HomeOf(changed.Fighter) == broke.Squad)
                        {
                            _sweptAlong.Add(j);
                            left--;
                        }
                    }
                }
            }

            for (int i = 0; i < events.Count; i++)
            {
                Log.Apply(state, events[i], _sweptAlong.Contains(i));
            }
        }
    }
}
