namespace Knez.Sim.Combat
{
    /// <summary>How badly a fighter is hurt (battle-sim.md §11). No hit points: every blow that gets through leaves a
    /// wound, and wounds add up.</summary>
    public enum WoundLevel : byte
    {
        /// <summary>Unhurt.</summary>
        None = 0,

        /// <summary>Lightly wounded: his blows get through less and he moves a little slower.</summary>
        Light = 1,

        /// <summary>Heavily wounded: much weaker blows, much slower, and his squad feels it (morale, step 5).</summary>
        Heavy = 2,

        /// <summary>Down: out of the fight (decided 2026-11-22); whether he lives is settled after the battle.</summary>
        Down = 3,
    }
}
