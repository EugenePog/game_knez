using Knez.Sim.Ai;

namespace Knez.Sim.Events
{
    /// <summary>The enemy AI changed what a squad is doing, or its target (battle-sim.md §14, M4 step 2): for the log and
    /// the view.</summary>
    public sealed class SquadAiChanged : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public SquadAiChanged(int tick, EntityId squad, SquadAiState from, SquadAiState to, EntityId target)
            : base(tick)
        {
            Squad = squad;
            From = from;
            To = to;
            Target = target;
        }

        /// <summary>Which squad.</summary>
        public EntityId Squad { get; }

        /// <summary>What it was doing.</summary>
        public SquadAiState From { get; }

        /// <summary>What it does now.</summary>
        public SquadAiState To { get; }

        /// <summary>The enemy squad it goes for (Advance, Engage), or none.</summary>
        public EntityId Target { get; }
    }
}
