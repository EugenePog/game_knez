using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Content.Json;
using Knez.Sim.Defs;

namespace Knez.Sim.Content
{
    /// <summary>
    /// All defs from the content def files (<c>content/defs/*.json</c>), looked up by id. Each file is an object with
    /// optional <c>fighters</c> and <c>formations</c> arrays, an optional <c>movement</c> object (the movement rules: in
    /// at most one file) and an optional <c>description</c>; ids are unique across all files. Lists are sorted by id
    /// (ordinal), so the order of files never matters.
    /// </summary>
    public sealed class DefLibrary
    {
        private const string FileMembers = "description, fighters, formations, movement";
        private const string MovementMembers = "pivotTolerance, slotCatchUpTime, personalSpace, separationTime, holdingReach, " +
            "formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead, regroupMaxWait, regroupCost";
        private const string FighterMembers = "id, radius, walkSpeed, runSpeed, turnRate";
        private const string FormationMembers = "id, spacing, rankSpacing, ranks, width, turnRate, holdWeight, allyPassSpeed";

        private readonly Dictionary<string, FighterDef> _fighters;
        private readonly Dictionary<string, FormationDef> _formations;

        private DefLibrary(List<FighterDef> fighters, List<FormationDef> formations, MovementDef? movement)
        {
            Movement = movement;
            fighters.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            formations.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Fighters = new ReadOnlyCollection<FighterDef>(fighters);
            Formations = new ReadOnlyCollection<FormationDef>(formations);
            _fighters = new Dictionary<string, FighterDef>(StringComparer.Ordinal);
            _formations = new Dictionary<string, FormationDef>(StringComparer.Ordinal);
            for (int i = 0; i < fighters.Count; i++)
            {
                _fighters.Add(fighters[i].Id, fighters[i]);
            }

            for (int i = 0; i < formations.Count; i++)
            {
                _formations.Add(formations[i].Id, formations[i]);
            }
        }

        /// <summary>Fighter defs sorted by id.</summary>
        public IReadOnlyList<FighterDef> Fighters { get; }

        /// <summary>Formation defs sorted by id.</summary>
        public IReadOnlyList<FormationDef> Formations { get; }

        /// <summary>The movement rules, or null when no def file has a <c>movement</c> object.</summary>
        public MovementDef? Movement { get; }

        /// <summary>Parses def files; throws <see cref="ContentException"/> on the first problem.</summary>
        public static DefLibrary Parse(IReadOnlyList<SourceText> files)
        {
            var fighters = new List<FighterDef>();
            var formations = new List<FormationDef>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal); // "kind:id" → where it was defined
            MovementDef? movement = null;

            for (int f = 0; f < files.Count; f++)
            {
                SourceText file = files[f];
                JsonValue rootValue = JsonReader.Parse(file);
                var root = JsonObjectReader.Of(rootValue, string.Empty, file.Name);
                root.OptionalString("description");

                IReadOnlyList<JsonValue> fighterItems = root.OptionalArray("fighters");
                for (int i = 0; i < fighterItems.Count; i++)
                {
                    var item = JsonObjectReader.Of(fighterItems[i], $"fighters[{i}]", file.Name);
                    string id = item.RequireString("id");
                    var radius = item.RequireFix("radius");
                    var walkSpeed = item.RequireFix("walkSpeed");
                    var runSpeed = item.RequireFix("runSpeed");
                    var turnRate = item.RequireFix("turnRate");
                    item.RejectUnknownMembers(FighterMembers);
                    Claim(seen, "fighter", id, item, fighterItems[i], file.Name);
                    fighters.Add(Build(item, fighterItems[i], () => new FighterDef(id, radius, walkSpeed, runSpeed, turnRate)));
                }

                IReadOnlyList<JsonValue> formationItems = root.OptionalArray("formations");
                for (int i = 0; i < formationItems.Count; i++)
                {
                    var item = JsonObjectReader.Of(formationItems[i], $"formations[{i}]", file.Name);
                    string id = item.RequireString("id");
                    var spacing = item.RequireFix("spacing");
                    var rankSpacing = item.RequireFix("rankSpacing");
                    int ranks = item.OptionalInt("ranks", 0);
                    int width = item.OptionalInt("width", 0);
                    var formationTurnRate = item.RequireFix("turnRate");
                    int holdWeight = item.RequireInt("holdWeight");
                    var allyPassSpeed = item.RequireFix("allyPassSpeed");
                    item.RejectUnknownMembers(FormationMembers);
                    Claim(seen, "formation", id, item, formationItems[i], file.Name);
                    formations.Add(Build(item, formationItems[i],
                        () => new FormationDef(id, spacing, rankSpacing, ranks, width, formationTurnRate, holdWeight, allyPassSpeed)));
                }

                JsonObjectReader? movementReader = root.OptionalObject("movement");
                if (movementReader != null)
                {
                    var item = movementReader;
                    JsonValue movementValue = rootValue.Get("movement")!;
                    Claim(seen, "movement", "rules", item, movementValue, file.Name);
                    var pivotTolerance = item.RequireFix("pivotTolerance");
                    var slotCatchUpTime = item.RequireFix("slotCatchUpTime");
                    var personalSpace = item.RequireFix("personalSpace");
                    var separationTime = item.RequireFix("separationTime");
                    var holdingReach = item.RequireFix("holdingReach");
                    var formedUpReach = item.RequireFix("formedUpReach");
                    int collisionPasses = item.RequireInt("collisionPasses");
                    var allyPartingDepth = item.RequireFix("allyPartingDepth");
                    int pathBudget = item.RequireInt("pathBudget");
                    var regroupLead = item.RequireFix("regroupLead");
                    var regroupMaxWait = item.RequireFix("regroupMaxWait");
                    var regroupCost = item.RequireFix("regroupCost");
                    item.RejectUnknownMembers(MovementMembers);
                    movement = Build(item, movementValue, () => new MovementDef(pivotTolerance, slotCatchUpTime, personalSpace,
                        separationTime, holdingReach, formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead,
                        regroupMaxWait, regroupCost));
                }

                root.RejectUnknownMembers(FileMembers);
            }

            return new DefLibrary(fighters, formations, movement);
        }

        /// <summary>The fighter def with <paramref name="id"/>, or null.</summary>
        public FighterDef? FindFighter(string id) => _fighters.TryGetValue(id, out FighterDef? def) ? def : null;

        /// <summary>The formation def with <paramref name="id"/>, or null.</summary>
        public FormationDef? FindFormation(string id) => _formations.TryGetValue(id, out FormationDef? def) ? def : null;

        /// <summary>Known fighter ids, for "unknown fighter" messages.</summary>
        public string FighterIds() => JoinIds(Fighters, d => d.Id);

        /// <summary>Known formation ids, for "unknown formation" messages.</summary>
        public string FormationIds() => JoinIds(Formations, d => d.Id);

        private static void Claim(Dictionary<string, string> seen, string kind, string id, JsonObjectReader item,
            JsonValue at, string source)
        {
            string key = kind + ":" + id;
            string here = $"{source}:{at.Line}:{at.Column}";
            if (seen.TryGetValue(key, out string? first))
            {
                throw item.Error(at, item.MemberPath("id"), $"{kind} '{id}' is already defined at {first}");
            }

            seen.Add(key, here);
        }

        private static T Build<T>(JsonObjectReader item, JsonValue at, Func<T> create)
        {
            try
            {
                return create();
            }
            catch (ArgumentException ex)
            {
                throw JsonObjectReader.FromDef(item.Source, at, item.Path, ex);
            }
        }

        private static string JoinIds<T>(IReadOnlyList<T> defs, Func<T, string> id)
        {
            if (defs.Count == 0)
            {
                return "none";
            }

            var names = new string[defs.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = id(defs[i]);
            }

            return string.Join(", ", names);
        }
    }
}
