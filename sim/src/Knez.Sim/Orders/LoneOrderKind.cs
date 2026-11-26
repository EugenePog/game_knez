namespace Knez.Sim.Orders
{
    /// <summary>What a Retainer standing alone has been told to do (battle-sim.md §13, M3 step 10).</summary>
    public enum LoneOrderKind : byte
    {
        /// <summary>Nothing: he stands where he is.</summary>
        None = 0,

        /// <summary>Walk to a point and face a way.</summary>
        Move = 1,

        /// <summary>Go at an enemy fighter and fight him.</summary>
        Attack = 2,
    }
}
