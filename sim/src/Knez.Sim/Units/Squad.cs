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
        private readonly List<FixVec2> _path;
        private readonly List<int> _pathFiles;
        private readonly List<AbilityEffect> _effects;

        internal Squad(EntityId id, int sideId, string name, FormationDef formation, FixVec2 anchor, Angle facing)
        {
            Id = id;
            SideId = sideId;
            Name = name;
            Formation = formation;
            Anchor = anchor;
            Facing = facing;
            _members = new List<EntityId>();
            _path = new List<FixVec2>();
            _pathFiles = new List<int>();
            _effects = new List<AbilityEffect>();
            PathRequestTick = -1;
            RegroupSince = -1;
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
            _path = new List<FixVec2>(source._path);
            _pathFiles = new List<int>(source._pathFiles);
            _effects = new List<AbilityEffect>(source._effects); // values: a list copy is a deep copy
            PathIndex = source.PathIndex;
            PathRequestTick = source.PathRequestTick;
            Files = source.Files;
            RegroupSince = source.RegroupSince;
            Frontage = source.Frontage;
            Reforming = source.Reforming;
            Withdrawing = source.Withdrawing;
            StartCount = source.StartCount;
            Broken = source.Broken;
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

        /// <summary>
        /// The route of the current move order: waypoints after the anchor's starting point, the last one the order's
        /// target. Empty while there is no order or the route is still being planned.
        /// </summary>
        public IReadOnlyList<FixVec2> Path => _path;

        /// <summary>The waypoint in <see cref="Path"/> the anchor is heading for.</summary>
        public int PathIndex { get; internal set; }

        /// <summary>The tick the squad asked for a route and is still waiting since (first come, first served), or -1.</summary>
        public int PathRequestTick { get; internal set; }

        /// <summary>
        /// What the squad does on reaching each waypoint of <see cref="Path"/>: −1 nothing, 0 widen back to its
        /// formation's width, k > 0 stop and regroup into k files before a narrow gap.
        /// </summary>
        public IReadOnlyList<int> PathFiles => _pathFiles;

        /// <summary>
        /// Files across the front of its formation, fixed when it forms up (spawn, formation change). Losses thin the rear
        /// first: the front narrows only once fewer men are left than this (decided 2026-11-21).
        /// </summary>
        public int Frontage { get; internal set; }

        /// <summary>Changing formation: men are moving to their new places and the squad marches at reduced pace.</summary>
        public bool Reforming { get; internal set; }

        /// <summary>
        /// Stepping back out of melee (battle-sim.md §11, decided 2026-11-22): it keeps facing the enemy and steps back at
        /// step-back pace until no enemy is within reach + <c>withdrawClearance</c>; then it marches on as usual. Set when
        /// an engaged squad steps back; cleared once clear, or by a new order.
        /// </summary>
        public bool Withdrawing { get; internal set; }

        /// <summary>Men the squad started the battle with, ± 1 as a Retainer joins or leaves alive (§13): a fallen man costs
        /// morale in proportion (battle-sim.md §12).</summary>
        public int StartCount { get; internal set; }

        /// <summary>Broken: more than half its men were routing, so all of them run; its order is dropped. It re-forms
        /// where its rallied men stand once more than half are back (battle-sim.md §12).</summary>
        public bool Broken { get; internal set; }

        /// <summary>Files the squad is narrowed to for a gap, or 0 when it stands in its formation's own width.</summary>
        public int Files { get; internal set; }

        /// <summary>The tick the squad stopped to regroup and is still waiting since, or -1.</summary>
        public int RegroupSince { get; internal set; }

        /// <summary>The path list itself, for systems.</summary>
        internal List<FixVec2> PathList => _path;

        /// <summary>The waypoint actions list itself, for systems (same length as <see cref="PathList"/>).</summary>
        internal List<int> PathFilesList => _pathFiles;

        /// <summary>Forgets the route (the order ended or was replaced), and with it any withdrawal. The squad keeps its
        /// current width.</summary>
        internal void ClearPath()
        {
            Withdrawing = false;
            _path.Clear();
            _pathFiles.Clear();
            PathIndex = 0;
            PathRequestTick = -1;
            RegroupSince = -1;
        }

        /// <summary>Member fighter ids, sorted by id.</summary>
        public IReadOnlyList<EntityId> Members => _members;

        /// <summary>The members list itself, for systems (index with <c>for</c>).</summary>
        internal List<EntityId> MemberList => _members;

        /// <summary>Lasting abilities used on the squad (M3 step 11), one per kind, in the order first used; an entry whose
        /// <see cref="AbilityEffect.Until"/> has passed no longer acts but stays until the kind is used again.</summary>
        public IReadOnlyList<AbilityEffect> Effects => _effects;

        internal List<AbilityEffect> EffectList => _effects;

        internal Squad Clone() => new Squad(this);
    }
}
