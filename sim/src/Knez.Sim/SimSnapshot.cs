using Knez.Sim.Commands;

namespace Knez.Sim
{
    /// <summary>
    /// A frozen copy of a simulation: state plus pending commands. Restoring it and continuing must produce exactly the
    /// hashes the uninterrupted run would (battle-sim.md §15). A snapshot can be restored any number of times.
    /// </summary>
    public sealed class SimSnapshot
    {
        internal SimSnapshot(BattleState state, CommandQueue commands)
        {
            State = state;
            Commands = commands;
        }

        /// <summary>The state at the moment of the snapshot. Internal: read it through a restored simulation.</summary>
        internal BattleState State { get; }

        /// <summary>Pending commands and the command log at the moment of the snapshot.</summary>
        public CommandQueue Commands { get; }

        /// <summary>The tick the snapshot was taken at.</summary>
        public int Tick => State.Tick;
    }
}
