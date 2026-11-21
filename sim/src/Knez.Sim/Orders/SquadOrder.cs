using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Orders
{
    /// <summary>
    /// A squad's current order (приказ), inside the sim. Commands and the scenario script set it; <see cref="OrderSystem"/>
    /// carries it out and clears it when done. Immutable value: replacing it is the only way to change it.
    /// </summary>
    public readonly struct SquadOrder : IEquatable<SquadOrder>
    {
        private SquadOrder(SquadOrderKind kind, FixVec2 target, Angle facing, bool facingFromMarch)
        {
            Kind = kind;
            Target = target;
            Facing = facing;
            FacingFromMarch = facingFromMarch;
        }

        /// <summary>No order.</summary>
        public static readonly SquadOrder None = default;

        /// <summary>What kind of order this is.</summary>
        public SquadOrderKind Kind { get; }

        /// <summary>Move: where the anchor goes.</summary>
        public FixVec2 Target { get; }

        /// <summary>Move: the facing on arrival (already resolved: an explicit one, or the march direction).</summary>
        public Angle Facing { get; }

        /// <summary>
        /// Move: no facing was ordered, so the squad ends facing the way it marched. <see cref="Facing"/> starts as the
        /// straight-line direction to the target and becomes the route's last leg once the route is planned.
        /// </summary>
        public bool FacingFromMarch { get; }

        /// <summary>March to <paramref name="target"/>, then face <paramref name="facing"/>.</summary>
        public static SquadOrder Move(FixVec2 target, Angle facing, bool facingFromMarch = false) =>
            new SquadOrder(SquadOrderKind.Move, target, facing, facingFromMarch);

        /// <inheritdoc/>
        public bool Equals(SquadOrder other) =>
            Kind == other.Kind && Target == other.Target && Facing == other.Facing && FacingFromMarch == other.FacingFromMarch;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is SquadOrder other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (int)Kind; // state is hashed by StateHasher, never by this

        /// <inheritdoc/>
        public override string ToString() => Kind == SquadOrderKind.None ? "none" : $"move to {Target} facing {Facing}";
    }
}
