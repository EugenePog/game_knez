using System.Collections.Generic;
using Knez.Sim.Events;
using Knez.Sim.Numerics;
using Knez.Sim.Units;

namespace Knez.Sim
{
    /// <summary>
    /// All state of one battle (AGENTS.md hard rule 3). Systems read and write it; the view only reads it.
    /// Every field that affects the future is hashed by <see cref="StateHasher"/> and copied by <see cref="Clone"/>.
    /// </summary>
    public sealed class BattleState
    {
        /// <summary>Simulation ticks per second of game time (ADR-0003).</summary>
        public const int TicksPerSecond = 20;

        private readonly SimRng[] _rng;
        private readonly List<Fighter> _fighters;
        private readonly List<SimEvent> _events;
        private int _nextEntityId;

        /// <summary>A new, empty battle whose random streams derive from <paramref name="seed"/>.</summary>
        public BattleState(ulong seed)
        {
            Seed = seed;
            _rng = new SimRng[RngStreams.Count];
            for (int i = 0; i < RngStreams.Count; i++)
            {
                _rng[i] = SimRng.ForStream(seed, (RngStream)i);
            }

            _fighters = new List<Fighter>();
            _events = new List<SimEvent>();
            _nextEntityId = 1;
        }

        private BattleState(BattleState source)
        {
            Seed = source.Seed;
            Tick = source.Tick;
            _nextEntityId = source._nextEntityId;
            _rng = new SimRng[RngStreams.Count];
            for (int i = 0; i < RngStreams.Count; i++)
            {
                _rng[i] = source._rng[i].Clone();
            }

            _fighters = new List<Fighter>(source._fighters.Count);
            for (int i = 0; i < source._fighters.Count; i++)
            {
                _fighters.Add(source._fighters[i].Clone());
            }

            _events = new List<SimEvent>(source._events);
        }

        /// <summary>Battle seed; every random stream derives from it.</summary>
        public ulong Seed { get; }

        /// <summary>Number of completed ticks. During a step, systems see the number of the tick being processed.</summary>
        public int Tick { get; private set; }

        /// <summary>The value the next allocated <see cref="EntityId"/> will get.</summary>
        public int NextEntityIdValue => _nextEntityId;

        /// <summary>All fighters, sorted by <see cref="EntityId"/> (read-only view for the outside).</summary>
        public IReadOnlyList<Fighter> Fighters => _fighters;

        /// <summary>
        /// Events emitted during the most recent tick, in emission order. They live for one tick only: a view that
        /// steps several times per frame must read them after every <see cref="BattleSimulation.Step"/>.
        /// </summary>
        public IReadOnlyList<SimEvent> Events => _events;

        /// <summary>The fighters list itself, for systems: index with a <c>for</c> loop (no interface calls, no enumerator).</summary>
        internal List<Fighter> FighterList => _fighters;

        /// <summary>
        /// The random stream for <paramref name="stream"/>. Internal: drawing from it outside the sim would desync the
        /// game from its replay. Systems and commands use <see cref="TickContext.Rng"/>.
        /// </summary>
        internal SimRng Rng(RngStream stream) => _rng[(int)stream];

        /// <summary>A fresh id, never used before in this battle.</summary>
        internal EntityId AllocateId() => new EntityId(_nextEntityId++);

        /// <summary>Adds a fighter (scenario setup or a spawning rule) and returns it.</summary>
        public Fighter AddFighter(int sideId, FixVec2 position)
        {
            var fighter = new Fighter(AllocateId(), sideId, position);
            _fighters.Add(fighter); // ids grow monotonically, so the list stays sorted
            return fighter;
        }

        /// <summary>The fighter with <paramref name="id"/>, or null. Binary search over the sorted list.</summary>
        public Fighter? FindFighter(EntityId id)
        {
            int lo = 0;
            int hi = _fighters.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int value = _fighters[mid].Id.Value;
                if (value == id.Value)
                {
                    return _fighters[mid];
                }

                if (value < id.Value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return null;
        }

        /// <summary>A deep copy: same hash, independent future.</summary>
        public BattleState Clone() => new BattleState(this);

        internal void BeginTick()
        {
            Tick++;
            _events.Clear();
        }

        internal void AddEvent(SimEvent simEvent) => _events.Add(simEvent);
    }
}
