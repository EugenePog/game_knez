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

        /// <summary>
        /// Room a fighter keeps around itself beyond touching: about 10 cm. Inside it, steering eases away from the other
        /// man. Smaller than the tightest formation gap (shield wall: 0.75 m spacing − 0.6 m of bodies), so men in their
        /// slots never push each other.
        /// </summary>
        public static readonly Fix PersonalSpace = Fix.FromRaw(Fix.OneRaw / 10);

        /// <summary>Time constant for easing out of someone's personal space: a fifth of the intrusion per tick.</summary>
        public const int SeparationTicks = 5;

        /// <summary>
        /// A man holds his ground — and weighs his formation's hold weight in a push — only while his squad has no order and
        /// he stands within this of his slot: 1.5 m. Beyond it he is trying to get somewhere, like anyone on the move.
        /// Without the second condition, a squad whose anchor arrived while its men are still blocked metres short of their
        /// slots would count as holding and shove an enemy line at full weight.
        /// </summary>
        public static readonly Fix HoldingReach = Fix.Ratio(3, 2);

        /// <summary>
        /// Most collision relaxation passes per tick, each over all pairs in id order; passes stop early once nothing
        /// overlaps. A push travels one link per pass against id order, so 3 passes left pushes 4+ men deep overlapping
        /// (7 % of fighter-ticks in a three-lines-deep test); 6 clears it at the same cost.
        /// </summary>
        public const int CollisionPasses = 6;
    }
}
