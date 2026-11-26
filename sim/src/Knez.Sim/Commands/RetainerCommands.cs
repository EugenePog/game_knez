using Knez.Sim.Units;

namespace Knez.Sim.Commands
{
    /// <summary>Checks the Retainer commands share (battle-sim.md §8, M3 step 10).</summary>
    internal static class RetainerCommands
    {
        /// <summary>The fighter <paramref name="id"/> as a Retainer of <paramref name="sideId"/> who can take orders, or the
        /// reason he can't: unknown-entity (no such Retainer), wrong-side, not-alive (down or fled), routing.</summary>
        public static RejectReason Check(BattleState state, int sideId, EntityId id, out Fighter? retainer)
        {
            retainer = state.FindFighter(id);
            if (retainer == null || !retainer.IsRetainer)
            {
                retainer = null;
                return RejectReason.UnknownEntity;
            }

            if (retainer.SideId != sideId)
            {
                return RejectReason.WrongSide;
            }

            if (!retainer.IsAlive)
            {
                return RejectReason.NotAlive;
            }

            return retainer.IsRouting ? RejectReason.Routing : RejectReason.None;
        }
    }
}
