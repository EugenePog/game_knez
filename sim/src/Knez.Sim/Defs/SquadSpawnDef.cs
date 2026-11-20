using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable scenario entry: one squad (отряд) as it stands when the battle starts — side, fighter type, size,
    /// formation, anchor position (centre of the front rank) and facing.
    /// </summary>
    public sealed class SquadSpawnDef
    {
        /// <summary>Creates a spawn entry; throws <see cref="ArgumentException"/> on invalid values.</summary>
        public SquadSpawnDef(string name, int sideId, FighterDef fighter, int count, FormationDef formation,
            FixVec2 position, Angle facing)
        {
            Name = DefChecks.Id(name, nameof(name));
            SideId = DefChecks.Range(sideId, 0, MaxSideId, "side");
            Fighter = fighter ?? throw new ArgumentNullException(nameof(fighter));
            Count = DefChecks.Range(count, 1, MaxCount, nameof(count));
            Formation = formation ?? throw new ArgumentNullException(nameof(formation));
            Position = position;
            Facing = facing;
        }

        /// <summary>Sanity bound for <see cref="Count"/>, not a balance number (phase 1 squads have 5–8).</summary>
        public const int MaxCount = 64;

        /// <summary>Highest side id (phase 1 uses 0 and 1).</summary>
        public const int MaxSideId = 7;

        /// <summary>Display name, unique within the scenario.</summary>
        public string Name { get; }

        /// <summary>Side (0 = player, 1 = enemy in phase 1).</summary>
        public int SideId { get; }

        /// <summary>Type of every fighter in the squad.</summary>
        public FighterDef Fighter { get; }

        /// <summary>Number of fighters.</summary>
        public int Count { get; }

        /// <summary>Formation at the start.</summary>
        public FormationDef Formation { get; }

        /// <summary>Anchor: centre of the front rank, metres.</summary>
        public FixVec2 Position { get; }

        /// <summary>Direction the squad faces.</summary>
        public Angle Facing { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Name);
            hash.Add(SideId);
            Fighter.AddTo(ref hash);
            hash.Add(Count);
            Formation.AddTo(ref hash);
            hash.Add(Position.X.Raw);
            hash.Add(Position.Y.Raw);
            hash.Add((int)Facing.Raw);
        }
    }
}
