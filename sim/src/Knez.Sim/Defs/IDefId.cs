namespace Knez.Sim.Defs
{
    /// <summary>A def with a content id, so catalogue helpers can find it without lambdas (which would add
    /// compiler-generated classes to the def namespace).</summary>
    internal interface IDefId
    {
        /// <summary>Content id.</summary>
        string Id { get; }
    }
}
