namespace Knez.Sim.Morale
{
    /// <summary>A fighter's state of mind (battle-sim.md §12). Falls at the thresholds of <see cref="Defs.MoraleDef"/>,
    /// climbs back only a hysteresis above them.</summary>
    public enum MoraleState : byte
    {
        /// <summary>Fights as ordered.</summary>
        Steady = 0,

        /// <summary>Shaken: still fights as ordered; a warning.</summary>
        Shaken = 1,

        /// <summary>Wavering: his blows get through less.</summary>
        Wavering = 2,

        /// <summary>Routing: flees toward his side's map edge, ignores orders, strikes no one.</summary>
        Routing = 3,

        /// <summary>Rallied after routing, on his way back to his squad; steady, shaken or wavering again once back.</summary>
        Rallying = 4,

        /// <summary>Left the battle over his side's map edge (out of the fight, alive).</summary>
        Fled = 5,

        /// <summary>Left the field in order with his squad (M4 step 2): it marched off its side's map edge (out of the
        /// fight, alive; not a rout).</summary>
        Left = 6,
    }
}
