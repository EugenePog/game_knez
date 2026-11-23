using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable morale rules from content (<c>content/defs/morale.json</c>, battle-sim.md §12). Morale is 0..1000 per
    /// man; he starts with his experience level's <see cref="ExperienceDef.Morale"/>. Deltas are whole points; rates are
    /// per second, spread exactly over the ticks of a second. Shared by the whole battle and fingerprinted.
    /// </summary>
    public sealed class MoraleDef
    {
        /// <summary>Creates the rules; throws <see cref="ArgumentException"/> on values outside their sanity bounds.</summary>
        public MoraleDef(int shaken, int wavering, int routing, int hysteresis, Fix lossWeight, int ownLightWound,
            int ownHeavyWound, int mateHeavyWound, int flankBlow, int rearBlow, int mateFlankBlow, int mateRearBlow,
            int mateRouts, int allySquadBroke, int enemySquadBroke, Fix nearby, int recoveryPerSecond, Fix safeDistance,
            int rallyAt, int breakShare, int reformShare, int waveringHitPenalty)
        {
            Shaken = DefChecks.Permille(shaken, "shaken");
            Wavering = DefChecks.Permille(wavering, "wavering");
            Routing = DefChecks.Permille(routing, "routing");
            if (!(routing < wavering && wavering < shaken))
            {
                throw new ArgumentException($"thresholds must fall: routing < wavering < shaken (got {routing}, {wavering}, {shaken}).");
            }

            Hysteresis = DefChecks.Range(hysteresis, 0, 300, nameof(hysteresis));
            LossWeight = DefChecks.NonNegative(lossWeight, 10, string.Empty, nameof(lossWeight));
            OwnLightWound = DefChecks.Permille(ownLightWound, nameof(ownLightWound));
            OwnHeavyWound = DefChecks.Permille(ownHeavyWound, nameof(ownHeavyWound));
            MateHeavyWound = DefChecks.Permille(mateHeavyWound, nameof(mateHeavyWound));
            FlankBlow = DefChecks.Permille(flankBlow, nameof(flankBlow));
            RearBlow = DefChecks.Permille(rearBlow, nameof(rearBlow));
            MateFlankBlow = DefChecks.Permille(mateFlankBlow, nameof(mateFlankBlow));
            MateRearBlow = DefChecks.Permille(mateRearBlow, nameof(mateRearBlow));
            MateRouts = DefChecks.Permille(mateRouts, nameof(mateRouts));
            AllySquadBroke = DefChecks.Permille(allySquadBroke, nameof(allySquadBroke));
            EnemySquadBroke = DefChecks.Permille(enemySquadBroke, nameof(enemySquadBroke));
            Nearby = DefChecks.PositiveMetres(nearby, 100, nameof(nearby));
            RecoveryPerSecond = DefChecks.Range(recoveryPerSecond, 0, 1000, nameof(recoveryPerSecond));
            SafeDistance = DefChecks.PositiveMetres(safeDistance, 100, nameof(safeDistance));
            RallyAt = DefChecks.Permille(rallyAt, nameof(rallyAt));
            if (rallyAt < routing)
            {
                throw new ArgumentException($"rallyAt must be at least routing ({routing}) (got {rallyAt}).");
            }

            BreakShare = DefChecks.Range(breakShare, 1, 999, nameof(breakShare));
            ReformShare = DefChecks.Range(reformShare, 1, 999, nameof(reformShare));
            WaveringHitPenalty = DefChecks.Permille(waveringHitPenalty, nameof(waveringHitPenalty));
            Fingerprint = ComputeFingerprint();
        }

        /// <summary>
        /// The values in <c>content/defs/morale.json</c> when this was written (2026-11-23), rounded as the content reader
        /// rounds decimals (a test keeps the fingerprints equal). For tests and scenarios built in code.
        /// </summary>
        public static readonly MoraleDef Standard = new MoraleDef(shaken: 600, wavering: 400, routing: 200, hysteresis: 50,
            lossWeight: Fix.FromInt(2), ownLightWound: 50, ownHeavyWound: 150, mateHeavyWound: 20, flankBlow: 20, rearBlow: 40,
            mateFlankBlow: 5, mateRearBlow: 10, mateRouts: 30, allySquadBroke: 150, enemySquadBroke: 100, nearby: Fix.FromInt(20),
            recoveryPerSecond: 20, safeDistance: Fix.FromInt(10), rallyAt: 350, breakShare: 500, reformShare: 500,
            waveringHitPenalty: 40);

        /// <summary>Below this a steady man is shaken.</summary>
        public int Shaken { get; }

        /// <summary>Below this he wavers: his blows get through less.</summary>
        public int Wavering { get; }

        /// <summary>Below this he routs: flees toward his side's map edge and ignores orders.</summary>
        public int Routing { get; }

        /// <summary>To climb back a state he needs this much above its threshold, so a man on the edge doesn't flicker.</summary>
        public int Hysteresis { get; }

        /// <summary>A fallen squadmate costs every man of the squad this × 1000 / the squad's starting size (decided
        /// 2026-11-23: 2, so a squad breaks after losing about a quarter).</summary>
        public Fix LossWeight { get; }

        /// <summary>A man's own light wound costs him this.</summary>
        public int OwnLightWound { get; }

        /// <summary>A man's own heavy wound costs him this.</summary>
        public int OwnHeavyWound { get; }

        /// <summary>A squadmate's heavy wound costs every other man of the squad this.</summary>
        public int MateHeavyWound { get; }

        /// <summary>A blow at a man from his flank (landed or not) costs him this.</summary>
        public int FlankBlow { get; }

        /// <summary>A blow at a man from his rear costs him this.</summary>
        public int RearBlow { get; }

        /// <summary>A blow at a squadmate from his flank costs every other man of the squad this.</summary>
        public int MateFlankBlow { get; }

        /// <summary>A blow at a squadmate from his rear costs every other man of the squad this.</summary>
        public int MateRearBlow { get; }

        /// <summary>A squadmate starting to rout costs every other man of the squad this.</summary>
        public int MateRouts { get; }

        /// <summary>An allied squad breaking within <see cref="Nearby"/> costs every man of a squad this.</summary>
        public int AllySquadBroke { get; }

        /// <summary>An enemy squad breaking within <see cref="Nearby"/> gives every man of a squad this.</summary>
        public int EnemySquadBroke { get; }

        /// <summary>How near (anchor to anchor, metres) a breaking squad must be to shake or hearten others.</summary>
        public Fix Nearby { get; }

        /// <summary>Morale a man regains per second while no enemy is within <see cref="SafeDistance"/>, up to his
        /// starting morale.</summary>
        public int RecoveryPerSecond { get; }

        /// <summary>Metres from the nearest enemy (edge to edge) at which a man feels safe enough to recover.</summary>
        public Fix SafeDistance { get; }

        /// <summary>A routing man who has recovered to this rallies and goes back to his squad (decided 2026-11-23).</summary>
        public int RallyAt { get; }

        /// <summary>A squad breaks — every man routs — when more than this share (‰) of its men in the fight rout
        /// (decided 2026-11-23: more than half).</summary>
        public int BreakShare { get; }

        /// <summary>A broken squad re-forms when more than this share (‰) of its men in the fight are back.</summary>
        public int ReformShare { get; }

        /// <summary>Permille taken off the chance of a wavering man's blows.</summary>
        public int WaveringHitPenalty { get; }

        /// <summary>FNV-1a hash of every number above, in order; part of the scenario's fingerprint.</summary>
        public ulong Fingerprint { get; }

        internal void AddTo(ref Fnv1a64 hash) => hash.Add(Fingerprint);

        private ulong ComputeFingerprint()
        {
            var hash = Fnv1a64.Start();
            hash.Add(Shaken);
            hash.Add(Wavering);
            hash.Add(Routing);
            hash.Add(Hysteresis);
            hash.Add(LossWeight.Raw);
            hash.Add(OwnLightWound);
            hash.Add(OwnHeavyWound);
            hash.Add(MateHeavyWound);
            hash.Add(FlankBlow);
            hash.Add(RearBlow);
            hash.Add(MateFlankBlow);
            hash.Add(MateRearBlow);
            hash.Add(MateRouts);
            hash.Add(AllySquadBroke);
            hash.Add(EnemySquadBroke);
            hash.Add(Nearby.Raw);
            hash.Add(RecoveryPerSecond);
            hash.Add(SafeDistance.Raw);
            hash.Add(RallyAt);
            hash.Add(BreakShare);
            hash.Add(ReformShare);
            hash.Add(WaveringHitPenalty);
            return hash.Value;
        }
    }
}
