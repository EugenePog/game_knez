namespace Knez.Sim.Events
{
    /// <summary>A squad broke — more than half its men in the fight were running, so all of them run (battle-sim.md §12) —
    /// or, with <see cref="Reformed"/>, re-formed after enough men rallied. Carries the counts that decided it, so the view
    /// can say why.</summary>
    public sealed class SquadBroke : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public SquadBroke(int tick, EntityId squad, bool reformed, int inFight, int running)
            : base(tick)
        {
            Squad = squad;
            Reformed = reformed;
            InFight = inFight;
            Running = running;
        }

        /// <summary>Which squad.</summary>
        public EntityId Squad { get; }

        /// <summary>False: it broke. True: it re-formed.</summary>
        public bool Reformed { get; }

        /// <summary>Its men in the fight (alive members) when it broke or re-formed.</summary>
        public int InFight { get; }

        /// <summary>Of those, men running: when it broke, those already running (the rest were swept along); when it
        /// re-formed, those still running.</summary>
        public int Running { get; }
    }
}
