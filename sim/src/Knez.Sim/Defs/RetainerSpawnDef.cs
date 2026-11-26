using System;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable scenario entry: one Retainer (дружинник) as a named person (battle-sim.md §13, M3 step 10) — side,
    /// trait, body, experience and kit, and where he starts: in the front rank of a squad of his side, or standing alone
    /// at a position and facing. Retainers get their ids after every squad's men, in list order.
    /// </summary>
    public sealed class RetainerSpawnDef
    {
        private RetainerSpawnDef(string name, int sideId, TraitDef trait, BodyDef body, ExperienceDef experience, KitDef kit,
            string? squadName, FixVec2 position, Angle facing)
        {
            Name = DefChecks.Id(name, nameof(name));
            SideId = DefChecks.Range(sideId, 0, SquadSpawnDef.MaxSideId, "side");
            Trait = trait ?? throw new ArgumentNullException(nameof(trait));
            Body = body ?? throw new ArgumentNullException(nameof(body));
            Experience = experience ?? throw new ArgumentNullException(nameof(experience));
            Kit = kit ?? throw new ArgumentNullException(nameof(kit));
            SquadName = squadName == null ? null : DefChecks.Id(squadName, "squad");
            Position = position;
            Facing = facing;
        }

        /// <summary>A Retainer who starts in the front rank of the squad named <paramref name="squadName"/>.</summary>
        public static RetainerSpawnDef InSquad(string name, int sideId, TraitDef trait, BodyDef body, ExperienceDef experience,
            KitDef kit, string squadName) =>
            new RetainerSpawnDef(name, sideId, trait, body, experience, kit, squadName ?? throw new ArgumentNullException(nameof(squadName)),
                FixVec2.Zero, Angle.Zero);

        /// <summary>A Retainer who starts standing alone at <paramref name="position"/>, facing <paramref name="facing"/>.</summary>
        public static RetainerSpawnDef Alone(string name, int sideId, TraitDef trait, BodyDef body, ExperienceDef experience,
            KitDef kit, FixVec2 position, Angle facing) =>
            new RetainerSpawnDef(name, sideId, trait, body, experience, kit, null, position, facing);

        /// <summary>His name, unique among the scenario's Retainers.</summary>
        public string Name { get; }

        /// <summary>His side.</summary>
        public int SideId { get; }

        /// <summary>His trait (D-025).</summary>
        public TraitDef Trait { get; }

        /// <summary>His body.</summary>
        public BodyDef Body { get; }

        /// <summary>His experience level (from the combat rules); it sets his leadership.</summary>
        public ExperienceDef Experience { get; }

        /// <summary>What he carries (from the combat rules).</summary>
        public KitDef Kit { get; }

        /// <summary>The squad he starts in, or null when he starts alone.</summary>
        public string? SquadName { get; }

        /// <summary>Where he stands when he starts alone (unused in a squad).</summary>
        public FixVec2 Position { get; }

        /// <summary>His facing when he starts alone (unused in a squad).</summary>
        public Angle Facing { get; }

        internal void AddTo(ref Fnv1a64 hash)
        {
            hash.Add(Name);
            hash.Add(SideId);
            Trait.AddTo(ref hash);
            Body.AddTo(ref hash);
            hash.Add(Experience.Id);
            Kit.AddTo(ref hash);
            hash.Add(SquadName ?? string.Empty);
            hash.Add(Position.X.Raw);
            hash.Add(Position.Y.Raw);
            hash.Add((int)Facing.Raw);
        }
    }
}
