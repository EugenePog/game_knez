using System;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// One group of a squad's men in a scenario (battle-sim.md §7, M3 step 9): how many, how experienced and in which
    /// kit. A squad lists its groups in order and its slots are filled in that order, front rank first.
    /// </summary>
    public sealed class SquadMenDef
    {
        /// <summary>Creates a group; throws <see cref="ArgumentException"/> on invalid values.</summary>
        public SquadMenDef(int count, ExperienceDef experience, KitDef kit)
        {
            Count = DefChecks.Range(count, 1, SquadSpawnDef.MaxCount, nameof(count));
            Experience = experience ?? throw new ArgumentNullException(nameof(experience));
            Kit = kit ?? throw new ArgumentNullException(nameof(kit));
        }

        /// <summary>Men in the group.</summary>
        public int Count { get; }

        /// <summary>How experienced they are (from the combat rules).</summary>
        public ExperienceDef Experience { get; }

        /// <summary>What they carry (from the combat rules).</summary>
        public KitDef Kit { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Count);
            hash.Add(Experience.Id);
            Kit.AddTo(ref hash);
        }
    }
}
