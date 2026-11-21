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
    ///   "map": { "width": 60, "height": 40, "walls": [ { "from": [0, 20], "to": [28, 21] } ] },
    ///   "squads": [ { "name": "Line", "side": 0, "fighter": "warrior", "count": 8, "formation": "line",
    ///                 "position": [20, 12], "facing": 90 } ],
    ///   "script": [ { "tick": 20, "order": "move", "squad": "Line", "target": [20, 24], "facing": 90 } ] }
    /// </code>
    /// Metres for lengths, degrees for <c>facing</c> (0 = east, 90 = north). Numbers may have up to 9 decimals.
    /// <c>script</c> is optional and in tick order; a scripted <c>facing</c> is optional (default: the march direction).
    /// </summary>
    public static class ScenarioLoader
    {
        private const string ScenarioMembers = "id, description, map, squads, script";
        private const string ScriptMembers = "tick, order, squad, target, facing";
        private const string MapMembers = "width, height, walls";
        private const string WallMembers = "from, to";
        private const string SquadMembers = "name, side, fighter, count, formation, position, facing";

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
                wall.RejectUnknownMembers(WallMembers);
                walls.Add(Build(wall, wallItems[i], () => new WallDef(from, to)));
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

            IReadOnlyList<JsonValue> scriptItems = root.OptionalArray("script");
            var script = new List<ScriptedOrderDef>(scriptItems.Count);
            for (int i = 0; i < scriptItems.Count; i++)
            {
                script.Add(ReadScriptedOrder(scriptItems[i], $"script[{i}]", source.Name));
            }

            root.RejectUnknownMembers(ScenarioMembers);

            // Whole-scenario checks (unique names, slots on the map, script order and squads) name the entry themselves.
            return Build(root, rootValue.Get("squads")!, () => new ScenarioDef(id, description, map, squads, script));
        }

        private static ScriptedOrderDef ReadScriptedOrder(JsonValue value, string path, string sourceName)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            int tick = item.RequireInt("tick");
            string order = item.RequireString("order");
            string squad = item.RequireString("squad");
            FixVec2 target = item.RequireVec2("target");
            Fix? facingDegrees = item.OptionalFix("facing");
            item.RejectUnknownMembers(ScriptMembers);
            if (order != "move")
            {
                throw item.Error(value.Get("order")!, item.MemberPath("order"), $"unknown order '{order}' (known: move)");
            }

            Angle? facing = facingDegrees.HasValue ? Angle.FromDegrees(facingDegrees.Value) : (Angle?)null;
            return Build(item, value, () => new ScriptedOrderDef(tick, squad, target, facing));
        }

        private static SquadSpawnDef ReadSquad(JsonValue value, string path, string sourceName, DefLibrary defs)
        {
            var item = JsonObjectReader.Of(value, path, sourceName);
            string name = item.RequireString("name");
            int side = item.RequireInt("side");
            string fighterId = item.RequireString("fighter");
            int count = item.RequireInt("count");
            string formationId = item.RequireString("formation");
            FixVec2 position = item.RequireVec2("position");
            Angle facing = Angle.FromDegrees(item.RequireFix("facing"));
            item.RejectUnknownMembers(SquadMembers);

            FighterDef? fighter = defs.FindFighter(fighterId);
            if (fighter == null)
            {
                throw item.Error(value.Get("fighter")!, item.MemberPath("fighter"),
                    $"unknown fighter '{fighterId}' (known: {defs.FighterIds()})");
            }

            FormationDef? formation = defs.FindFormation(formationId);
            if (formation == null)
            {
                throw item.Error(value.Get("formation")!, item.MemberPath("formation"),
                    $"unknown formation '{formationId}' (known: {defs.FormationIds()})");
            }

            return Build(item, value, () => new SquadSpawnDef(name, side, fighter, count, formation, position, facing));
        }

        /// <summary>Runs a def constructor and turns its <see cref="ArgumentException"/> into a located content error.</summary>
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
