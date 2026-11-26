namespace Knez.Sim.Defs
{
    /// <summary>What a scenario script entry orders (battle-sim.md §7–§8).</summary>
    public enum ScriptedOrderKind
    {
        /// <summary>A squad marches (MoveSquad).</summary>
        Move = 0,

        /// <summary>A squad changes formation (SetFormation).</summary>
        Formation = 1,

        /// <summary>A Retainer joins a squad (AttachRetainer, M3 step 10).</summary>
        AttachRetainer = 2,

        /// <summary>A Retainer leaves his squad (DetachRetainer).</summary>
        DetachRetainer = 3,

        /// <summary>A Retainer standing alone walks somewhere (MoveRetainer).</summary>
        MoveRetainer = 4,

        /// <summary>A Retainer standing alone goes at an enemy fighter (RetainerAttack).</summary>
        RetainerAttack = 5,

        /// <summary>A Retainer uses his ability (UseAbility, M3 step 11).</summary>
        UseAbility = 6,

        /// <summary>A squad attacks an enemy squad (AttackSquad, M4 step 1).</summary>
        AttackSquad = 7,

        /// <summary>A squad takes a stance (SetStance, M4 step 1).</summary>
        SetStance = 8,
    }
}
