namespace Knez.Sim
{
    /// <summary>
    /// One stage of the tick pipeline (battle-sim.md §9). Systems hold no state of their own: everything they read or
    /// write lives in <see cref="BattleState"/>, and scratch buffers live in <see cref="TickContext"/>.
    /// </summary>
    public interface ISimSystem
    {
        /// <summary>Runs this stage for the tick <see cref="BattleState.Tick"/>.</summary>
        void Execute(BattleState state, TickContext context);
    }
}
