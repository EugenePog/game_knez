using Knez.Sim.Commands;

namespace Knez.Sim.Events
{
    /// <summary>A command was invalid when its tick came and changed nothing (ADR-0003).</summary>
    public sealed class CommandRejected : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public CommandRejected(int tick, ICommand command, RejectReason reason)
            : base(tick)
        {
            Command = command;
            Reason = reason;
        }

        /// <summary>The rejected command.</summary>
        public ICommand Command { get; }

        /// <summary>Why it was rejected, for the UI and the battle log.</summary>
        public RejectReason Reason { get; }
    }
}
