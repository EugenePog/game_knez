namespace Knez.Sim.Events
{
    /// <summary>
    /// Something that happened during a tick, for the view (VFX, sound, UI, battle log). Events are output only:
    /// they are cleared at the start of every tick and are not part of the state hash.
    /// </summary>
    public abstract class SimEvent
    {
        /// <summary>Creates an event stamped with the tick it happened in.</summary>
        protected SimEvent(int tick)
        {
            Tick = tick;
        }

        /// <summary>The tick the event happened in.</summary>
        public int Tick { get; }
    }
}
