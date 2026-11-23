using Knez.Sim.Morale;

namespace Knez.Sim.Events
{
    /// <summary>A man's morale state changed: shaken, wavering, routing, rallying, back, or fled the field.</summary>
    public sealed class MoraleStateChanged : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public MoraleStateChanged(int tick, EntityId fighter, MoraleState from, MoraleState to)
            : base(tick)
        {
            Fighter = fighter;
            From = from;
            To = to;
        }

        /// <summary>Whose state.</summary>
        public EntityId Fighter { get; }

        /// <summary>The state before.</summary>
        public MoraleState From { get; }

        /// <summary>The state now.</summary>
        public MoraleState To { get; }
    }
}
