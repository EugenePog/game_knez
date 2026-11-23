using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Formations;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable battle setup: map and starting squads (battle-sim.md §7). <see cref="BattleSetup.Create"/> turns it
    /// into a <see cref="BattleState"/>, which keeps a reference to it and hashes its <see cref="Fingerprint"/>.
    /// </summary>
    public sealed class ScenarioDef
    {
        /// <summary>
        /// Creates a scenario and checks it as a whole: unique squad names, one def object per def id (state hashes refer
        /// to defs by id), every fighter's starting slot on the map. Throws <see cref="ArgumentException"/>; messages
        /// name the squad as <c>squads[i]</c>.
        /// </summary>
        public ScenarioDef(string id, string description, MapDef map, IReadOnlyList<SquadSpawnDef> squads,
            IReadOnlyList<ScriptedOrderDef>? script = null, MovementDef? movement = null,
            IReadOnlyList<FormationDef>? formations = null, CombatDef? combat = null)
        {
            Id = DefChecks.Id(id, nameof(id));
            Movement = movement ?? MovementDef.Standard;
            Combat = combat ?? CombatDef.Standard;
            Description = description ?? string.Empty;
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (squads == null)
            {
                throw new ArgumentNullException(nameof(squads));
            }

            var copy = new SquadSpawnDef[squads.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                SquadSpawnDef squad = squads[i] ?? throw new ArgumentNullException(nameof(squads), $"squads[{i}] is null.");
                for (int j = 0; j < i; j++)
                {
                    SquadSpawnDef other = copy[j];
                    if (string.Equals(other.Name, squad.Name, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"squads[{i}]: name '{squad.Name}' is already used by squads[{j}].");
                    }

                    if (!ReferenceEquals(other.Fighter, squad.Fighter) && string.Equals(other.Fighter.Id, squad.Fighter.Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"squads[{i}]: a different fighter def with id '{squad.Fighter.Id}' is used by squads[{j}].");
                    }

                    if (!ReferenceEquals(other.Formation, squad.Formation) && string.Equals(other.Formation.Id, squad.Formation.Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"squads[{i}]: a different formation def with id '{squad.Formation.Id}' is used by squads[{j}].");
                    }
                }

                string? stranger = Combat.Stranger(squad.Fighter.Kit);
                if (stranger != null)
                {
                    throw new ArgumentException(
                        $"squads[{i}] '{squad.Name}': fighter '{squad.Fighter.Id}': {stranger} is not the combat rules' def of that id.");
                }

                CheckOnMap(map, squad, i);
                CheckPersonalSpace(Movement, squad, i);
                copy[i] = squad;
            }

            Squads = new ReadOnlyCollection<SquadSpawnDef>(copy);
            Formations = new ReadOnlyCollection<FormationDef>(Catalogue(formations, copy));
            Script = new ReadOnlyCollection<ScriptedOrderDef>(CheckScript(script ?? Array.Empty<ScriptedOrderDef>(), copy, map, Formations));
            Fingerprint = ComputeFingerprint();
        }

        /// <summary>No squads on an open 100 × 100 m map: the empty battle for tests and tools.</summary>
        public static readonly ScenarioDef Empty =
            new ScenarioDef("empty", "No squads.", new MapDef(Fix.FromInt(100), Fix.FromInt(100)), Array.Empty<SquadSpawnDef>());

        /// <summary>Content id, e.g. "formations".</summary>
        public string Id { get; }

        /// <summary>Free text for people; not part of the fingerprint.</summary>
        public string Description { get; }

        /// <summary>The battle map.</summary>
        public MapDef Map { get; }

        /// <summary>
        /// Every formation the battle may use (content's whole catalogue; when built in code, the ones the squads spawn
        /// in), sorted by id and fingerprinted. Formation changes name formations from here.
        /// </summary>
        public IReadOnlyList<FormationDef> Formations { get; }

        /// <summary>The formation with <paramref name="id"/> in <see cref="Formations"/>, or null.</summary>
        public FormationDef? FindFormation(string id)
        {
            for (int i = 0; i < Formations.Count; i++)
            {
                if (string.Equals(Formations[i].Id, id, StringComparison.Ordinal))
                {
                    return Formations[i];
                }
            }

            return null;
        }

        /// <summary>Movement rules (content's <c>movement.json</c>; <see cref="MovementDef.Standard"/> when built in code).</summary>
        public MovementDef Movement { get; }

        /// <summary>Combat rules (content's <c>combat.json</c>; <see cref="CombatDef.Standard"/> when built in code). Every
        /// squad's fighter kit is drawn from them.</summary>
        public CombatDef Combat { get; }

        /// <summary>Starting squads in spawn order (ids are allocated in this order).</summary>
        public IReadOnlyList<SquadSpawnDef> Squads { get; }

        /// <summary>Orders the scenario gives by itself, sorted by tick (file order within a tick).</summary>
        public IReadOnlyList<ScriptedOrderDef> Script { get; }

        /// <summary>
        /// FNV-1a hash of everything that affects the battle (map, movement and combat rules, the formation catalogue, squads and the
        /// defs they use, script), in a canonical order.
        /// Hashed in the state's Core section, so two runs of different content never share a hash.
        /// </summary>
        public ulong Fingerprint { get; }

        /// <summary>
        /// Men standing in their slots must not crowd each other: personal space has to fit in the gap between two bodies
        /// side by side and rank behind rank, or a formation at rest keeps pushing itself apart.
        /// </summary>
        private static void CheckPersonalSpace(MovementDef movement, SquadSpawnDef squad, int index)
        {
            Fix bodies = squad.Fighter.Radius * 2;
            Fix gap = FixMath.Min(squad.Formation.Spacing, squad.Formation.RankSpacing) - bodies;
            if (movement.PersonalSpace >= gap)
            {
                throw new ArgumentException(
                    $"squads[{index}] '{squad.Name}': personalSpace {movement.PersonalSpace} m must be less than the {gap} m between " +
                    $"'{squad.Fighter.Id}' bodies in formation '{squad.Formation.Id}'.");
            }
        }

        private static void CheckOnMap(MapDef map, SquadSpawnDef squad, int index)
        {
            for (int slot = 0; slot < squad.Count; slot++)
            {
                FixVec2 position = FormationLayout.SlotPosition(squad.Formation, squad.Count, slot, squad.Position, squad.Facing);
                if (!map.Contains(position))
                {
                    throw new ArgumentException(
                        $"squads[{index}] '{squad.Name}': slot {slot} at {position} is off the {map} map; move the squad inward.");
                }

                if (Knez.Sim.Movement.Terrain.WallDepth(map, position, squad.Fighter.Radius) > Fix.Zero)
                {
                    throw new ArgumentException(
                        $"squads[{index}] '{squad.Name}': slot {slot} at {position} is in or against a wall; move the squad clear of it.");
                }
            }
        }

        /// <summary>
        /// The catalogue: <paramref name="given"/> (or the spawns' formations), sorted by id, one object per id, and holding
        /// the very def object each spawn uses (state hashes name formations by id).
        /// </summary>
        private static FormationDef[] Catalogue(IReadOnlyList<FormationDef>? given, SquadSpawnDef[] squads)
        {
            var list = new List<FormationDef>();
            if (given != null)
            {
                for (int i = 0; i < given.Count; i++)
                {
                    list.Add(given[i] ?? throw new ArgumentNullException(nameof(given), $"formations[{i}] is null."));
                }
            }

            for (int i = 0; i < squads.Length; i++)
            {
                if (given == null && !list.Contains(squads[i].Formation))
                {
                    list.Add(squads[i].Formation);
                }
            }

            for (int i = 1; i < list.Count; i++)
            {
                // Insertion sort by id (ordinal): a handful of defs, and no lambda for the def-type tests to trip over.
                FormationDef item = list[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(list[j].Id, item.Id) > 0)
                {
                    list[j + 1] = list[j];
                    j--;
                }

                list[j + 1] = item;
            }
            for (int i = 1; i < list.Count; i++)
            {
                if (string.Equals(list[i - 1].Id, list[i].Id, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"formation id '{list[i].Id}' appears twice in the catalogue.");
                }
            }

            for (int i = 0; i < squads.Length; i++)
            {
                if (!list.Contains(squads[i].Formation))
                {
                    throw new ArgumentException($"squads[{i}] '{squads[i].Name}': formation '{squads[i].Formation.Id}' is not the catalogue's def of that id.");
                }
            }

            return list.ToArray();
        }

        private static ScriptedOrderDef[] CheckScript(IReadOnlyList<ScriptedOrderDef> script, SquadSpawnDef[] squads, MapDef map,
            IReadOnlyList<FormationDef> formations)
        {
            var copy = new ScriptedOrderDef[script.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                ScriptedOrderDef entry = script[i] ?? throw new ArgumentNullException(nameof(script), $"script[{i}] is null.");
                if (i > 0 && entry.Tick < copy[i - 1].Tick)
                {
                    throw new ArgumentException($"script[{i}]: tick {entry.Tick} comes before the previous entry's tick {copy[i - 1].Tick}; keep the script in tick order.");
                }

                bool known = false;
                for (int s = 0; s < squads.Length && !known; s++)
                {
                    known = string.Equals(squads[s].Name, entry.SquadName, StringComparison.Ordinal);
                }

                if (!known)
                {
                    throw new ArgumentException($"script[{i}]: no squad is named '{entry.SquadName}'.");
                }

                if (entry.FormationId != null)
                {
                    bool exists = false;
                    for (int f = 0; f < formations.Count && !exists; f++)
                    {
                        exists = string.Equals(formations[f].Id, entry.FormationId, StringComparison.Ordinal);
                    }

                    if (!exists)
                    {
                        throw new ArgumentException($"script[{i}]: no formation '{entry.FormationId}' in the catalogue.");
                    }
                }
                else if (!map.Contains(entry.Target))
                {
                    throw new ArgumentException($"script[{i}]: target {entry.Target} is off the {map} map.");
                }

                copy[i] = entry;
            }

            return copy;
        }

        private ulong ComputeFingerprint()
        {
            var hash = Fnv1a64.Start();
            hash.Add(Id);
            Map.AddTo(ref hash);
            Movement.AddTo(ref hash);
            Combat.AddTo(ref hash);
            hash.Add(Formations.Count);
            for (int i = 0; i < Formations.Count; i++)
            {
                Formations[i].AddTo(ref hash);
            }

            hash.Add(Squads.Count);
            for (int i = 0; i < Squads.Count; i++)
            {
                Squads[i].AddTo(ref hash);
            }

            hash.Add(Script.Count);
            for (int i = 0; i < Script.Count; i++)
            {
                Script[i].AddTo(ref hash);
            }

            return hash.Value;
        }
    }
}
