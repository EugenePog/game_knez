namespace Knez.Sim.Combat
{
    /// <summary>Tick stage 7: engagement and target choice in reach, ties broken by distance then id (battle-sim.md §9). Empty until M3.</summary>
    public sealed class TargetingSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
        }
    }
}
