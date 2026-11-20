using System;
using System.Globalization;

namespace Knez.Sim
{
    /// <summary>
    /// Identity of an entity in a battle. Allocated monotonically by <see cref="BattleState"/>, never reused;
    /// <see cref="None"/> (0) means "no entity". Lists of entities are kept sorted by id, and ties are broken by id.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        /// <summary>No entity.</summary>
        public static readonly EntityId None = default;

        /// <summary>The raw id; 0 for <see cref="None"/>.</summary>
        public readonly int Value;

        /// <summary>Wraps a raw id. New entities get theirs from <see cref="BattleState"/>.</summary>
        public EntityId(int value)
        {
            Value = value;
        }

        /// <summary>True for <see cref="None"/>.</summary>
        public bool IsNone => Value == 0;

        /// <summary>Equality.</summary>
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;

        /// <summary>Inequality.</summary>
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;

        /// <inheritdoc/>
        public bool Equals(EntityId other) => Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is EntityId other && Value == other.Value;

        /// <inheritdoc/>
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

        /// <inheritdoc/>
        public override int GetHashCode() => Value;

        /// <inheritdoc/>
        public override string ToString() => "#" + Value.ToString(CultureInfo.InvariantCulture);
    }
}
