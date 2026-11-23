using Knez.Sim.Morale;

namespace Knez.Sim.Events
{
    /// <summary>A man's morale changed for a reason the player can read (battle-sim.md §12). Steady recovery out of danger
    /// is not reported tick by tick.</summary>
    public sealed class MoraleChanged : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public MoraleChanged(int tick, EntityId fighter, MoraleReason reason, int delta, int now)
            : base(tick)
        {
            Fighter = fighter;
            Reason = reason;
            Delta = delta;
            Now = now;
        }

        /// <summary>Whose morale.</summary>
        public EntityId Fighter { get; }

        /// <summary>Why.</summary>
        public MoraleReason Reason { get; }

        /// <summary>By how much (negative = worse), after clamping to 0..1000.</summary>
        public int Delta { get; }

        /// <summary>His morale after it.</summary>
        public int Now { get; }
    }
}
