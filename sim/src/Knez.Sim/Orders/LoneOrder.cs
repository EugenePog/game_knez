using Knez.Sim.Numerics;

namespace Knez.Sim.Orders
{
    /// <summary>
    /// The order of a Retainer standing alone (battle-sim.md §13, M3 step 10): move to <see cref="Target"/> (then face
    /// <see cref="Facing"/> when <see cref="HasFacing"/>), or attack <see cref="Enemy"/>. A value: copied with the fighter.
    /// For now he walks straight, sliding along walls; planned routes for a man alone come later.
    /// </summary>
    public readonly struct LoneOrder
    {
        private LoneOrder(LoneOrderKind kind, FixVec2 target, Angle facing, bool hasFacing, EntityId enemy)
        {
            Kind = kind;
            Target = target;
            Facing = facing;
            HasFacing = hasFacing;
            Enemy = enemy;
        }

        /// <summary>No order.</summary>
        public static LoneOrder None => default;

        /// <summary>Walk to <paramref name="target"/>; then face <paramref name="facing"/>, or keep the march's facing.</summary>
        public static LoneOrder Move(FixVec2 target, Angle? facing) =>
            new LoneOrder(LoneOrderKind.Move, target, facing ?? Angle.Zero, facing.HasValue, EntityId.None);

        /// <summary>Go at <paramref name="enemy"/> and fight him.</summary>
        public static LoneOrder Attack(EntityId enemy) => new LoneOrder(LoneOrderKind.Attack, FixVec2.Zero, Angle.Zero, false, enemy);

        /// <summary>What kind of order.</summary>
        public LoneOrderKind Kind { get; }

        /// <summary>Where a move goes.</summary>
        public FixVec2 Target { get; }

        /// <summary>The facing on arrival, when <see cref="HasFacing"/>.</summary>
        public Angle Facing { get; }

        /// <summary>Whether a move ends with a facing.</summary>
        public bool HasFacing { get; }

        /// <summary>The enemy an attack goes at.</summary>
        public EntityId Enemy { get; }
    }
}
