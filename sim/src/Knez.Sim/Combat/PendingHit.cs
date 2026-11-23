namespace Knez.Sim.Combat
{
    /// <summary>A blow that got through this tick, waiting for <see cref="DamageSystem"/>: all of a tick's blows are struck
    /// before any of them wounds, so two men striking on the same tick both land, whatever their ids. Tick scratch, never
    /// state: the list is empty between ticks.</summary>
    internal readonly struct PendingHit
    {
        public PendingHit(EntityId attacker, EntityId defender)
        {
            Attacker = attacker;
            Defender = defender;
        }

        public EntityId Attacker { get; }

        public EntityId Defender { get; }
    }
}
