namespace Knez.Sim.Defs
{
    /// <summary>
    /// The six Retainer abilities, one per trait (D-025, battle-sim.md §13). All are defined in content so the AI can
    /// weigh them; M3 step 11 builds the three that only change numbers — battle cry, close ranks, stand to the death —
    /// and the others are refused as not built until each is tested with the AI (decided by the user 2026-11-26).
    /// </summary>
    public enum AbilityKind : byte
    {
        /// <summary>Proud: own men near him gain morale, enemies near him lose it (built).</summary>
        BattleCry = 1,

        /// <summary>Cautious: his squad takes less from the front and marches slower, for a while (built).</summary>
        CloseRanks = 2,

        /// <summary>Greedy: his squad's morale and push up for a while (not built).</summary>
        PromiseLoot = 3,

        /// <summary>Loyal: his squad's morale can't fall for a few seconds (built).</summary>
        StandToTheDeath = 4,

        /// <summary>Bold: the front rank steps forward and strikes harder, then the squad is disordered (not built).</summary>
        FrontRankRush = 5,

        /// <summary>Cruel: his squad finishes the fallen and enemies near it lose morale faster (not built).</summary>
        NoQuarter = 6,
    }
}
