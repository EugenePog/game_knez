using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Units
{
    /// <summary>
    /// A combatant (боец). Heroes will be fighters too, with extra hero data. M1 step 1 has identity, type, side,
    /// transform and squad membership; body, morale and combat data arrive with their milestones (battle-sim.md §6).
    /// </summary>
    public sealed class Fighter : IHasEntityId
    {
        internal Fighter(EntityId id, FighterDef def, int sideId, FixVec2 position, Angle facing)
        {
            Id = id;
            Def = def;
            SideId = sideId;
            Position = position;
            Facing = facing;
            SquadId = EntityId.None;
            SlotIndex = NoSlot;
            IsAlive = true;
        }

        /// <summary><see cref="SlotIndex"/> of a fighter outside any squad.</summary>
        public const int NoSlot = -1;

        /// <summary>Identity; fighters are kept sorted by it.</summary>
        public EntityId Id { get; }

        /// <summary>Fighter type from content; immutable and shared, never copied.</summary>
        public FighterDef Def { get; }

        /// <summary>Side the fighter belongs to (0 = player, 1 = enemy in phase 1).</summary>
        public int SideId { get; }

        /// <summary>Position in metres. Written only by systems and commands.</summary>
        public FixVec2 Position { get; internal set; }

        /// <summary>Direction the fighter faces.</summary>
        public Angle Facing { get; internal set; }

        /// <summary>The squad the fighter belongs to, or <see cref="EntityId.None"/>.</summary>
        public EntityId SquadId { get; internal set; }

        /// <summary>Formation slot within the squad (0 = front-left), or <see cref="NoSlot"/>.</summary>
        public int SlotIndex { get; internal set; }

        /// <summary>False once the fighter has died; dead fighters stay in the list until cleanup.</summary>
        public bool IsAlive { get; internal set; }

        /// <summary>Collision radius in metres, from <see cref="Def"/>.</summary>
        public Fix Radius => Def.Radius;

        // Every field is a value or an immutable def, so a shallow copy is a deep copy.
        internal Fighter Clone() => (Fighter)MemberwiseClone();
    }
}
