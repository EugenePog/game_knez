namespace Knez.Sim.Movement
{
    /// <summary>Tick stage 5: desired velocity from slot seek, separation and obstacle avoidance (battle-sim.md §9). Empty until M1.</summary>
    public sealed class SteeringSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
        }
    }
}
