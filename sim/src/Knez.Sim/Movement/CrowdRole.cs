namespace Knez.Sim.Movement
{
    /// <summary>What a fighter is doing for the crowding rules this tick (battle-sim.md §10).</summary>
    internal enum CrowdRole : byte
    {
        /// <summary>Neither: dead, without a squad, or off his slot with no order (e.g. shoved out of a holding line).</summary>
        Loose = 0,

        /// <summary>Holding his ground: his squad has no order and he stands within 1.5 m of his slot.</summary>
        Holding = 1,

        /// <summary>On orders: his squad has a move order (it lasts until the squad is formed up at the target).</summary>
        Ordered = 2,
    }
}
