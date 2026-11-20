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

        internal TickContext(BattleState state, CommandQueue commands)
        {
            _state = state;
            Commands = commands;
        }

        /// <summary>The tick being processed.</summary>
        public int Tick => _state.Tick;

        /// <summary>The random stream for <paramref name="stream"/> — the only allowed source of randomness.</summary>
        public SimRng Rng(RngStream stream) => _state.Rng(stream);

        /// <summary>Records an event for the view.</summary>
        public void Emit(SimEvent simEvent) => _state.AddEvent(simEvent);

        internal CommandQueue Commands { get; }

        internal List<ScheduledCommand> DueCommands { get; } = new List<ScheduledCommand>();
    }
}
