using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Morale;
using Knez.Sim.Numerics;

namespace Knez.Sim.Units
{
    /// <summary>
    /// A combatant (боец): a body, an experience level and a kit (M3 step 9, ADR-0005), with his side, place, squad,
    /// morale, wound and action (battle-sim.md §6). Retainers will be fighters too, with a Retainer part.
    /// </summary>
    public sealed class Fighter : IHasEntityId
    {
        internal Fighter(EntityId id, BodyDef body, ExperienceDef experience, KitDef kit, int sideId, FixVec2 position, Angle facing)
        {
            Id = id;
            Body = body;
            Experience = experience;
            Kit = kit;
            SideId = sideId;
            Position = position;
            Facing = facing;
            SquadId = EntityId.None;
            SlotIndex = NoSlot;
            IsAlive = true;
            Morale = experience.Morale;
        }

        /// <summary><see cref="SlotIndex"/> of a fighter outside any squad.</summary>
        public const int NoSlot = -1;

        /// <summary>Identity; fighters are kept sorted by it.</summary>
        public EntityId Id { get; }

        /// <summary>His body (size, speeds, turn rate); immutable and shared, never copied.</summary>
        public BodyDef Body { get; }

        /// <summary>How experienced he is; from the combat rules, shared.</summary>
        public ExperienceDef Experience { get; }

        /// <summary>What he carries: weapon, shield, body armour, head cover; from the combat rules, shared.</summary>
        public KitDef Kit { get; }

        /// <summary>Side the fighter belongs to (0 = player, 1 = enemy in phase 1).</summary>
        public int SideId { get; }

        /// <summary>Position in metres. Written only by systems and commands.</summary>
        public FixVec2 Position { get; internal set; }

        /// <summary>How far the fighter moves this tick, metres per tick (set by steering, applied by movement).</summary>
        public FixVec2 Velocity { get; internal set; }

        /// <summary>Direction the fighter faces.</summary>
        public Angle Facing { get; internal set; }

        /// <summary>
        /// How far behind his slot he is meant to be because his squad turned (battle-sim.md §10, soft wheel): his slot
        /// swings round with the squad, this keeps him where the swing left him, and it shrinks to zero over
        /// <c>MovementDef.TurnLagTicks</c>. He walks toward slot + this. Zero when he isn't following a slot.
        /// </summary>
        public FixVec2 TurnLag { get; internal set; }

        /// <summary>The squad the fighter belongs to, or <see cref="EntityId.None"/>.</summary>
        public EntityId SquadId { get; internal set; }

        /// <summary>Formation slot within the squad (0 = front-left), or <see cref="NoSlot"/>.</summary>
        public int SlotIndex { get; internal set; }

        /// <summary>
        /// True while he passes through the ranks of an allied squad holding its ground (battle-sim.md §10): set when, on
        /// orders, he walks into an allied holder who stands between him and his slot; kept while his body still overlaps
        /// such holders. Being shoved backwards into allies never sets it.
        /// </summary>
        public bool PassingThrough { get; internal set; }

        /// <summary>
        /// The enemy he is fighting: the nearest living enemy within his weapon's reach (body edge to body edge), ties by
        /// lower id, or <see cref="EntityId.None"/>. Chosen after movement each tick (battle-sim.md §11, M3 step 2).
        /// </summary>
        public EntityId Target { get; internal set; }

        /// <summary>His morale, 0..1000 (battle-sim.md §12); starts at his experience level's.</summary>
        public int Morale { get; internal set; }

        /// <summary>His state of mind: steady, shaken, wavering, routing, rallying or fled.</summary>
        public MoraleState MoraleState { get; internal set; }

        /// <summary>True while he runs: he flees, ignores orders and strikes no one.</summary>
        public bool IsRouting => MoraleState == MoraleState.Routing;

        /// <summary>How badly he is hurt; <see cref="WoundLevel.Down"/> means he has fallen (<see cref="IsAlive"/> false).</summary>
        public WoundLevel Wound { get; internal set; }

        /// <summary>What his weapon is doing: idle, winding up a blow, or recovering from one (battle-sim.md §11).</summary>
        public FighterAction Action { get; internal set; }

        /// <summary>Ticks left in <see cref="Action"/> (0 when idle).</summary>
        public int ActionTicks { get; internal set; }

        /// <summary>True while he has a <see cref="Target"/>: in melee, he holds his ground and his squad stops its march.</summary>
        public bool IsEngaged => !Target.IsNone;

        /// <summary>False once the fighter is out of the fight — fallen (<see cref="Wound"/> Down) or fled the field
        /// (<see cref="MoraleState"/> Fled); "alive" predates morale and means "in the fight". Such fighters stay in the list for the view.</summary>
        public bool IsAlive { get; internal set; }

        /// <summary>Collision radius in metres, from <see cref="Body"/>.</summary>
        public Fix Radius => Body.Radius;

        // Every field is a value or an immutable def, so a shallow copy is a deep copy.
        internal Fighter Clone() => (Fighter)MemberwiseClone();
    }
}
