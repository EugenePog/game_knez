using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// A Retainer's active ability (D-023, D-025; <c>content/defs/retainers.json</c>, a trait's <c>ability</c>;
    /// battle-sim.md §13, M3 step 11): what it is, its cooldown, and the numbers of its effect. Each kind has its own
    /// numbers; the ones a kind doesn't use are zero. Immutable, shared.
    /// </summary>
    public sealed class AbilityDef : IDefId
    {
        /// <summary>Sanity bound on a cooldown, in seconds.</summary>
        public const int MaxCooldownSeconds = 600;

        /// <summary>Sanity bound on how long an effect lasts, in seconds.</summary>
        public const int MaxDurationSeconds = 120;

        /// <summary>Sanity bound on a radius, in metres.</summary>
        public const int MaxRadius = 50;

        /// <summary>Content ids in <see cref="AbilityKind"/> order, for messages.</summary>
        public const string Ids = "battle-cry, close-ranks, promise-loot, stand-to-the-death, front-rank-rush, no-quarter";

        private AbilityDef(AbilityKind kind, Fix cooldown, int durationTicks, Fix radius, int ownMorale, int enemyMorale, int frontHit, Fix pace)
        {
            Kind = kind;
            Id = IdOf(kind);
            CooldownTicks = DefChecks.Ticks(cooldown, MaxCooldownSeconds, "cooldown");
            DurationTicks = durationTicks;
            Radius = radius;
            OwnMorale = ownMorale;
            EnemyMorale = enemyMorale;
            FrontHit = frontHit;
            Pace = pace;
        }

        /// <summary>Battle cry: own-side men within <paramref name="radius"/> m of him gain <paramref name="ownMorale"/>
        /// (up to 1000), enemies within it lose <paramref name="enemyMorale"/>; at once.</summary>
        public static AbilityDef BattleCry(Fix cooldown, Fix radius, int ownMorale, int enemyMorale) =>
            new AbilityDef(AbilityKind.BattleCry, cooldown, 0, DefChecks.PositiveMetres(radius, MaxRadius, "radius"),
                DefChecks.Permille(ownMorale, "ownMorale"), DefChecks.Permille(enemyMorale, "enemyMorale"), 0, Fix.One);

        /// <summary>Close ranks: for <paramref name="duration"/> s blows from the front at his squad's men get
        /// <paramref name="frontHit"/> ‰ less often through, and the squad marches at <paramref name="pace"/> of its pace.</summary>
        public static AbilityDef CloseRanks(Fix cooldown, Fix duration, int frontHit, Fix pace) =>
            new AbilityDef(AbilityKind.CloseRanks, cooldown, DefChecks.Ticks(duration, MaxDurationSeconds, "duration"), Fix.Zero, 0, 0,
                DefChecks.Permille(frontHit, "frontHit"), DefChecks.Fraction(pace, "pace"));

        /// <summary>Stand to the death: for <paramref name="duration"/> s his squad's men lose no morale.</summary>
        public static AbilityDef StandToTheDeath(Fix cooldown, Fix duration) =>
            new AbilityDef(AbilityKind.StandToTheDeath, cooldown, DefChecks.Ticks(duration, MaxDurationSeconds, "duration"), Fix.Zero, 0, 0, 0, Fix.One);

        /// <summary>An ability defined but not built yet (promise loot, front-rank rush, no quarter): only its cooldown.</summary>
        public static AbilityDef NotBuilt(AbilityKind kind, Fix cooldown)
        {
            if (IsBuilt(kind))
            {
                throw new ArgumentException($"{IdOf(kind)} is built: give its numbers.");
            }

            return new AbilityDef(kind, cooldown, 0, Fix.Zero, 0, 0, 0, Fix.One);
        }

        /// <summary>Content id, e.g. "battle-cry".</summary>
        public string Id { get; }

        /// <summary>Which ability.</summary>
        public AbilityKind Kind { get; }

        /// <summary>Whether the sim acts on it yet (M3 step 11: battle cry, close ranks, stand to the death).</summary>
        public bool Built => IsBuilt(Kind);

        /// <summary>Whether it acts on his squad (so he must be in one to use it) rather than round him.</summary>
        public bool OnSquad => Kind != AbilityKind.BattleCry;

        /// <summary>Ticks before he can use it again.</summary>
        public int CooldownTicks { get; }

        /// <summary>Ticks a lasting effect lasts; 0 for one acting at once.</summary>
        public int DurationTicks { get; }

        /// <summary>Battle cry: how far it reaches (m, centre to centre).</summary>
        public Fix Radius { get; }

        /// <summary>Battle cry: what own-side men in reach gain.</summary>
        public int OwnMorale { get; }

        /// <summary>Battle cry: what enemies in reach lose.</summary>
        public int EnemyMorale { get; }

        /// <summary>Close ranks: ‰ off the chance of a blow from the front at his squad's men.</summary>
        public int FrontHit { get; }

        /// <summary>Close ranks: the share of its pace the squad marches at; 1 for the others.</summary>
        public Fix Pace { get; }

        /// <summary>The content id of <paramref name="kind"/>.</summary>
        public static string IdOf(AbilityKind kind) => kind switch
        {
            AbilityKind.BattleCry => "battle-cry",
            AbilityKind.CloseRanks => "close-ranks",
            AbilityKind.PromiseLoot => "promise-loot",
            AbilityKind.StandToTheDeath => "stand-to-the-death",
            AbilityKind.FrontRankRush => "front-rank-rush",
            AbilityKind.NoQuarter => "no-quarter",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>The kind with content id <paramref name="id"/>, or null.</summary>
        public static AbilityKind? KindOf(string id) => id switch
        {
            "battle-cry" => AbilityKind.BattleCry,
            "close-ranks" => AbilityKind.CloseRanks,
            "promise-loot" => AbilityKind.PromiseLoot,
            "stand-to-the-death" => AbilityKind.StandToTheDeath,
            "front-rank-rush" => AbilityKind.FrontRankRush,
            "no-quarter" => AbilityKind.NoQuarter,
            _ => null,
        };

        private static bool IsBuilt(AbilityKind kind) =>
            kind == AbilityKind.BattleCry || kind == AbilityKind.CloseRanks || kind == AbilityKind.StandToTheDeath;

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add((int)Kind);
            hash.Add(CooldownTicks);
            hash.Add(DurationTicks);
            hash.Add(Radius.Raw);
            hash.Add(OwnMorale);
            hash.Add(EnemyMorale);
            hash.Add(FrontHit);
            hash.Add(Pace.Raw);
        }
    }
}
