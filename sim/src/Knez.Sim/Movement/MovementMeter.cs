using System;
using System.Collections.Generic;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Accumulates the M1 movement metrics over a run (battle-sim.md §10, M1 step 8): call <see cref="Observe"/> after
    /// every tick, always with the same battle. A measuring tool for the CLI, the bench and tests — it only reads the state
    /// and keeps its own per-fighter memory, never anything the simulation depends on. Integers only (the sim's purity
    /// rules); tools turn the sums into ratios and metres.
    /// <list type="bullet">
    /// <item>overlap, passing and in-walls counts (<see cref="MovementMetrics"/>), per fighter-tick;</item>
    /// <item>slot error: distance from the slot (where a body fits) over squad members' fighter-ticks, mean and worst,
    /// and the longest spell any man spends more than <see cref="StuckGoal"/> from it (orbiting shows here);</item>
    /// <item>engaged: a squad any of whose men stands within <see cref="EngagedGap"/> of an enemy's body (decided
    /// 2026-11-21: fighting distance is the body plus 0.5 m; the whole squad counts — its rear ranks are held by the
    /// press in front). Combat and the melee lock come in M3; until then an engaged squad simply stands pressed;</item>
    /// <item>stuck: a living member of a squad that isn't engaged, more than <see cref="StuckGoal"/> from his slot and
    /// moving less than <see cref="StuckSpeed"/> a tick, for more than <see cref="StuckTicks"/> ticks in a row.</item>
    /// </list>
    /// </summary>
    public sealed class MovementMeter
    {
        /// <summary>A man further than this from his slot has somewhere to go: 1 m.</summary>
        public static readonly Fix StuckGoal = Fix.One;

        /// <summary>Moving less than this per tick (1 cm, i.e. 0.2 m/s) counts as not moving.</summary>
        public static readonly Fix StuckSpeed = Fix.Ratio(1, 100);

        /// <summary>Stuck means not moving toward a goal for longer than this: 2 s.</summary>
        public const int StuckTicks = 2 * BattleState.TicksPerSecond;

        /// <summary>Fighting distance beyond touching (decided 2026-11-21): bodies within 0.5 m of each other. The
        /// default for weapon reach when combat arrives (M3).</summary>
        public static readonly Fix EngagedGap = Fix.Ratio(1, 2);

        private readonly List<EntityId> _ids = new List<EntityId>();
        private readonly List<FixVec2> _last = new List<FixVec2>();
        private readonly List<int> _still = new List<int>();
        private readonly List<int> _far = new List<int>();
        private readonly List<bool> _everStuck = new List<bool>();
        private readonly List<bool> _engaged = new List<bool>();

        /// <summary>Living fighter-ticks observed.</summary>
        public long FighterTicks { get; private set; }

        /// <summary>Fighter-ticks pressed more than 5 cm into someone they shouldn't be.</summary>
        public long OverlappingTicks { get; private set; }

        /// <summary>The most fighters overlapping in one tick.</summary>
        public int WorstOverlap { get; private set; }

        /// <summary>Fighter-ticks spent passing through allied ranks (allowed overlaps).</summary>
        public long PassingTicks { get; private set; }

        /// <summary>Fighter-ticks with a body more than 5 cm inside a wall.</summary>
        public long InWallTicks { get; private set; }

        /// <summary>Squad members' fighter-ticks, the denominator of the slot error.</summary>
        public long MemberTicks { get; private set; }

        /// <summary>Sum of members' distances from their slots, in raw <see cref="Fix"/> units (divide by
        /// <see cref="MemberTicks"/> and <see cref="Fix.OneRaw"/> for the mean in metres).</summary>
        public long SlotErrorSumRaw { get; private set; }

        /// <summary>The largest distance any member stood from his slot.</summary>
        public Fix WorstSlotError { get; private set; }

        /// <summary>The longest spell any man spent more than <see cref="StuckGoal"/> from his slot, moving or not, ticks.</summary>
        public int LongestFarFromSlot { get; private set; }

        /// <summary>Members' fighter-ticks while their squad was engaged (excluded from stuck, reported beside it).</summary>
        public long EngagedTicks { get; private set; }

        /// <summary>Fighter-ticks beyond the first <see cref="StuckTicks"/> of a stuck spell.</summary>
        public long StuckFighterTicks { get; private set; }

        /// <summary>The longest stuck spell, ticks.</summary>
        public int LongestStuck { get; private set; }

        /// <summary>Fighters stuck for longer than <see cref="StuckTicks"/> at some point.</summary>
        public int FightersStuck { get; private set; }

        /// <summary>Records the state after a tick.</summary>
        public void Observe(BattleState state)
        {
            int overlapping = MovementMetrics.OverlappingFighters(state);
            OverlappingTicks += overlapping;
            WorstOverlap = WorstOverlap > overlapping ? WorstOverlap : overlapping;
            PassingTicks += MovementMetrics.PassingFighters(state);
            InWallTicks += MovementMetrics.FightersInWalls(state);

            List<Fighter> fighters = state.FighterList;
            List<Squad> squads = state.SquadList;
            MarkEngaged(state, fighters, squads);
            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter fighter = fighters[i];
                if (i == _ids.Count)
                {
                    _ids.Add(fighter.Id);
                    _last.Add(fighter.Position);
                    _still.Add(0);
                    _far.Add(0);
                    _everStuck.Add(false);
                }
                else if (_ids[i] != fighter.Id)
                {
                    throw new InvalidOperationException("MovementMeter follows one battle; its fighter list changed under it.");
                }

                FixVec2 moved = fighter.Position - _last[i];
                _last[i] = fighter.Position;
                if (!fighter.IsAlive)
                {
                    _still[i] = 0;
                    _far[i] = 0;
                    continue;
                }

                FighterTicks++;
                int squadIndex = fighter.SquadId.IsNone ? -1 : IndexOf(squads, fighter.SquadId);
                if (squadIndex < 0 || fighter.SlotIndex < 0)
                {
                    _still[i] = 0;
                    _far[i] = 0;
                    continue;
                }

                Fix error = Crowding.DistanceToSlot(state, squads[squadIndex], fighter);
                MemberTicks++;
                SlotErrorSumRaw += error.Raw;
                WorstSlotError = FixMath.Max(WorstSlotError, error);
                _far[i] = error > StuckGoal ? _far[i] + 1 : 0;
                LongestFarFromSlot = LongestFarFromSlot > _far[i] ? LongestFarFromSlot : _far[i];

                if (_engaged[squadIndex])
                {
                    EngagedTicks++;
                    _still[i] = 0;
                    continue;
                }

                _still[i] = error > StuckGoal && moved.Length < StuckSpeed ? _still[i] + 1 : 0;
                if (_still[i] > StuckTicks)
                {
                    StuckFighterTicks++;
                    if (!_everStuck[i])
                    {
                        _everStuck[i] = true;
                        FightersStuck++;
                    }
                }

                LongestStuck = LongestStuck > _still[i] ? LongestStuck : _still[i];
            }
        }

        /// <summary>Which squads have a living man within fighting distance of a living enemy this tick.</summary>
        private void MarkEngaged(BattleState state, List<Fighter> fighters, List<Squad> squads)
        {
            _engaged.Clear();
            for (int s = 0; s < squads.Count; s++)
            {
                _engaged.Add(false);
            }

            for (int i = 0; i < fighters.Count; i++)
            {
                Fighter a = fighters[i];
                if (!a.IsAlive || a.SquadId.IsNone)
                {
                    continue;
                }

                int squad = IndexOf(squads, a.SquadId);
                if (squad < 0 || _engaged[squad])
                {
                    continue;
                }

                for (int j = 0; j < fighters.Count; j++)
                {
                    Fighter b = fighters[j];
                    if (b.IsAlive && b.SideId != a.SideId
                        && (b.Position - a.Position).Length < a.Radius + b.Radius + EngagedGap)
                    {
                        _engaged[squad] = true;
                        break;
                    }
                }
            }
        }

        private static int IndexOf(List<Squad> squads, EntityId id)
        {
            for (int s = 0; s < squads.Count; s++)
            {
                if (squads[s].Id == id)
                {
                    return s;
                }
            }

            return -1;
        }
    }
}
