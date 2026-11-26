using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable scenario entry: one squad (отряд) as it stands when the battle starts — side, the body of its men, its
    /// men in groups (count, experience, kit; M3 step 9), formation, anchor position (centre of the front rank) and
    /// facing. Slots are filled in group order, front rank first.
    /// </summary>
    public sealed class SquadSpawnDef
    {
        private readonly ReadOnlyCollection<SquadMenDef> _men;

        /// <summary>Creates a spawn entry; throws <see cref="ArgumentException"/> on invalid values.</summary>
        public SquadSpawnDef(string name, int sideId, BodyDef body, IReadOnlyList<SquadMenDef> men, FormationDef formation,
            FixVec2 position, Angle facing, Orders.SquadStance stance = Orders.SquadStance.Hold)
        {
            Stance = stance;
            Name = DefChecks.Id(name, nameof(name));
            SideId = DefChecks.Range(sideId, 0, MaxSideId, "side");
            Body = body ?? throw new ArgumentNullException(nameof(body));
            if (men == null || men.Count == 0)
            {
                throw new ArgumentException("men: a squad needs at least one group of men.");
            }

            var copy = new SquadMenDef[men.Count];
            int count = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = men[i] ?? throw new ArgumentException($"men[{i}] is missing.");
                count += copy[i].Count;
            }

            _men = new ReadOnlyCollection<SquadMenDef>(copy);
            Count = DefChecks.Range(count, 1, MaxCount, "the squad's size (men counts summed)");
            Formation = formation ?? throw new ArgumentNullException(nameof(formation));
            Position = position;
            Facing = facing;
        }

        /// <summary>Sanity bound for <see cref="Count"/>, not a balance number (phase 1 squads have 5–8).</summary>
        public const int MaxCount = 64;

        /// <summary>The stance it starts in (M4 step 1): Hold unless the scenario says otherwise.</summary>
        public Orders.SquadStance Stance { get; }

        /// <summary>Highest side id (phase 1 uses 0 and 1).</summary>
        public const int MaxSideId = 7;

        /// <summary>Display name, unique within the scenario.</summary>
        public string Name { get; }

        /// <summary>Side (0 = player, 1 = enemy in phase 1).</summary>
        public int SideId { get; }

        /// <summary>The body of every man of the squad.</summary>
        public BodyDef Body { get; }

        /// <summary>Its men in groups, in slot order.</summary>
        public IReadOnlyList<SquadMenDef> Men => _men;

        /// <summary>Number of men: the groups' counts summed.</summary>
        public int Count { get; }

        /// <summary>Formation at the start.</summary>
        public FormationDef Formation { get; }

        /// <summary>Anchor: centre of the front rank, metres.</summary>
        public FixVec2 Position { get; }

        /// <summary>Direction the squad faces.</summary>
        public Angle Facing { get; }

        /// <summary>The group the man in slot <paramref name="slot"/> (0-based, in fill order) belongs to.</summary>
        public SquadMenDef GroupAt(int slot)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} is before the squad's first man.");
            }

            int left = slot;
            for (int i = 0; i < _men.Count; i++)
            {
                if (left < _men[i].Count)
                {
                    return _men[i];
                }

                left -= _men[i].Count;
            }

            throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} is past the squad's {Count} men.");
        }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Name);
            hash.Add(SideId);
            Body.AddTo(ref hash);
            hash.Add(_men.Count);
            for (int i = 0; i < _men.Count; i++)
            {
                _men[i].AddTo(ref hash);
            }

            hash.Add(Count);
            Formation.AddTo(ref hash);
            hash.Add(Position.X.Raw);
            hash.Add(Position.Y.Raw);
            hash.Add((int)Stance);
            hash.Add((int)Facing.Raw);
        }
    }
}
