using Knez.Sim.Numerics;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Constants of the movement algorithms (battle-sim.md §10). These are solver settings, not balance: speeds and turn
    /// rates live in content defs. Revisit on the M1 bench (step 8); move a value to content if it turns out to shape feel.
    /// </summary>
    internal static class MovementRules
    {
        /// <summary>
        /// A squad turns in place until its front is within this of the march heading, then marches while finishing the
        /// turn: 30°. A fixed rule agreed on 2026-11-20 (not per formation); move it to FormationDef if formations should
        /// differ.
        /// </summary>
        public const int PivotToleranceUnits = Angle.UnitsPerTurn / 12;

        /// <summary>
        /// Time constant for closing the lag to the slot, on top of the slot's own motion: each tick a fighter closes
        /// 1/10 of it (10 ticks = 0.5 s; 1 m of lag reaches the 2 cm snap in about 37 ticks).
        /// </summary>
        public const int SlotCatchUpTicks = BattleState.TicksPerSecond / 2;

        /// <summary>The anchor counts as arrived within this distance: about 1 cm.</summary>
        public static readonly Fix ArriveDistance = Fix.FromRaw(Fix.OneRaw / 100);

        /// <summary>A fighter of a squad standing still steps exactly onto its slot within this distance (about 2 cm), so
        /// formations settle instead of creeping forever.</summary>
        public static readonly Fix SnapDistance = Fix.FromRaw(Fix.OneRaw / 50);
    }
}
