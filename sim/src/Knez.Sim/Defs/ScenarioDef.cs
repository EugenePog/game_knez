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
            IReadOnlyList<FormationDef>? formations = null, CombatDef? combat = null, MoraleDef? morale = null,
            IReadOnlyList<RetainerSpawnDef>? retainers = null, AiDef? ai = null, IReadOnlyList<int>? aiSides = null)
        {
            Morale = morale ?? MoraleDef.Standard;
            Ai = ai ?? AiDef.Standard;
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

                    if (!ReferenceEquals(other.Body, squad.Body) && string.Equals(other.Body.Id, squad.Body.Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"squads[{i}]: a different body def with id '{squad.Body.Id}' is used by squads[{j}].");
                    }

                    if (!ReferenceEquals(other.Formation, squad.Formation) && string.Equals(other.Formation.Id, squad.Formation.Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"squads[{i}]: a different formation def with id '{squad.Formation.Id}' is used by squads[{j}].");
                    }
                }

                for (int g = 0; g < squad.Men.Count; g++)
                {
                    SquadMenDef men = squad.Men[g];
                    if (men.Experience.Morale < Morale.Routing)
                    {
                        throw new ArgumentException(
                            $"squads[{i}] '{squad.Name}' men[{g}]: experience '{men.Experience.Id}' starts at morale {men.Experience.Morale}, below the routing threshold {Morale.Routing}: they would run at once.");
                    }

                    if (men.Experience.RetainerOnly)
                    {
                        throw new ArgumentException(
                            $"squads[{i}] '{squad.Name}' men[{g}]: experience '{men.Experience.Id}' is for Retainers only (D-019); list him in retainers.");
                    }

                    if (!Combat.Contains(men.Experience))
                    {
                        throw new ArgumentException(
                            $"squads[{i}] '{squad.Name}' men[{g}]: experience level '{men.Experience.Id}' is not the combat rules' def of that id.");
                    }

                    if (!Combat.Contains(men.Kit))
                    {
                        throw new ArgumentException(
                            $"squads[{i}] '{squad.Name}' men[{g}]: kit '{men.Kit.Id}' is not the combat rules' def of that id.");
                    }
                }

                CheckOnMap(map, squad, i);
                CheckPersonalSpace(Movement, squad, i);
                copy[i] = squad;
            }

            Squads = new ReadOnlyCollection<SquadSpawnDef>(copy);
            Retainers = new ReadOnlyCollection<RetainerSpawnDef>(CheckRetainers(retainers ?? Array.Empty<RetainerSpawnDef>(), copy, map));
            Formations = new ReadOnlyCollection<FormationDef>(Catalogue(formations, copy));
            AiSides = new ReadOnlyCollection<int>(CheckAiSides(aiSides ?? Array.Empty<int>(), copy, Retainers, out _aiMask));
            Script = new ReadOnlyCollection<ScriptedOrderDef>(CheckScript(script ?? Array.Empty<ScriptedOrderDef>(), copy, Retainers, map, Formations));
            CheckScriptSides(Script, copy, Retainers, _aiMask);
            Fingerprint = ComputeFingerprint();
        }

        private readonly int _aiMask;

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

        /// <summary>Morale rules (content's <c>morale.json</c>; <see cref="MoraleDef.Standard"/> when built in code).</summary>
        public MoraleDef Morale { get; }

        /// <summary>Combat rules (content's <c>combat.json</c>; <see cref="CombatDef.Standard"/> when built in code). Every
        /// squad's fighter kit is drawn from them.</summary>
        public CombatDef Combat { get; }

        /// <summary>Enemy-AI rules (content's <c>ai.json</c>; <see cref="AiDef.Standard"/> when built in code).</summary>
        public AiDef Ai { get; }

        /// <summary>The sides the AI plays (M4 step 2), ascending; empty when the scenario's script and the player give every
        /// order. A side the AI plays takes no commands and no script orders.</summary>
        public IReadOnlyList<int> AiSides { get; }

        /// <summary>Whether the AI plays <paramref name="side"/>.</summary>
        public bool PlaysAi(int side) => side >= 0 && side <= SquadSpawnDef.MaxSideId && ((_aiMask >> side) & 1) != 0;

        /// <summary>
        /// This scenario with the AI playing <paramref name="sides"/> instead (the CLI's <c>--ai</c>): script orders for
        /// squads and Retainers of those sides are dropped, since the AI gives their orders.
        /// </summary>
        public ScenarioDef WithAi(IReadOnlyList<int> sides)
        {
            if (sides == null)
            {
                throw new ArgumentNullException(nameof(sides));
            }

            int mask = 0;
            for (int i = 0; i < sides.Count; i++)
            {
                mask |= sides[i] >= 0 && sides[i] <= SquadSpawnDef.MaxSideId ? 1 << sides[i] : 0;
            }

            var squads = new SquadSpawnDef[Squads.Count];
            for (int i = 0; i < squads.Length; i++)
            {
                squads[i] = Squads[i];
            }

            var kept = new List<ScriptedOrderDef>(Script.Count);
            for (int i = 0; i < Script.Count; i++)
            {
                int side = ScriptSide(Script[i], squads, Retainers);
                if (side < 0 || ((mask >> side) & 1) == 0)
                {
                    kept.Add(Script[i]);
                }
            }

            return new ScenarioDef(Id, Description, Map, Squads, kept, Movement, Formations, Combat, Morale, Retainers, Ai, sides);
        }

        /// <summary>Starting squads in spawn order (ids are allocated in this order).</summary>
        public IReadOnlyList<SquadSpawnDef> Squads { get; }

        /// <summary>Orders the scenario gives by itself, sorted by tick (file order within a tick).</summary>
        public IReadOnlyList<ScriptedOrderDef> Script { get; }

        /// <summary>Retainers as named people (battle-sim.md §13, M3 step 10), in id order: after every squad's men.</summary>
        public IReadOnlyList<RetainerSpawnDef> Retainers { get; }

        /// <summary>
        /// FNV-1a hash of everything that affects the battle (map, movement, combat, morale and AI rules, the sides the AI
        /// plays, the formation catalogue, squads and the defs they use, script, Retainers), in a canonical order.
        /// Hashed in the state's Core section, so two runs of different content never share a hash.
        /// </summary>
        public ulong Fingerprint { get; }

        /// <summary>
        /// Men standing in their slots must not crowd each other: personal space has to fit in the gap between two bodies
        /// side by side and rank behind rank, or a formation at rest keeps pushing itself apart.
        /// </summary>
        private static void CheckPersonalSpace(MovementDef movement, SquadSpawnDef squad, int index)
        {
            Fix bodies = squad.Body.Radius * 2;
            Fix gap = FixMath.Min(squad.Formation.Spacing, squad.Formation.RankSpacing) - bodies;
            if (movement.PersonalSpace >= gap)
            {
                throw new ArgumentException(
                    $"squads[{index}] '{squad.Name}': personalSpace {movement.PersonalSpace} m must be less than the {gap} m between " +
                    $"'{squad.Body.Id}' bodies in formation '{squad.Formation.Id}'.");
            }
        }

        private static void CheckOnMap(MapDef map, SquadSpawnDef squad, int index) => CheckOnMap(map, squad, index, squad.Count, string.Empty);

        /// <summary>Every slot of <paramref name="squad"/> laid out for <paramref name="count"/> men is on the map and clear
        /// of walls (<paramref name="with"/>: what the count includes, for the message).</summary>
        private static void CheckOnMap(MapDef map, SquadSpawnDef squad, int index, int count, string with)
        {
            for (int slot = 0; slot < count; slot++)
            {
                FixVec2 position = FormationLayout.SlotPosition(squad.Formation, count, slot, squad.Position, squad.Facing);
                if (!map.Contains(position))
                {
                    throw new ArgumentException(
                        $"squads[{index}] '{squad.Name}'{with}: slot {slot} at {position} is off the {map} map; move the squad inward.");
                }

                if (Knez.Sim.Movement.Terrain.WallDepth(map, position, squad.Body.Radius) > Fix.Zero)
                {
                    throw new ArgumentException(
                        $"squads[{index}] '{squad.Name}'{with}: slot {slot} at {position} is in or against a wall; move the squad clear of it.");
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

        /// <summary>
        /// Retainers: unique names; experience and kit from these combat rules, starting morale above routing; a squad to
        /// start in that exists, is of his side and has room (64 with every Retainer in it), or a place on the map clear of
        /// walls. Messages name the entry as <c>retainers[i]</c>.
        /// </summary>
        private RetainerSpawnDef[] CheckRetainers(IReadOnlyList<RetainerSpawnDef> retainers, SquadSpawnDef[] squads, MapDef map)
        {
            var copy = new RetainerSpawnDef[retainers.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                RetainerSpawnDef r = retainers[i] ?? throw new ArgumentNullException(nameof(retainers), $"retainers[{i}] is null.");
                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(copy[j].Name, r.Name, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"retainers[{i}]: name '{r.Name}' is already used by retainers[{j}].");
                    }
                }

                if (!Combat.Contains(r.Experience))
                {
                    throw new ArgumentException($"retainers[{i}] '{r.Name}': experience level '{r.Experience.Id}' is not the combat rules' def of that id.");
                }

                if (!Combat.Contains(r.Kit))
                {
                    throw new ArgumentException($"retainers[{i}] '{r.Name}': kit '{r.Kit.Id}' is not the combat rules' def of that id.");
                }

                if (r.Experience.Morale < Morale.Routing)
                {
                    throw new ArgumentException($"retainers[{i}] '{r.Name}': experience '{r.Experience.Id}' starts at morale {r.Experience.Morale}, below the routing threshold {Morale.Routing}: he would run at once.");
                }

                if (r.SquadName != null)
                {
                    SquadSpawnDef? squad = null;
                    for (int s = 0; s < squads.Length && squad == null; s++)
                    {
                        squad = string.Equals(squads[s].Name, r.SquadName, StringComparison.Ordinal) ? squads[s] : null;
                    }

                    if (squad == null)
                    {
                        throw new ArgumentException($"retainers[{i}] '{r.Name}': no squad is named '{r.SquadName}'.");
                    }

                    if (squad.SideId != r.SideId)
                    {
                        throw new ArgumentException($"retainers[{i}] '{r.Name}': squad '{r.SquadName}' is side {squad.SideId}, he is side {r.SideId}.");
                    }

                    int joining = 0;
                    for (int j = 0; j <= i && j < retainers.Count; j++)
                    {
                        joining += string.Equals(retainers[j]?.SquadName, r.SquadName, StringComparison.Ordinal) ? 1 : 0;
                    }

                    if (squad.Count + joining > SquadSpawnDef.MaxCount)
                    {
                        throw new ArgumentException($"retainers[{i}] '{r.Name}': squad '{r.SquadName}' would have more than {SquadSpawnDef.MaxCount} men.");
                    }
                }
                else
                {
                    if (!map.Contains(r.Position))
                    {
                        throw new ArgumentException($"retainers[{i}] '{r.Name}': position {r.Position} is off the {map} map.");
                    }

                    if (Knez.Sim.Movement.Terrain.WallDepth(map, r.Position, r.Body.Radius) > Fix.Zero)
                    {
                        throw new ArgumentException($"retainers[{i}] '{r.Name}': position {r.Position} is in or against a wall.");
                    }
                }

                copy[i] = r;
            }

            // A squad that Retainers join at spawn is laid out for all its men (§13): check that layout as its own.
            for (int s = 0; s < squads.Length; s++)
            {
                int joining = 0;
                for (int i = 0; i < copy.Length; i++)
                {
                    joining += string.Equals(copy[i].SquadName, squads[s].Name, StringComparison.Ordinal) ? 1 : 0;
                }

                if (joining > 0)
                {
                    CheckOnMap(map, squads[s], s, squads[s].Count + joining, $" with its {joining} Retainer(s)");
                }
            }

            return copy;
        }

        private static ScriptedOrderDef[] CheckScript(IReadOnlyList<ScriptedOrderDef> script, SquadSpawnDef[] squads,
            IReadOnlyList<RetainerSpawnDef> retainers, MapDef map, IReadOnlyList<FormationDef> formations)
        {
            var copy = new ScriptedOrderDef[script.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                ScriptedOrderDef entry = script[i] ?? throw new ArgumentNullException(nameof(script), $"script[{i}] is null.");
                if (i > 0 && entry.Tick < copy[i - 1].Tick)
                {
                    throw new ArgumentException($"script[{i}]: tick {entry.Tick} comes before the previous entry's tick {copy[i - 1].Tick}; keep the script in tick order.");
                }

                if (entry.SquadName != null)
                {
                    bool known = false;
                    for (int s = 0; s < squads.Length && !known; s++)
                    {
                        known = string.Equals(squads[s].Name, entry.SquadName, StringComparison.Ordinal);
                    }

                    if (!known)
                    {
                        throw new ArgumentException($"script[{i}]: no squad is named '{entry.SquadName}'.");
                    }
                }

                if (entry.EnemySquadName != null)
                {
                    bool known = false;
                    for (int s = 0; s < squads.Length && !known; s++)
                    {
                        known = string.Equals(squads[s].Name, entry.EnemySquadName, StringComparison.Ordinal);
                    }

                    if (!known)
                    {
                        throw new ArgumentException($"script[{i}]: no squad is named '{entry.EnemySquadName}'.");
                    }
                }

                if (entry.RetainerName != null)
                {
                    bool known = false;
                    for (int r = 0; r < retainers.Count && !known; r++)
                    {
                        known = string.Equals(retainers[r].Name, entry.RetainerName, StringComparison.Ordinal);
                    }

                    if (!known)
                    {
                        throw new ArgumentException($"script[{i}]: no Retainer is named '{entry.RetainerName}'.");
                    }
                }

                if (entry.Kind == ScriptedOrderKind.DetachRetainer || entry.Kind == ScriptedOrderKind.RetainerAttack
                    || entry.Kind == ScriptedOrderKind.AttachRetainer || entry.Kind == ScriptedOrderKind.UseAbility
                    || entry.Kind == ScriptedOrderKind.AttackSquad || entry.Kind == ScriptedOrderKind.SetStance)
                {
                    // Nothing on the map to check: who may join whom, and which enemy, is checked when the order runs.
                }
                else if (entry.FormationId != null)
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

        /// <summary>The sides the AI plays: each one has squads or Retainers, none twice; sorted. <paramref name="mask"/>
        /// has bit <c>side</c> set for each.</summary>
        private static int[] CheckAiSides(IReadOnlyList<int> sides, SquadSpawnDef[] squads, IReadOnlyList<RetainerSpawnDef> retainers,
            out int mask)
        {
            mask = 0;
            for (int i = 0; i < sides.Count; i++)
            {
                int side = sides[i];
                if (side < 0 || side > SquadSpawnDef.MaxSideId)
                {
                    throw new ArgumentException($"ai[{i}]: side {side} is outside 0..{SquadSpawnDef.MaxSideId}.");
                }

                if (((mask >> side) & 1) != 0)
                {
                    throw new ArgumentException($"ai[{i}]: side {side} is listed twice.");
                }

                bool present = false;
                for (int s = 0; s < squads.Length && !present; s++)
                {
                    present = squads[s].SideId == side;
                }

                for (int r = 0; r < retainers.Count && !present; r++)
                {
                    present = retainers[r].SideId == side;
                }

                if (!present)
                {
                    throw new ArgumentException($"ai[{i}]: side {side} has no squads or Retainers for the AI to play.");
                }

                mask |= 1 << side;
            }

            var sorted = new List<int>(sides.Count);
            for (int side = 0; side <= SquadSpawnDef.MaxSideId; side++)
            {
                if (((mask >> side) & 1) != 0)
                {
                    sorted.Add(side);
                }
            }

            return sorted.ToArray();
        }

        /// <summary>The AI gives its own sides' orders: the script may give none of them.</summary>
        private static void CheckScriptSides(IReadOnlyList<ScriptedOrderDef> script, SquadSpawnDef[] squads,
            IReadOnlyList<RetainerSpawnDef> retainers, int aiMask)
        {
            for (int i = 0; i < script.Count; i++)
            {
                int side = ScriptSide(script[i], squads, retainers);
                if (side >= 0 && ((aiMask >> side) & 1) != 0)
                {
                    string who = script[i].SquadName != null ? $"squad '{script[i].SquadName}'" : $"Retainer '{script[i].RetainerName}'";
                    throw new ArgumentException($"script[{i}]: {who} is side {side}, which the AI plays; the AI gives that side's orders.");
                }
            }
        }

        /// <summary>The side that gives a script order: its squad's, else its Retainer's; −1 if neither is named.</summary>
        private static int ScriptSide(ScriptedOrderDef entry, SquadSpawnDef[] squads, IReadOnlyList<RetainerSpawnDef> retainers)
        {
            if (entry.SquadName != null)
            {
                for (int s = 0; s < squads.Length; s++)
                {
                    if (string.Equals(squads[s].Name, entry.SquadName, StringComparison.Ordinal))
                    {
                        return squads[s].SideId;
                    }
                }
            }

            if (entry.RetainerName != null)
            {
                for (int r = 0; r < retainers.Count; r++)
                {
                    if (string.Equals(retainers[r].Name, entry.RetainerName, StringComparison.Ordinal))
                    {
                        return retainers[r].SideId;
                    }
                }
            }

            return -1;
        }

        private ulong ComputeFingerprint()
        {
            var hash = Fnv1a64.Start();
            hash.Add(Id);
            Map.AddTo(ref hash);
            Movement.AddTo(ref hash);
            Combat.AddTo(ref hash);
            Morale.AddTo(ref hash);
            Ai.AddTo(ref hash); // M4 step 2
            hash.Add(_aiMask);
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

            hash.Add(Retainers.Count);
            for (int i = 0; i < Retainers.Count; i++)
            {
                Retainers[i].AddTo(ref hash);
            }

            return hash.Value;
        }
    }
}
