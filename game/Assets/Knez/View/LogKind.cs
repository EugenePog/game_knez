namespace Knez.Game.View
{
    /// <summary>What a battle log line is about, for its colour (M3 step 8).</summary>
    public enum LogKind
    {
        /// <summary>The first blow of the battle.</summary>
        FirstBlow,

        /// <summary>A man wounded.</summary>
        Wound,

        /// <summary>A man down.</summary>
        Fell,

        /// <summary>A man began to run.</summary>
        Ran,

        /// <summary>A man rallied or is back in the ranks.</summary>
        Rallied,

        /// <summary>A man left the field.</summary>
        Fled,

        /// <summary>A squad broke, with why.</summary>
        Broke,

        /// <summary>A squad re-formed.</summary>
        Reformed,

        /// <summary>An order was refused.</summary>
        Refused,

        /// <summary>The battle was decided.</summary>
        Ended,

        /// <summary>A Retainer used his ability (M3 step 11).</summary>
        Ability,

        /// <summary>The enemy AI changed what one of its squads is doing (M4 step 2).</summary>
        Ai,
    }
}
