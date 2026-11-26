namespace Knez.Sim.Ai
{
    /// <summary>
    /// What the enemy AI has a squad doing (battle-sim.md §14, M4 step 2). Routing is morale's, not a choice: a broken
    /// squad is put in <see cref="Regroup"/>, which it carries out once it re-forms.
    /// </summary>
    public enum SquadAiState : byte
    {
        /// <summary>No decision yet, or nothing to fight; also every squad of a side the AI doesn't play.</summary>
        Idle = 0,

        /// <summary>Going round to its target's open flank before it attacks (the second squad on a target).</summary>
        Advance = 1,

        /// <summary>Attacking its target (an attack order on it).</summary>
        Engage = 2,

        /// <summary>Holding its ground, outnumbered: it meets anyone who comes within the advance range.</summary>
        Hold = 3,

        /// <summary>Fallen back to recover its nerve; fights again once its men's morale is back.</summary>
        Regroup = 4,

        /// <summary>Its side is leaving the field: it marches off its side's map edge. Never undone.</summary>
        Withdraw = 5,
    }
}
