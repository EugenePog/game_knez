namespace Knez.Sim.Morale
{
    /// <summary>Why a man's morale changed — the reason code the UI shows in tooltips and the log (battle-sim.md §12).</summary>
    public enum MoraleReason : byte
    {
        /// <summary>A man of his squad fell.</summary>
        SquadmateFell = 0,

        /// <summary>He was wounded.</summary>
        Wounded = 1,

        /// <summary>A man of his squad was heavily wounded.</summary>
        SquadmateHeavilyWounded = 2,

        /// <summary>He was struck at from his flank.</summary>
        StruckFromFlank = 3,

        /// <summary>He was struck at from behind.</summary>
        StruckFromRear = 4,

        /// <summary>His squad is being struck from the flank.</summary>
        SquadFlanked = 5,

        /// <summary>His squad is being struck from behind.</summary>
        SquadTakenInRear = 6,

        /// <summary>A man of his squad ran.</summary>
        SquadmateRouted = 7,

        /// <summary>An allied squad nearby broke.</summary>
        AlliedSquadBroke = 8,

        /// <summary>An enemy squad nearby broke.</summary>
        EnemySquadBroke = 9,

        /// <summary>The Retainer leading his squad fell (M3 step 10).</summary>
        LeaderFell = 10,

        /// <summary>A Retainer of his side gave a battle cry near him (M3 step 11).</summary>
        BattleCry = 11,

        /// <summary>An enemy Retainer gave a battle cry near him (M3 step 11).</summary>
        EnemyBattleCry = 12,
    }
}
