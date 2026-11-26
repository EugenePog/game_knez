using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable enemy-AI rules from content (<c>content/defs/ai.json</c>, battle-sim.md §14, M4 step 2; decided by the user
    /// 2026-11-26). Strength is men in the fight, a heavily wounded man counting half. Shared by the whole battle and
    /// fingerprinted.
    /// </summary>
    public sealed class AiDef
    {
        /// <summary>Creates the rules; throws <see cref="ArgumentException"/> on values outside their sanity bounds.</summary>
        public AiDef(int attackAt, int holdBelow, int leaveBelow, int regroupMorale, int readyMorale, Fix regroupDistance, Fix helpBonus,
            Fix flankOffset, int maxOnTarget, Fix jitter, int cryEnemies, Fix cryContact, Fix closeRanksRange, int standShare)
        {
            AttackAt = DefChecks.Range(attackAt, 0, 10 * DefChecks.Whole, nameof(attackAt));
            HoldBelow = DefChecks.Range(holdBelow, 0, 10 * DefChecks.Whole, nameof(holdBelow));
            LeaveBelow = DefChecks.Range(leaveBelow, 0, 10 * DefChecks.Whole, nameof(leaveBelow));
            if (holdBelow > attackAt)
            {
                throw new ArgumentException($"holdBelow ({holdBelow}) must not be above attackAt ({attackAt}): a squad would stop an attack it may start.");
            }

            if (leaveBelow > holdBelow)
            {
                throw new ArgumentException($"leaveBelow ({leaveBelow}) must not be above holdBelow ({holdBelow}): a side would leave while it attacks.");
            }

            RegroupMorale = DefChecks.Permille(regroupMorale, nameof(regroupMorale));
            ReadyMorale = DefChecks.Permille(readyMorale, nameof(readyMorale));
            if (readyMorale < regroupMorale)
            {
                throw new ArgumentException($"readyMorale ({readyMorale}) must be at least regroupMorale ({regroupMorale}).");
            }

            RegroupDistance = DefChecks.PositiveMetres(regroupDistance, 100, nameof(regroupDistance));
            HelpBonus = DefChecks.NonNegative(helpBonus, 100, "m", nameof(helpBonus));
            FlankOffset = DefChecks.PositiveMetres(flankOffset, 50, nameof(flankOffset));
            MaxOnTarget = DefChecks.Range(maxOnTarget, 1, 8, nameof(maxOnTarget));
            Jitter = DefChecks.NonNegative(jitter, 50, "m", nameof(jitter));
            CryEnemies = DefChecks.Range(cryEnemies, 1, 64, nameof(cryEnemies));
            CryContact = DefChecks.PositiveMetres(cryContact, 50, nameof(cryContact));
            CloseRanksRange = DefChecks.PositiveMetres(closeRanksRange, 50, nameof(closeRanksRange));
            StandShare = DefChecks.Range(standShare, 1, DefChecks.Whole, nameof(standShare));
            Fingerprint = ComputeFingerprint();
        }

        /// <summary>The values in <c>content/defs/ai.json</c> when this was written (2026-11-26); a test keeps the
        /// fingerprints equal. For tests and scenarios built in code.</summary>
        public static readonly AiDef Standard = new AiDef(attackAt: 800, holdBelow: 700, leaveBelow: 333, regroupMorale: 400, readyMorale: 600,
            regroupDistance: Fix.FromInt(20), helpBonus: Fix.FromInt(10), flankOffset: Fix.FromInt(8), maxOnTarget: 2,
            jitter: Fix.FromInt(5), cryEnemies: 4, cryContact: Fix.FromInt(5), closeRanksRange: Fix.FromInt(10), standShare: 250);

        /// <summary>The side attacks while its strength is at least this share (‰) of the enemy's; else it holds its ground
        /// in Advance (decided: 0.8).</summary>
        public int AttackAt { get; }

        /// <summary>A squad already attacking holds only once its side's strength falls below this share (‰) of the enemy's,
        /// so odds hovering at <see cref="AttackAt"/> don't start and stop its march every second (decided by the user 2026-11-26: 0.7).</summary>
        public int HoldBelow { get; }

        /// <summary>The side leaves the field once its strength falls below this share (‰) of the enemy's (decided: a
        /// third); it never comes back.</summary>
        public int LeaveBelow { get; }

        /// <summary>A squad not in melee whose men in the fight average morale below this falls back to regroup.</summary>
        public int RegroupMorale { get; }

        /// <summary>A regrouping squad fights again once its men in the fight average this (or what they started with, if
        /// less: they recover only that far).</summary>
        public int ReadyMorale { get; }

        /// <summary>How far a squad falls back to regroup, toward its nearest ally (or its start), metres.</summary>
        public Fix RegroupDistance { get; }

        /// <summary>An enemy squad an ally already fights counts this much nearer when choosing a target, metres.</summary>
        public Fix HelpBonus { get; }

        /// <summary>The second squad on a target, standing in its front, first marches to a point this far beyond the
        /// target's open flank, metres.</summary>
        public Fix FlankOffset { get; }

        /// <summary>At most this many squads on one target while another enemy squad stands free.</summary>
        public int MaxOnTarget { get; }

        /// <summary>Up to this much (metres) is added at random (the <c>ai</c> stream) to each target's distance, so
        /// near-ties go differently from battle to battle.</summary>
        public Fix Jitter { get; }

        /// <summary>Battle cry: at least this many enemy men in the fight within its radius…</summary>
        public int CryEnemies { get; }

        /// <summary>…and his squad in melee or an enemy within this, metres (edge to edge).</summary>
        public Fix CryContact { get; }

        /// <summary>Close ranks: his squad in melee, or a man of an enemy squad attacking it within this, metres (edge to
        /// edge).</summary>
        public Fix CloseRanksRange { get; }

        /// <summary>Stand to the death: his squad in melee and at least this share (‰) of its living men wavering or
        /// running.</summary>
        public int StandShare { get; }

        /// <summary>FNV-1a hash of every number above, in order; part of the scenario's fingerprint.</summary>
        public ulong Fingerprint { get; }

        internal void AddTo(ref Fnv1a64 hash) => hash.Add(Fingerprint);

        private ulong ComputeFingerprint()
        {
            var hash = Fnv1a64.Start();
            hash.Add(AttackAt);
            hash.Add(HoldBelow);
            hash.Add(LeaveBelow);
            hash.Add(RegroupMorale);
            hash.Add(ReadyMorale);
            hash.Add(RegroupDistance.Raw);
            hash.Add(HelpBonus.Raw);
            hash.Add(FlankOffset.Raw);
            hash.Add(MaxOnTarget);
            hash.Add(Jitter.Raw);
            hash.Add(CryEnemies);
            hash.Add(CryContact.Raw);
            hash.Add(CloseRanksRange.Raw);
            hash.Add(StandShare);
            return hash.Value;
        }
    }
}
