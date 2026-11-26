namespace Knez.Sim.Events
{
    /// <summary>A Retainer used his active ability (battle-sim.md §13, M3 step 11).</summary>
    public sealed class AbilityUsed : SimEvent
    {
        /// <summary>Creates the event.</summary>
        public AbilityUsed(int tick, EntityId retainer, string ability, EntityId squad)
            : base(tick)
        {
            Retainer = retainer;
            Ability = ability;
            Squad = squad;
        }

        /// <summary>Who used it.</summary>
        public EntityId Retainer { get; }

        /// <summary>The ability's content id, e.g. "battle-cry".</summary>
        public string Ability { get; }

        /// <summary>The squad he was in (a lasting ability acts on it), or none when he stood alone.</summary>
        public EntityId Squad { get; }
    }
}
