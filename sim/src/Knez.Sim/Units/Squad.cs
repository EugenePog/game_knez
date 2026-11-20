using System.Collections.Generic;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;
using Knez.Sim.Orders;

namespace Knez.Sim.Units
{
    /// <summary>
    /// A squad (отряд): fighters that move and fight as one through orders. The anchor (centre of the front rank) and
    /// facing place the formation's slots; each member steers to its slot (battle-sim.md §10).
    /// </summary>
    public sealed class Squad : IHasEntityId
    {
        private readonly List<EntityId> _members;

        internal Squad(EntityId id, int sideId, string name, FormationDef formation, FixVec2 anchor, Angle facing)
        {
            Id = id;
            SideId = sideId;
            Name = name;
            Formation = formation;
            Anchor = anchor;
            Facing = facing;
            _members = new List<EntityId>();
        }

        private Squad(Squad source)
        {
            Id = source.Id;
            SideId = source.SideId;
            Name = source.Name;
            Formation = source.Formation;
            Anchor = source.Anchor;
            Facing = source.Facing;
            Order = source.Order;
            Velocity = source.Velocity;
            Spin = source.Spin;
            _members = new List<EntityId>(source._members);
        }

        /// <summary>Identity; squads are kept sorted by it.</summary>
        public EntityId Id { get; }

        /// <summary>Side the squad belongs to.</summary>
        public int SideId { get; }

        /// <summary>Display name from the scenario.</summary>
        public string Name { get; }

        /// <summary>Current formation; immutable def, shared.</summary>
        public FormationDef Formation { get; internal set; }

        /// <summary>Centre of the front rank, metres.</summary>
        public FixVec2 Anchor { get; internal set; }

        /// <summary>Direction the formation faces.</summary>
        public Angle Facing { get; internal set; }

        /// <summary>What the squad is doing on orders; <see cref="SquadOrder.None"/> when it holds.</summary>
        public SquadOrder Order { get; internal set; }

        /// <summary>How far the anchor moved this tick, metres per tick (zero while holding or turning in place).</summary>
        public FixVec2 Velocity { get; internal set; }

        /// <summary>How far the facing turned this tick, in <see cref="Angle"/> units (positive = counter-clockwise).
        /// With <see cref="Velocity"/> it gives each slot's own motion, which members follow.</summary>
        public int Spin { get; internal set; }

        /// <summary>Member fighter ids, sorted by id.</summary>
        public IReadOnlyList<EntityId> Members => _members;

        /// <summary>The members list itself, for systems (index with <c>for</c>).</summary>
        internal List<EntityId> MemberList => _members;

        internal Squad Clone() => new Squad(this);
    }
}
