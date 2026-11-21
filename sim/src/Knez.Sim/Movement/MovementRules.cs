using Knez.Sim.Numerics;

namespace Knez.Sim.Movement
{
    /// <summary>
    /// Numerical constants of the movement algorithms (battle-sim.md §10) that keep the maths exact or safe; nothing here
    /// shapes how the battle feels. Everything that does is in content: <c>movement.json</c> (<see cref="Defs.MovementDef"/>),
    /// fighter and formation defs.
    /// </summary>
    internal static class MovementRules
    {
        /// <summary>The anchor counts as arrived within this distance: about 1 cm.</summary>
        public static readonly Fix ArriveDistance = Fix.FromRaw(Fix.OneRaw / 100);

        /// <summary>A fighter of a squad standing still steps exactly onto its slot within this distance (about 2 cm), so
        /// formations settle instead of creeping forever.</summary>
        public static readonly Fix SnapDistance = Fix.FromRaw(Fix.OneRaw / 50);

        /// <summary>Spacing of the points a straight stretch of route is checked at for room: 25 cm.</summary>
        public static readonly Fix RouteSampleStep = Fix.Ratio(1, 4);

        /// <summary>
        /// How much less room a shortcut may sample than the planned cells had: 23 cm. Between two cell centres a diagonal
        /// cell route can come 35 cm (half a cell diagonal) closer to a wall than its cells, a straight one 25 cm; a
        /// shortcut sampled every 25 cm comes at most 12.5 cm closer than its samples, so 35 cm in all. A shortcut is
        /// thus never tighter than a diagonal cell route and at most 10 cm tighter than a straight one.
        /// </summary>
        public static readonly Fix ShortcutSlack = Fix.Ratio(23, 100);
    }
}
