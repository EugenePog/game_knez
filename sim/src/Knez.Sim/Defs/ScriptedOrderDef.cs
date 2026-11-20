using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable scenario script entry: a move order the scenario itself gives a squad at a fixed tick — for benchmarks,
    /// tests and set pieces. It goes through the same validation as a player's MoveSquad command but is not part of the
    /// command log: the scenario (and its fingerprint) already contains it.
    /// </summary>
    public sealed class ScriptedOrderDef
    {
        /// <summary>Creates an entry; <paramref name="tick"/> is the tick the order is applied in (1 or later).</summary>
        public ScriptedOrderDef(int tick, string squadName, FixVec2 target, Angle? facing)
        {
            Tick = DefChecks.Range(tick, 1, int.MaxValue, nameof(tick));
            SquadName = DefChecks.Id(squadName, "squad");
            Target = target;
            Facing = facing;
        }

        /// <summary>The tick the order is applied in, at the start of the tick like a command.</summary>
        public int Tick { get; }

        /// <summary>The squad's scenario name.</summary>
        public string SquadName { get; }

        /// <summary>Where the anchor (front-rank centre) goes, metres.</summary>
        public FixVec2 Target { get; }

        /// <summary>Facing on arrival, or null for the direction of the march.</summary>
        public Angle? Facing { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Tick);
            hash.Add(SquadName);
            hash.Add(Target.X.Raw);
            hash.Add(Target.Y.Raw);
            hash.Add(Facing.HasValue);
            hash.Add(Facing.HasValue ? (int)Facing.Value.Raw : 0);
        }
    }
}
