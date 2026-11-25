#nullable enable
using System;
using System.Collections.Generic;
using Knez.Sim;
using Knez.Sim.Events;
using Knez.Sim.Morale;
using Knez.Sim.Units;
using EntityId = Knez.Sim.EntityId;

namespace Knez.Game.View
{
    /// <summary>
    /// View-side record of why morale moved (M3 step 8): for every man, the morale he had when the account began and the
    /// sum of the sim's <see cref="MoraleChanged"/> deltas by reason since. An account begins at the battle's start and
    /// again for a squad's men when it re-forms, so a squad that breaks twice is explained by what happened since it
    /// last stood. Plain C#, keyed by ids; never touches the state.
    /// </summary>
    public sealed class MoraleLedger
    {
        private static readonly int ReasonCount = (int)MaxReason() + 1;
        private readonly SquadTally _tally;
        private readonly Dictionary<int, int> _start = new Dictionary<int, int>();
        private readonly Dictionary<int, int[]> _sums = new Dictionary<int, int[]>();

        /// <summary>Creates a ledger that finds each man's squad through <paramref name="tally"/>.</summary>
        public MoraleLedger(SquadTally tally) => _tally = tally;

        /// <summary>Opens every man's account at his morale now; call when the battle starts.</summary>
        public void Begin(BattleState state)
        {
            _start.Clear();
            _sums.Clear();
            foreach (Fighter fighter in state.Fighters)
            {
                _start[fighter.Id.Value] = fighter.Morale;
                _sums[fighter.Id.Value] = new int[ReasonCount];
            }
        }

        /// <summary>
        /// Takes the tick just stepped (<paramref name="events"/>: its events, normally <c>state.Events</c>). Every morale
        /// change goes into its man's account first; only then do squads that re-formed open new accounts at their men's
        /// morale now. Accounts are read against morale at the end of the tick, so a change later in the same tick (a
        /// squad nearby breaking after this one re-formed) must land on one side of the reset, not both.
        /// </summary>
        public void Observe(BattleState state, IReadOnlyList<SimEvent> events)
        {
            foreach (SimEvent simEvent in events)
            {
                if (simEvent is MoraleChanged changed && _sums.TryGetValue(changed.Fighter.Value, out int[]? sums))
                {
                    sums[(int)changed.Reason] += changed.Delta;
                }
            }

            foreach (SimEvent simEvent in events)
            {
                if (simEvent is SquadBroke broke && broke.Reformed)
                {
                    foreach (Fighter fighter in state.Fighters)
                    {
                        if (_tally.HomeOf(fighter) == broke.Squad && _sums.TryGetValue(fighter.Id.Value, out int[]? his))
                        {
                            _start[fighter.Id.Value] = fighter.Morale;
                            Array.Clear(his, 0, his.Length);
                        }
                    }
                }
            }
        }

        /// <summary>The account of a squad's men still in the fight (alive, not fled), averaged over them.</summary>
        public MoraleAccount Of(BattleState state, EntityId squad)
        {
            var men = new List<Fighter>();
            foreach (Fighter fighter in state.Fighters)
            {
                if (fighter.IsAlive && _tally.HomeOf(fighter) == squad)
                {
                    men.Add(fighter);
                }
            }

            return Sum(men);
        }

        /// <summary>One man's account.</summary>
        public MoraleAccount Of(Fighter fighter) => Sum(new List<Fighter> { fighter });

        private MoraleAccount Sum(List<Fighter> men)
        {
            long start = 0;
            long now = 0;
            long moved = 0;
            var totals = new long[ReasonCount];
            int counted = 0;
            foreach (Fighter man in men)
            {
                if (!_sums.TryGetValue(man.Id.Value, out int[]? sums))
                {
                    continue;
                }

                counted++;
                start += _start[man.Id.Value];
                now += man.Morale;
                for (int r = 0; r < ReasonCount; r++)
                {
                    totals[r] += sums[r];
                    moved += sums[r];
                }
            }

            if (counted == 0)
            {
                return new MoraleAccount(0, 0, 0, Array.Empty<(MoraleReason, int)>(), 0);
            }

            var reasons = new List<(MoraleReason Reason, int Average)>();
            for (int r = 0; r < ReasonCount; r++)
            {
                int average = Average(totals[r], counted);
                if (average != 0)
                {
                    reasons.Add(((MoraleReason)r, average));
                }
            }

            reasons.Sort((a, b) => Math.Abs(b.Average).CompareTo(Math.Abs(a.Average)));
            return new MoraleAccount(counted, Average(start, counted), Average(now, counted), reasons, Average(now - start - moved, counted));
        }

        private static int Average(long sum, int count) => (int)Math.Round(sum / (double)count, MidpointRounding.AwayFromZero);

        private static MoraleReason MaxReason()
        {
            MoraleReason max = default;
            foreach (MoraleReason reason in (MoraleReason[])Enum.GetValues(typeof(MoraleReason)))
            {
                max = reason > max ? reason : max;
            }

            return max;
        }
    }
}
