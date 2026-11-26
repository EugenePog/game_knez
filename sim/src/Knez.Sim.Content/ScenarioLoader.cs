using System;
using System.Collections.Generic;
using Knez.Sim.Content.Json;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Content
{
    /// <summary>
    /// Reads a scenario file (<c>content/scenarios/*.json</c>) into a <see cref="ScenarioDef"/>, resolving def ids
    /// against a <see cref="DefLibrary"/>. Format (battle-sim.md §7):
    /// <code>
    /// { "id": "formations", "description": "…",
    ///   "map": { "width": 60, "height": 40, "walls": [ { "from": [0, 20], "to": [28, 21], "kind": "house" } ] },
    ///   "squads": [ { "name": "Line", "side": 0, "body": "man",
    ///                 "men": [ { "count": 8, "experience": "experienced", "kit": "sword-shield-padded" } ],
    ///                 "formation": "line", "position": [20, 12], "facing": 90 } ],
    ///   "script": [ { "tick": 20, "order": "move", "squad": "Line", "target": [20, 24], "facing": 90 } ] }
    /// </code>
    /// Metres for lengths, degrees for <c>facing</c> (0 = east, 90 = north). Numbers may have up to 9 decimals.
    /// <c>script</c> is optional and in tick order; a scripted <c>facing</c> is optional (default: the march direction).
    /// A squad's men come in groups (M3 step 9, ADR-0005), its slots filled in that order; experience levels and kits are
    /// the combat rules' (<see cref="DefLibrary.Rules"/>).
    /// </summary>
    public static class ScenarioLoader
    {
        private const string ScenarioMembers = "id, description, map, squads, retainers, script";
        private const string RetainerMembers = "name, side, trait, body, experience, kit, squad, position, facing";
        private const string ScriptMembers = "tick, order, squad, target, facing, formation";
        private const string MapMembers = "width, height, walls";
        private const string WallMembers = "from, to, kind";
        private const string SquadMembers = "name, side, body, men, formation, position, facing";
        private const string MenMembers = "count, experience, kit";

        /// <summary>Parses a scenario; throws <see cref="ContentException"/> on the first problem.</summary>
        public static ScenarioDef Parse(SourceText source, DefLibrary defs)
        {
            JsonValue rootValue = JsonReader.Parse(source);
            var root = JsonObjectReader.Of(rootValue, string.Empty, source.Name);
            string id = root.RequireString("id");
            string description = root.OptionalString("description") ?? string.Empty;

            var mapReader = root.RequireObject("map");
            Fix width = mapReader.RequireFix("width");
            Fix height = mapReader.RequireFix("height");
            IReadOnlyList<JsonValue> wallItems = mapReader.OptionalArray("walls");
            var walls = new List<WallDef>(wallItems.Count);
            for (int i = 0; i < wallItems.Count; i++)
            {
                var wall = JsonObjectReader.Of(wallItems[i], $"map.walls[{i}]", source.Name);
                FixVec2 from = wall.RequireVec2("from");
                FixVec2 to = wall.RequireVec2("to");
                WallKind kind = WallKindOf(wall);
                wall.RejectUnknownMembers(WallMembers);
                walls.Add(Build(wall, wallItems[i], () => new WallDef(from, to, kind)));
            }

            mapReader.RejectUnknownMembers(MapMembers);
            JsonValue mapValue = rootValue.Get("map")!;
            MapDef map = Build(mapReader, mapValue, () => new MapDef(width, height, walls));

            IReadOnlyList<JsonValue> squadItems = root.RequireArray("squads");
            var squads = new List<SquadSpawnDef>(squadItems.Count);
            for (int i = 0; i < squadItems.Count; i++)
            {
                squads.Add(ReadSquad(squadItems[i], $"squads[{i}]", source.Name, defs));
            }

            IReadOnlyList<JsonValue> retainerItems = root.OptionalArray("retainers");
            var retainers = new List<RetainerSpawnDef>(retainerItems.Count);
            for (int i = 0; i < retainerItems.Count; i++)
            {
                retainers.Add(ReadRetainer(retainerItems[i], $"retainers[{i}]", source.Name, defs));
            }

            IReadOnlyList<JsonValue> scriptItems = root.OptionalArray("script");
            var script = new List<ScriptedOrderDef>(scriptItems.Count);
            for (int i = 0; i < scriptItems.Count; i++)
            {
                script.Add(ReadScriptedOrder(scriptItems[i], $"script[{i}]", source.Name));
            }

            root.RejectUnknownMembers(ScenarioMembers);

            // Whole-scenario checks (unique names, slots on the map, script order and squads) name the entry themselves.
            return Build(root, rootValue.Get("squads")!, () => new ScenarioDef(id, description, map, squads, script, defs.Movement, defs.Formations,
                defs.Combat, defs.Morale, retainers));
        }

        private static ScriptedOrderDef ReadScriptedOrder(JsonValue value, string path, string sourceName)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            int tick = item.RequireInt("tick");
            string order = item.RequireString("order");
            switch (order)
            {
                case "formation":
                {
                    string squad = item.RequireString("squad");
                    string formation = item.RequireString("formation");
                    item.RejectUnknownMembers("tick, order, squad, formation");
                    return Build(item, value, () => ScriptedOrderDef.Formation(tick, squad, formation));
                }

                case "move" when item.Member("retainer") != null:
                {
                    string retainer = item.RequireString("retainer");
                    FixVec2 target = item.RequireVec2("target");
                    Angle? facing = Facing(item.OptionalFix("facing"));
                    item.RejectUnknownMembers("tick, order, retainer, target, facing");
                    return Build(item, value, () => ScriptedOrderDef.MoveRetainer(tick, retainer, target, facing));
                }

                case "move":
                {
                    string squad = item.RequireString("squad");
                    FixVec2 target = item.RequireVec2("target");
                    Angle? facing = Facing(item.OptionalFix("facing"));
                    item.RejectUnknownMembers(ScriptMembers);
                    return Build(item, value, () => new ScriptedOrderDef(tick, squad, target, facing));
                }

                case "attach":
                {
                    string retainer = item.RequireString("retainer");
                    string squad = item.RequireString("squad");
                    item.RejectUnknownMembers("tick, order, retainer, squad");
                    return Build(item, value, () => ScriptedOrderDef.Attach(tick, retainer, squad));
                }

                case "ability":
                {
                    string retainer = item.RequireString("retainer");
                    item.RejectUnknownMembers("tick, order, retainer");
                    return Build(item, value, () => ScriptedOrderDef.Ability(tick, retainer));
                }

                case "detach":
                {
                    string retainer = item.RequireString("retainer");
                    item.RejectUnknownMembers("tick, order, retainer");
                    return Build(item, value, () => ScriptedOrderDef.Detach(tick, retainer));
                }

                case "attack":
                {
                    string retainer = item.RequireString("retainer");
                    int enemy = item.RequireInt("enemy");
                    item.RejectUnknownMembers("tick, order, retainer, enemy");
                    return Build(item, value, () => ScriptedOrderDef.Attack(tick, retainer, new EntityId(enemy)));
                }

                default:
                    throw item.Error(value.Get("order")!, item.MemberPath("order"),
                        $"unknown order '{order}' (known: move, formation, attach, detach, attack, ability)");
            }
        }

        private static Angle? Facing(Fix? degrees) => degrees.HasValue ? Angle.FromDegrees(degrees.Value) : (Angle?)null;

        private static RetainerSpawnDef ReadRetainer(JsonValue value, string path, string sourceName, DefLibrary defs)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            string name = item.RequireString("name");
            int side = item.RequireInt("side");
            string traitId = item.RequireString("trait");
            string bodyId = item.RequireString("body");
            string experienceId = item.RequireString("experience");
            string kitId = item.RequireString("kit");
            string? squad = item.OptionalString("squad");
            bool alone = item.Member("position") != null || item.Member("facing") != null;
            FixVec2 position = alone ? item.RequireVec2("position") : FixVec2.Zero;
            Fix facing = alone ? item.RequireFix("facing") : Fix.Zero;
            item.RejectUnknownMembers(RetainerMembers);
            if (squad != null && alone)
            {
                throw item.Error("give either 'squad' (he starts in it) or 'position' and 'facing' (he starts alone), not both");
            }

            if (squad == null && !alone)
            {
                throw item.Error("give 'squad' (he starts in it) or 'position' and 'facing' (he starts alone)");
            }

            TraitDef? trait = defs.FindTrait(traitId);
            if (trait == null)
            {
                throw item.Error(value.Get("trait")!, item.MemberPath("trait"), $"unknown trait '{traitId}' (known: {defs.TraitIds()})");
            }

            BodyDef? body = defs.FindBody(bodyId);
            if (body == null)
            {
                throw item.Error(value.Get("body")!, item.MemberPath("body"), $"unknown body '{bodyId}' (known: {defs.BodyIds()})");
            }

            CombatDef rules = defs.Rules;
            ExperienceDef? experience = rules.FindExperience(experienceId);
            if (experience == null)
            {
                throw item.Error(value.Get("experience")!, item.MemberPath("experience"),
                    $"unknown experience level '{experienceId}' (known: {Ids(rules.ExperienceLevels, e => e.Id)})");
            }

            KitDef? kit = rules.FindKit(kitId);
            if (kit == null)
            {
                throw item.Error(value.Get("kit")!, item.MemberPath("kit"), $"unknown kit '{kitId}' (known: {Ids(rules.Kits, k => k.Id)})");
            }

            return squad != null
                ? Build(item, value, () => RetainerSpawnDef.InSquad(name, side, trait, body, experience, kit, squad))
                : Build(item, value, () => RetainerSpawnDef.Alone(name, side, trait, body, experience, kit, position, Angle.FromDegrees(facing)));
        }

        private static SquadSpawnDef ReadSquad(JsonValue value, string path, string sourceName, DefLibrary defs)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            string name = item.RequireString("name");
            int side = item.RequireInt("side");
            JsonValue? oldFighter = item.Member("fighter");
            if (oldFighter != null)
            {
                throw item.Error(oldFighter, item.MemberPath("fighter"),
                    "'fighter' and 'count' became 'body' and 'men' (M3 step 9, ADR-0005), e.g. \"body\": \"man\", \"men\": [{ \"count\": 8, \"experience\": \"experienced\", \"kit\": \"sword-shield-padded\" }]");
            }

            string bodyId = item.RequireString("body");
            IReadOnlyList<JsonValue> menItems = item.RequireArray("men");
            string formationId = item.RequireString("formation");
            FixVec2 position = item.RequireVec2("position");
            Angle facing = Angle.FromDegrees(item.RequireFix("facing"));
            item.RejectUnknownMembers(SquadMembers);

            BodyDef? body = defs.FindBody(bodyId);
            if (body == null)
            {
                throw item.Error(value.Get("body")!, item.MemberPath("body"), $"unknown body '{bodyId}' (known: {defs.BodyIds()})");
            }

            if (menItems.Count == 0)
            {
                throw item.Error(value.Get("men")!, item.MemberPath("men"), "a squad needs at least one group of men");
            }

            var men = new List<SquadMenDef>(menItems.Count);
            for (int j = 0; j < menItems.Count; j++)
            {
                men.Add(ReadMen(menItems[j], item.MemberPath($"men[{j}]"), sourceName, defs.Rules));
            }

            FormationDef? formation = defs.FindFormation(formationId);
            if (formation == null)
            {
                throw item.Error(value.Get("formation")!, item.MemberPath("formation"),
                    $"unknown formation '{formationId}' (known: {defs.FormationIds()})");
            }

            return Build(item, value, () => new SquadSpawnDef(name, side, body, men, formation, position, facing));
        }

        private static SquadMenDef ReadMen(JsonValue value, string path, string sourceName, CombatDef rules)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            int count = item.RequireInt("count");
            string experienceId = item.RequireString("experience");
            string kitId = item.RequireString("kit");
            item.RejectUnknownMembers(MenMembers);
            ExperienceDef? experience = rules.FindExperience(experienceId);
            if (experience == null)
            {
                throw item.Error(value.Get("experience")!, item.MemberPath("experience"),
                    $"unknown experience level '{experienceId}' (known: {Ids(rules.ExperienceLevels, e => e.Id)})");
            }

            if (experience.RetainerOnly)
            {
                throw item.Error(value.Get("experience")!, item.MemberPath("experience"),
                    $"'{experienceId}' is for Retainers only (D-019): list him in 'retainers'");
            }

            KitDef? kit = rules.FindKit(kitId);
            if (kit == null)
            {
                throw item.Error(value.Get("kit")!, item.MemberPath("kit"), $"unknown kit '{kitId}' (known: {Ids(rules.Kits, k => k.Id)})");
            }

            return Build(item, value, () => new SquadMenDef(count, experience, kit));
        }

        private static string Ids<T>(IReadOnlyList<T> defs, Func<T, string> idOf)
        {
            var ids = new string[defs.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = idOf(defs[i]);
            }

            return string.Join(", ", ids);
        }

        /// <summary>Runs a def constructor and turns its <see cref="ArgumentException"/> into a located content error.</summary>
        /// <summary>A wall's optional <c>kind</c>: "house" or "obstacle" (the default).</summary>
        private static WallKind WallKindOf(JsonObjectReader wall)
        {
            string? kind = wall.OptionalString("kind");
            switch (kind)
            {
                case null:
                case "obstacle":
                    return WallKind.Obstacle;
                case "house":
                    return WallKind.House;
                default:
                    throw wall.Error(wall.Member("kind")!, wall.MemberPath("kind"), $"unknown wall kind '{kind}' (known: obstacle, house)");
            }
        }

        private static T Build<T>(JsonObjectReader reader, JsonValue at, Func<T> create)
        {
            try
            {
                return create();
            }
            catch (ArgumentException ex)
            {
                throw JsonObjectReader.FromDef(reader.Source, at, reader.Path, ex);
            }
        }
    }
}
