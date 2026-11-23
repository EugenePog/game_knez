namespace Knez.Sim.Combat
{
    /// <summary>What a fighter's weapon is doing (battle-sim.md §11): a blow is a windup, the strike, then a recovery.</summary>
    public enum FighterAction : byte
    {
        /// <summary>Ready: he starts a blow as soon as an enemy is within reach and his strike arc.</summary>
        Idle = 0,

        /// <summary>Drawing back for a blow; it lands when the windup runs out.</summary>
        Windup = 1,

        /// <summary>Recovering after the strike; idle again when it runs out.</summary>
        Recovery = 2,
    }
}
