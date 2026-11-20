namespace Knez.Sim.Orders
{
    /// <summary>What a squad is doing on orders.</summary>
    public enum SquadOrderKind
    {
        /// <summary>No order: the squad holds where it stands.</summary>
        None = 0,

        /// <summary>March the anchor to a target, then face a direction.</summary>
        Move = 1,
    }
}
