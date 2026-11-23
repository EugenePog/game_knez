namespace Knez.Sim.Events
{
    /// <summary>A squad broke — more than half its men were routing, so all of them run (battle-sim.md §12) — or, with
    /// <see cref="Reformed"/>, re-formed after enough men rallied.</summary>
    public sealed class SquadBroke : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public SquadBroke(int tick, EntityId squad, bool reformed)
            : base(tick)
        {
            Squad = squad;
            Reformed = reformed;
        }

        /// <summary>Which squad.</summary>
        public EntityId Squad { get; }

        /// <summary>False: it broke. True: it re-formed.</summary>
        public bool Reformed { get; }
    }
}
