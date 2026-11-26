#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Knez.Sim.Content;
using Knez.Sim.Content.Json;

namespace Knez.Game.View
{
    /// <summary>
    /// The art the view can draw: the manifest written by <c>tools/art/export_game_art.js</c> (<c>art-manifest.json</c>,
    /// docs/art-assets.md) and the map colours of its palette. Plain C#: <see cref="ArtLibrary"/> loads the text and the
    /// sprites in Unity, tests read the same files from disk. Paths in it are Resources paths (docs/art-pipeline.md).
    /// </summary>
    public sealed class ArtCatalog
    {
        /// <summary>The manifest version this reader understands.</summary>
        public const int Version = 1;

        /// <summary>No art at all: every lookup misses and the view draws its primitives.</summary>
        public static readonly ArtCatalog Empty =
            new ArtCatalog(new TokenArt[0], new TokenArt[0], new MapObjectArt[0], new Dictionary<string, string>());

        /// <summary>Holds parsed art; see <see cref="Parse"/>.</summary>
        public ArtCatalog(IReadOnlyList<TokenArt> fighters, IReadOnlyList<TokenArt> heroes, IReadOnlyList<MapObjectArt> map,
            IReadOnlyDictionary<string, string> mapColours)
        {
            Fighters = fighters;
            Heroes = heroes;
            Map = map;
            MapColours = mapColours;
        }

        /// <summary>Tokens of unnamed fighters: art side × kit × variant.</summary>
        public IReadOnlyList<TokenArt> Fighters { get; }

        /// <summary>Tokens of Retainers: named ones of the design roster, then generic ones per side and kit.</summary>
        public IReadOnlyList<TokenArt> Heroes { get; }

        /// <summary>Map objects seen from above, in catalogue order.</summary>
        public IReadOnlyList<MapObjectArt> Map { get; }

        /// <summary>The palette's map colours by name (<c>grass</c>, <c>earth</c>, <c>stone</c>, …) as <c>#RRGGBB</c>.</summary>
        public IReadOnlyDictionary<string, string> MapColours { get; }

        /// <summary>True when there is nothing to draw.</summary>
        public bool IsEmpty => Fighters.Count == 0 && Heroes.Count == 0 && Map.Count == 0;

        /// <summary>The map object called <paramref name="id"/>, or null.</summary>
        public MapObjectArt? FindMap(string id)
        {
            foreach (MapObjectArt item in Map)
            {
                if (string.Equals(item.Id, id, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the manifest text and, when given, the palette text. Throws <see cref="ContentException"/> for text that
        /// isn't JSON and <see cref="FormatException"/> for JSON that isn't a version-1 manifest.
        /// </summary>
        public static ArtCatalog Parse(string manifest, string? palette = null)
        {
            JsonValue root = JsonReader.Parse(new SourceText("art-manifest.json", manifest));
            int version = Int(Require(root, "version", "manifest"), "version");
            if (version != Version)
            {
                throw new FormatException($"art-manifest.json: version {version}, this view reads version {Version}");
            }

            var fighters = new List<TokenArt>();
            foreach (JsonValue item in Items(root, "fighters"))
            {
                string token = Text(item, "token");
                fighters.Add(new TokenArt(Name(token), Text(item, "side"), Text(item, "kit"), Text(item, "experience"),
                    OptionalInt(item, "variant"), false, token));
            }

            var heroes = new List<TokenArt>();
            foreach (JsonValue item in Items(root, "heroes"))
            {
                JsonValue? named = item.Get("named");
                heroes.Add(new TokenArt(Text(item, "id"), Text(item, "side"), Text(item, "kit"), Text(item, "experience"),
                    OptionalInt(item, "variant"), named != null && named.Kind == JsonKind.Boolean && named.Text == "true", Text(item, "token")));
            }

            var map = new List<MapObjectArt>();
            foreach (JsonValue item in Items(root, "map"))
            {
                map.Add(MapObject(item));
            }

            return new ArtCatalog(fighters, heroes, map, palette == null ? new Dictionary<string, string>() : MapPalette(palette));
        }

        private static MapObjectArt MapObject(JsonValue item)
        {
            string id = Text(item, "id");
            JsonValue? walls = item.Get("walls");
            int count = 0;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            if (walls != null && walls.Kind == JsonKind.Array)
            {
                foreach (JsonValue wall in walls.Items)
                {
                    IReadOnlyList<JsonValue> centre = Items(wall, "center");
                    IReadOnlyList<JsonValue> size = Items(wall, "size");
                    if (centre.Count != 2 || size.Count != 2)
                    {
                        throw new FormatException($"art-manifest.json: map {id}: a wall's center and size are [x, y]");
                    }

                    float cx = Float(centre[0], "center"), cy = Float(centre[1], "center");
                    float w = Float(size[0], "size"), h = Float(size[1], "size");
                    float x0 = cx - (w / 2), y0 = cy - (h / 2), x1 = cx + (w / 2), y1 = cy + (h / 2);
                    if (count == 0)
                    {
                        (minX, minY, maxX, maxY) = (x0, y0, x1, y1);
                    }
                    else
                    {
                        (minX, minY, maxX, maxY) = (Math.Min(minX, x0), Math.Min(minY, y0), Math.Max(maxX, x1), Math.Max(maxY, y1));
                    }

                    count++;
                }
            }

            return new MapObjectArt(id, Text(item, "sim"), OptionalString(item, "group"), Text(item, "sprite"), count,
                (minX + maxX) / 2, (minY + maxY) / 2, maxX - minX, maxY - minY);
        }

        private static Dictionary<string, string> MapPalette(string palette)
        {
            JsonValue root = JsonReader.Parse(new SourceText("palette.json", palette));
            var colours = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonValue? map = root.Get("map");
            if (map != null && map.Kind == JsonKind.Object)
            {
                foreach (JsonMember member in map.Members)
                {
                    if (member.Value.Kind == JsonKind.String)
                    {
                        colours[member.Name] = member.Value.Text;
                    }
                }
            }

            return colours;
        }

        // The token's file name: "Tokens/tok_clan_axe-shield_0" → "tok_clan_axe-shield_0".
        private static string Name(string path) => path.Substring(path.LastIndexOf('/') + 1);

        private static JsonValue Require(JsonValue item, string name, string where)
        {
            JsonValue? value = item.Get(name);
            if (value == null)
            {
                throw new FormatException($"art-manifest.json:{item.Line}: {where} has no \"{name}\"");
            }

            return value;
        }

        private static IReadOnlyList<JsonValue> Items(JsonValue item, string name)
        {
            JsonValue value = Require(item, name, "an entry");
            if (value.Kind != JsonKind.Array)
            {
                throw new FormatException($"art-manifest.json:{value.Line}: \"{name}\" is {value.Describe()}, not an array");
            }

            return value.Items;
        }

        private static string Text(JsonValue item, string name)
        {
            JsonValue value = Require(item, name, "an entry");
            if (value.Kind != JsonKind.String)
            {
                throw new FormatException($"art-manifest.json:{value.Line}: \"{name}\" is {value.Describe()}, not a string");
            }

            return value.Text;
        }

        private static string OptionalString(JsonValue item, string name)
        {
            JsonValue? value = item.Get(name);
            return value != null && value.Kind == JsonKind.String ? value.Text : "";
        }

        private static int OptionalInt(JsonValue item, string name)
        {
            JsonValue? value = item.Get(name);
            return value == null || value.Kind == JsonKind.Null ? 0 : Int(value, name);
        }

        private static int Int(JsonValue value, string name)
        {
            if (value.Kind != JsonKind.Number || !int.TryParse(value.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw new FormatException($"art-manifest.json:{value.Line}: \"{name}\" is not a whole number");
            }

            return result;
        }

        private static float Float(JsonValue value, string name)
        {
            if (value.Kind != JsonKind.Number || !float.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            {
                throw new FormatException($"art-manifest.json:{value.Line}: \"{name}\" is not a number");
            }

            return result;
        }
    }
}
