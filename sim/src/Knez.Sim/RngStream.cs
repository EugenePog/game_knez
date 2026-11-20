namespace Knez.Sim
{
    /// <summary>
    /// Independent random streams, one per purpose (battle-sim.md §15). Separate streams keep, say, a change in AI
    /// randomness from shifting every combat roll in an A/B balance comparison. Count: <see cref="RngStreams.Count"/>.
    /// </summary>
    public enum RngStream
    {
        /// <summary>Hit rolls, damage, wounds.</summary>
        Combat = 0,

        /// <summary>Morale checks, rout and rally.</summary>
        Morale = 1,

        /// <summary>Enemy AI decisions.</summary>
        Ai = 2,

        /// <summary>Anything else (scenario variation, tests).</summary>
        Misc = 3,
    }
}
