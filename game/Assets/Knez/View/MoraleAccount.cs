#nullable enable
using System.Collections.Generic;
using Knez.Sim.Morale;

namespace Knez.Game.View
{
    /// <summary>
    /// What moved some men's morale since a point — the battle's start, or their squad's last re-form — averaged over them
    /// (M3 step 8): where it started, where it is now, and how much each reason moved it. Recovery away from enemies isn't
    /// reported reason by reason by the sim, so it is what's left over.
    /// </summary>
    public sealed class MoraleAccount
    {
        /// <summary>Creates an account.</summary>
        public MoraleAccount(int men, int start, int now, IReadOnlyList<(MoraleReason Reason, int Average)> reasons, int recovered)
        {
            Men = men;
            Start = start;
            Now = now;
            Reasons = reasons;
            Recovered = recovered;
        }

        /// <summary>Men averaged over (0: nobody left).</summary>
        public int Men { get; }

        /// <summary>Their average morale at the start of the account.</summary>
        public int Start { get; }

        /// <summary>Their average morale now.</summary>
        public int Now { get; }

        /// <summary>Each reason that moved it, as the average change per man (negative = lost), largest first; zeros left
        /// out.</summary>
        public IReadOnlyList<(MoraleReason Reason, int Average)> Reasons { get; }

        /// <summary>Average morale regained out of the enemy's reach (now − start − every reason).</summary>
        public int Recovered { get; }
    }
}
