namespace Knez.Sim.Combat
{
    /// <summary>Tick stage 11: fatigue (battle-sim.md §9). Empty: built only if M6 tuning asks for it.</summary>
    public sealed class FatigueSystem : ISimSystem
    {
        /// <inheritdoc/>
        public void Execute(BattleState state, TickContext context)
        {
        }
    }
}
