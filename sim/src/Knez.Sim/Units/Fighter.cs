using Knez.Sim.Numerics;

namespace Knez.Sim.Units
{
    /// <summary>
    /// A combatant (боец). Heroes will be fighters too, with extra hero data. M0 keeps only identity, side and
    /// position; movement, body, morale and combat data arrive with their milestones (battle-sim.md §6).
    /// </summary>
    public sealed class Fighter
    {
        internal Fighter(EntityId id, int sideId, FixVec2 position)
        {
            Id = id;
            SideId = sideId;
            Position = position;
            IsAlive = true;
        }

        /// <summary>Identity; fighters are kept sorted by it.</summary>
        public EntityId Id { get; }

        /// <summary>Side the fighter belongs to (0 = player, 1 = enemy in phase 1).</summary>
        public int SideId { get; }

        /// <summary>Position in metres. Written only by systems and commands.</summary>
        public FixVec2 Position { get; internal set; }

        /// <summary>False once the fighter has died; dead fighters stay in the list until cleanup.</summary>
        public bool IsAlive { get; internal set; }

        internal Fighter Clone() => (Fighter)MemberwiseClone();
    }
}
