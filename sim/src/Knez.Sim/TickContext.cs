using System.Collections.Generic;
using Knez.Sim.Commands;
using Knez.Sim.Events;

namespace Knez.Sim
{
    /// <summary>
    /// What a system may use besides <see cref="BattleState"/>: random streams, event emission, and reusable scratch
    /// buffers. One instance lives as long as its <see cref="BattleSimulation"/>, so nothing here allocates per tick.
    /// </summary>
    public sealed class TickContext
    {
        private readonly BattleState _state;
        private Movement.NavGrid? _nav;
        private Movement.PathScratch? _pathScratch;

        internal TickContext(BattleState state, CommandQueue commands)
        {
            _state = state;
            Commands = commands;
            if (state.Scenario.Map.Walls.Count > 0)
            {
                // Built up front, not in the first tick that needs a route: a big map's grid is a load-time cost. Without
                // walls every straight line has room, so no route is ever searched and no grid is needed.
                _nav = new Movement.NavGrid(state.Scenario.Map);
                _pathScratch = new Movement.PathScratch(_nav.Count);
            }
        }

        /// <summary>The tick being processed.</summary>
        public int Tick => _state.Tick;

        /// <summary>The random stream for <paramref name="stream"/> — the only allowed source of randomness.</summary>
        public SimRng Rng(RngStream stream) => _state.Rng(stream);

        /// <summary>Records an event for the view.</summary>
        public void Emit(SimEvent simEvent) => _state.AddEvent(simEvent);

        internal CommandQueue Commands { get; }

        internal List<ScheduledCommand> DueCommands { get; } = new List<ScheduledCommand>();

        /// <summary>Per-fighter push weights for this tick (index = fighter list index), rebuilt by the systems that use it.</summary>
        internal List<int> FighterWeights { get; } = new List<int>();

        /// <summary>Whether each squad (by its index in the list) has a man in melee this tick (scratch for steering).</summary>
        internal List<bool> SquadInMelee { get; } = new List<bool>();

        /// <summary>Whether each fighter was safe (no threatening enemy within the safe distance) at the start of the
        /// morale stage, judged once so the order of transitions can't change it.</summary>
        internal List<bool> Safe { get; } = new List<bool>();

        /// <summary>Each fighter's morale state at the start of the morale stage (scratch for <see cref="Morale.MoraleSystem"/>).</summary>
        internal List<Morale.MoraleState> MoraleBefore { get; } = new List<Morale.MoraleState>();

        /// <summary>Blows that got through this tick, from combat (stage 8) to damage (stage 9); empty between ticks.</summary>
        internal List<Combat.PendingHit> Hits { get; } = new List<Combat.PendingHit>();

        /// <summary>Per-fighter intent for this tick: where formation-following alone would take him (before crowding).</summary>
        internal List<Numerics.FixVec2> FighterIntents { get; } = new List<Numerics.FixVec2>();

        /// <summary>The route-planning grid of this battle's map: built with the context for maps with walls, else on first use
        /// (derived content: not state).</summary>
        internal Movement.NavGrid Nav => _nav ??= new Movement.NavGrid(_state.Scenario.Map);

        /// <summary>Buffers for one route search, sized to <see cref="Nav"/>.</summary>
        internal Movement.PathScratch PathScratch => _pathScratch ??= new Movement.PathScratch(Nav.Count);

        /// <summary>Per-fighter crowding roles for this tick, filled with <see cref="FighterWeights"/>.</summary>
        internal List<Movement.CrowdRole> FighterRoles { get; } = new List<Movement.CrowdRole>();
    }
}
