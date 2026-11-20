namespace Knez.Sim
{
    /// <summary>An entity kept in a list sorted by its id, so it can be found by binary search.</summary>
    internal interface IHasEntityId
    {
        /// <summary>Identity.</summary>
        EntityId Id { get; }
    }
}
