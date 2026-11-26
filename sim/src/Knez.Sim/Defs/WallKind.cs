namespace Knez.Sim.Defs
{
    /// <summary>
    /// What a wall stands for on the map (battle-sim.md §7): today only drawn differently — every kind blocks bodies the
    /// same way. A rule that reads it (cover, line of sight, setting a house on fire) must first add it to the scenario
    /// fingerprint (<see cref="WallDef"/>).
    /// </summary>
    public enum WallKind : byte
    {
        /// <summary>Rocks, a fence, a palisade: anything that isn't a house. The default.</summary>
        Obstacle = 0,

        /// <summary>A house or another building of a village.</summary>
        House = 1,
    }
}
