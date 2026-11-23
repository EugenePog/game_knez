namespace Knez.Sim.Events
{
    /// <summary>The battle is decided (battle-sim.md §12): only one side has men left in the fight who aren't routing.
    /// <see cref="Winner"/> is that side, or −1 when nobody is left standing.</summary>
    public sealed class BattleEnded : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public BattleEnded(int tick, int winner)
            : base(tick)
        {
            Winner = winner;
        }

        /// <summary>The side that holds the field, or −1.</summary>
        public int Winner { get; }
    }
}
