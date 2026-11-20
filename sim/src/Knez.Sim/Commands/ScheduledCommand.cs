namespace Knez.Sim.Commands
{
    /// <summary>A command stamped with the tick it applies at and its enqueue order (the tie-break within a tick).</summary>
    public readonly struct ScheduledCommand
    {
        /// <summary>Creates a scheduled command.</summary>
        public ScheduledCommand(int tick, long sequence, ICommand command)
        {
            Tick = tick;
            Sequence = sequence;
            Command = command;
        }

        /// <summary>The tick at whose start the command applies.</summary>
        public int Tick { get; }

        /// <summary>Enqueue order, unique per queue; orders commands within the same tick.</summary>
        public long Sequence { get; }

        /// <summary>The command.</summary>
        public ICommand Command { get; }
    }
}
