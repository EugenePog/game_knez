namespace Knez.Sim.Movement
{
    /// <summary>Tick stage 4: A* requests for squad anchors and detached fighters, budgeted per tick (battle-sim.md §9). Empty until M1.</summary>
    public sealed class PathSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
        }
    }
}
