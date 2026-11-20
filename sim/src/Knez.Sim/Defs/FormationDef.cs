using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable formation (строй) from content: a grid of slots behind the squad anchor (battle-sim.md §10).
    /// The shape is fixed either by the number of <see cref="Ranks"/> (the width follows from the squad size) or by
    /// the <see cref="Width"/> (the ranks follow). <see cref="Formations.FormationLayout"/> turns it into slot offsets.
    /// </summary>
    public sealed class FormationDef
    {
        /// <summary>Creates a def; exactly one of <paramref name="ranks"/> and <paramref name="width"/> is positive (at most
        /// <see cref="SquadSpawnDef.MaxCount"/>), the other 0.</summary>
        public FormationDef(string id, Fix spacing, Fix rankSpacing, int ranks, int width, Fix turnRate, int holdWeight)
        {
            TurnRate = DefChecks.Positive(turnRate, FighterDef.MaxTurnRate, "°/s", nameof(turnRate));
            HoldWeight = DefChecks.Range(holdWeight, 1, MaxHoldWeight, nameof(holdWeight));
            Id = DefChecks.Id(id, nameof(id));
            Spacing = DefChecks.PositiveMetres(spacing, MaxSpacingMetres, nameof(spacing));
            RankSpacing = DefChecks.PositiveMetres(rankSpacing, MaxSpacingMetres, nameof(rankSpacing));
            Ranks = DefChecks.Range(ranks, 0, SquadSpawnDef.MaxCount, nameof(ranks));
            Width = DefChecks.Range(width, 0, SquadSpawnDef.MaxCount, nameof(width));
            if ((ranks > 0) == (width > 0))
            {
                throw new ArgumentException($"set exactly one of ranks and width (got ranks {ranks}, width {width}).");
            }
        }

        /// <summary>Sanity bound for spacings, not a balance number.</summary>
        public const int MaxSpacingMetres = 10;

        /// <summary>Content id, e.g. "line".</summary>
        public string Id { get; }

        /// <summary>Distance between neighbours in a rank, metres.</summary>
        public Fix Spacing { get; }

        /// <summary>Distance between ranks, metres.</summary>
        public Fix RankSpacing { get; }

        /// <summary>Fixed number of ranks, or 0 when <see cref="Width"/> fixes the shape.</summary>
        public int Ranks { get; }

        /// <summary>Fixed number of files (fighters per rank), or 0 when <see cref="Ranks"/> fixes the shape.</summary>
        public int Width { get; }

        /// <summary>How fast a squad in this formation turns its front, degrees per second.</summary>
        public Fix TurnRate { get; }

        /// <summary><see cref="TurnRate"/> as <see cref="Angle"/> units per tick (at least 1).</summary>
        public int TurnPerTick => DefChecks.TurnUnitsPerTick(TurnRate);

        /// <summary>
        /// How much harder members of a squad holding its ground (no order) are to push than men on the move, who weigh 1
        /// (battle-sim.md §10). With 3, a holding man takes a quarter of a push against a moving one.
        /// </summary>
        public int HoldWeight { get; }

        /// <summary>Sanity bound for <see cref="HoldWeight"/>, not a balance number.</summary>
        public const int MaxHoldWeight = 100;

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Id);
            hash.Add(Spacing.Raw);
            hash.Add(RankSpacing.Raw);
            hash.Add(Ranks);
            hash.Add(Width);
            hash.Add(TurnRate.Raw);
            hash.Add(HoldWeight);
        }
    }
}
