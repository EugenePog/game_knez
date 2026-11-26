using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable scenario script entry: an order the scenario itself gives at a fixed tick — for benchmarks, tests, set
    /// pieces and the orders a Unity session recorded — to a squad (move, like MoveSquad; formation change, like
    /// SetFormation) or to a Retainer (attach, detach, move, attack; M3 step 10). It goes through the same validation as
    /// the player's command but is not part of the command log: the scenario (and its fingerprint) already contains it.
    /// </summary>
    public sealed class ScriptedOrderDef
    {
        /// <summary>Creates a squad move entry; <paramref name="tick"/> is the tick the order is applied in (1 or later).</summary>
        public ScriptedOrderDef(int tick, string squadName, FixVec2 target, Angle? facing)
            : this(ScriptedOrderKind.Move, tick, DefChecks.Id(squadName, "squad"), null, target, facing, null, EntityId.None)
        {
        }

        private ScriptedOrderDef(ScriptedOrderKind kind, int tick, string? squadName, string? retainerName, FixVec2 target,
            Angle? facing, string? formationId, EntityId enemy, string? enemySquadName = null, Orders.SquadStance stance = Orders.SquadStance.Hold)
        {
            EnemySquadName = enemySquadName;
            Stance = stance;
            Kind = kind;
            Tick = DefChecks.Range(tick, 1, int.MaxValue, nameof(tick));
            SquadName = squadName;
            RetainerName = retainerName;
            Target = target;
            Facing = facing;
            FormationId = formationId;
            Enemy = enemy;
        }

        /// <summary>A formation change entry: at <paramref name="tick"/> the squad re-forms into <paramref name="formationId"/>.</summary>
        public static ScriptedOrderDef Formation(int tick, string squadName, string formationId) =>
            new ScriptedOrderDef(ScriptedOrderKind.Formation, tick, DefChecks.Id(squadName, "squad"), null, FixVec2.Zero, null,
                DefChecks.Id(formationId, "formation"), EntityId.None);

        /// <summary>The Retainer <paramref name="retainerName"/> joins the squad <paramref name="squadName"/>.</summary>
        public static ScriptedOrderDef Attach(int tick, string retainerName, string squadName) =>
            new ScriptedOrderDef(ScriptedOrderKind.AttachRetainer, tick, DefChecks.Id(squadName, "squad"),
                DefChecks.Id(retainerName, "retainer"), FixVec2.Zero, null, null, EntityId.None);

        /// <summary>The squad <paramref name="squadName"/> attacks the enemy squad <paramref name="enemySquadName"/> (M4 step 1).</summary>
        public static ScriptedOrderDef AttackSquad(int tick, string squadName, string enemySquadName) =>
            new ScriptedOrderDef(ScriptedOrderKind.AttackSquad, tick, DefChecks.Id(squadName, "squad"), null, FixVec2.Zero, null, null,
                EntityId.None, DefChecks.Id(enemySquadName, "enemy"));

        /// <summary>The squad <paramref name="squadName"/> takes <paramref name="stance"/> (M4 step 1).</summary>
        public static ScriptedOrderDef SetStance(int tick, string squadName, Orders.SquadStance stance) =>
            new ScriptedOrderDef(ScriptedOrderKind.SetStance, tick, DefChecks.Id(squadName, "squad"), null, FixVec2.Zero, null, null,
                EntityId.None, null, stance);

        /// <summary>The Retainer <paramref name="retainerName"/> uses his ability (M3 step 11).</summary>
        public static ScriptedOrderDef Ability(int tick, string retainerName) =>
            new ScriptedOrderDef(ScriptedOrderKind.UseAbility, tick, null, DefChecks.Id(retainerName, "retainer"), FixVec2.Zero,
                null, null, EntityId.None);

        /// <summary>The Retainer <paramref name="retainerName"/> leaves his squad.</summary>
        public static ScriptedOrderDef Detach(int tick, string retainerName) =>
            new ScriptedOrderDef(ScriptedOrderKind.DetachRetainer, tick, null, DefChecks.Id(retainerName, "retainer"), FixVec2.Zero,
                null, null, EntityId.None);

        /// <summary>The Retainer <paramref name="retainerName"/>, standing alone, walks to <paramref name="target"/>.</summary>
        public static ScriptedOrderDef MoveRetainer(int tick, string retainerName, FixVec2 target, Angle? facing) =>
            new ScriptedOrderDef(ScriptedOrderKind.MoveRetainer, tick, null, DefChecks.Id(retainerName, "retainer"), target, facing,
                null, EntityId.None);

        /// <summary>The Retainer <paramref name="retainerName"/>, standing alone, goes at the enemy fighter <paramref name="enemy"/>.</summary>
        public static ScriptedOrderDef Attack(int tick, string retainerName, EntityId enemy) =>
            new ScriptedOrderDef(ScriptedOrderKind.RetainerAttack, tick, null, DefChecks.Id(retainerName, "retainer"), FixVec2.Zero,
                null, null, enemy);

        /// <summary>What the entry orders.</summary>
        public ScriptedOrderKind Kind { get; }

        /// <summary>The formation of a formation change, else null.</summary>
        public string? FormationId { get; }

        /// <summary>The tick the order is applied in.</summary>
        public int Tick { get; }

        /// <summary>The squad ordered (move, formation) or joined (attach); null for the other Retainer orders.</summary>
        public string? SquadName { get; }

        /// <summary>The Retainer ordered, or null for squad orders.</summary>
        public string? RetainerName { get; }

        /// <summary>Where a move goes.</summary>
        public FixVec2 Target { get; }

        /// <summary>Facing on arrival, or null for the march direction.</summary>
        public Angle? Facing { get; }

        /// <summary>The enemy fighter of a Retainer's attack.</summary>
        public EntityId Enemy { get; }

        /// <summary>The enemy squad of a squad's attack (M4 step 1), else null.</summary>
        public string? EnemySquadName { get; }

        /// <summary>The stance of a stance order (M4 step 1).</summary>
        public Orders.SquadStance Stance { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add((int)Kind);
            hash.Add(Tick);
            hash.Add(SquadName ?? string.Empty);
            hash.Add(RetainerName ?? string.Empty);
            hash.Add(Target.X.Raw);
            hash.Add(Target.Y.Raw);
            hash.Add(Facing.HasValue);
            hash.Add(Facing.HasValue ? (int)Facing.Value.Raw : 0);
            hash.Add(FormationId ?? string.Empty);
            hash.Add(Enemy.Value);
            hash.Add(EnemySquadName ?? string.Empty);
            hash.Add((int)Stance);
        }
    }
}
