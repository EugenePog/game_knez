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
    ///   "map": { "width": 60, "height": 40 },
    ///   "squads": [ { "name": "Line", "side": 0, "fighter": "warrior", "count": 8, "formation": "line",
    ///                 "position": [20, 12], "facing": 90 } ] }
    /// </code>
    /// Metres for lengths, degrees for <c>facing</c> (0 = east, 90 = north). Numbers may have up to 9 decimals.
    /// </summary>
    public static class ScenarioLoader
    {
        private const string ScenarioMembers = "id, description, map, squads";
        private const string MapMembers = "width, height";
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
            mapReader.RejectUnknownMembers(MapMembers);
            JsonValue mapValue = rootValue.Get("map")!;
            MapDef map = Build(mapReader, mapValue, () => new MapDef(width, height));

            IReadOnlyList<JsonValue> squadItems = root.RequireArray("squads");
            var squads = new List<SquadSpawnDef>(squadItems.Count);
            for (int i = 0; i < squadItems.Count; i++)
            {
                squads.Add(ReadSquad(squadItems[i], $"squads[{i}]", source.Name, defs));
            }

            root.RejectUnknownMembers(ScenarioMembers);

            // Whole-scenario checks (unique names, slots on the map) name the squad as squads[i] themselves.
            return Build(root, rootValue.Get("squads")!, () => new ScenarioDef(id, description, map, squads));
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
