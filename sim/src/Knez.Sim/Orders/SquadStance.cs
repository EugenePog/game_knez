namespace Knez.Sim.Orders
{
    /// <summary>
    /// How a squad fights on its own (battle-sim.md §8, §10, M4 step 1; decided by the user 2026-11-26). Each includes the
    /// one before: Pursue also advances.
    /// </summary>
    public enum SquadStance : byte
    {
        /// <summary>It fights what comes to it and goes nowhere on its own. The default.</summary>
        Hold = 0,

        /// <summary>With no order, it goes at the nearest enemy squad within the advance range (15 m).</summary>
        Advance = 1,

        /// <summary>As Advance; and when the enemy it fights breaks, its men break ranks and run the runners down for up to
        /// 10 s within 30 m of where the chase began, then re-form where they stand.</summary>
        Pursue = 2,
    }
}
