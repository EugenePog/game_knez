using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Content.Json;
using Knez.Sim.Defs;
using Knez.Sim.Numerics;

namespace Knez.Sim.Content
{
    /// <summary>
    /// All defs from the content def files (<c>content/defs/*.json</c>), looked up by id. Each file is an object with
    /// optional <c>fighters</c> and <c>formations</c> arrays, optional <c>movement</c> and <c>combat</c> objects (the
    /// movement and combat rules: each in at most one file) and an optional <c>description</c>; ids are unique across all
    /// files. Lists are sorted by id (ordinal), so the order of files never matters. Fighters name their kit (weapon,
    /// shield, bodyArmour, headCover, experience, type) by ids from the combat rules — <see cref="CombatDef.Standard"/>
    /// when no file has them — and take the rules' default kit for what they leave out.
    /// </summary>
    public sealed class DefLibrary
    {
        private const string FileMembers = "description, fighters, formations, movement, combat, morale";
        private const string MoraleMembers = "shaken, wavering, routing, hysteresis, lossWeight, ownLightWound, ownHeavyWound, " +
            "mateHeavyWound, flankBlow, rearBlow, mateFlankBlow, mateRearBlow, mateRouts, allySquadBroke, enemySquadBroke, nearby, " +
            "recoveryPerSecond, safeDistance, rallyAt, breakShare, reformShare, waveringHitPenalty";
        private const string MovementMembers = "pivotTolerance, slotCatchUpTime, personalSpace, separationTime, holdingReach, " +
            "formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead, regroupMaxWait, regroupCost, reformPace, stepBackPace, withdrawClearance, closeInRange";
        private const string FighterMembers = "id, radius, walkSpeed, runSpeed, turnRate, weapon, shield, bodyArmour, headCover, experience, type";
        private const string CombatMembers = "weapons, shields, bodyArmour, headCover, experience, warriorTypes, hitChance, defaultKit, " +
            "hitLimits, position, strikeArc, wounded";
        private const string WeaponMembers = "id, reach, windup, recovery, wounds";
        private const string KitMembers = "weapon, shield, bodyArmour, headCover, experience, type";
        private const string FormationMembers = "id, spacing, rankSpacing, ranks, width, turnRate, holdWeight, allyPassSpeed, shieldWall, keepsRanks";

        private readonly Dictionary<string, FighterDef> _fighters;
        private readonly Dictionary<string, FormationDef> _formations;

        private DefLibrary(List<FighterDef> fighters, List<FormationDef> formations, MovementDef? movement, CombatDef? combat,
            MoraleDef? morale)
        {
            Movement = movement;
            Morale = morale;
            Combat = combat;
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

        /// <summary>The morale rules, or null when no def file has a <c>morale</c> object.</summary>
        public MoraleDef? Morale { get; }

        /// <summary>The combat rules, or null when no def file has a <c>combat</c> object.</summary>
        public CombatDef? Combat { get; }

        /// <summary>Parses def files; throws <see cref="ContentException"/> on the first problem.</summary>
        public static DefLibrary Parse(IReadOnlyList<SourceText> files)
        {
            var fighters = new List<FighterDef>();
            var formations = new List<FormationDef>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal); // "kind:id" → where it was defined
            MovementDef? movement = null;
            CombatDef? combat = null;
            MoraleDef? morale = null;
            var pendingFighters = new List<PendingFighter>(); // built once every file is read: kits need the combat rules

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
                    var kit = KitIds.Read(item);
                    item.RejectUnknownMembers(FighterMembers);
                    Claim(seen, "fighter", id, item, fighterItems[i], file.Name);
                    pendingFighters.Add(new PendingFighter(item, fighterItems[i], kit,
                        k => new FighterDef(id, radius, walkSpeed, runSpeed, turnRate, k)));
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
                    bool shieldWall = item.OptionalBool("shieldWall", false);
                    bool keepsRanks = item.OptionalBool("keepsRanks", false);
                    item.RejectUnknownMembers(FormationMembers);
                    Claim(seen, "formation", id, item, formationItems[i], file.Name);
                    formations.Add(Build(item, formationItems[i],
                        () => new FormationDef(id, spacing, rankSpacing, ranks, width, formationTurnRate, holdWeight, allyPassSpeed,
                        shieldWall, keepsRanks)));
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
                    var reformPace = item.RequireFix("reformPace");
                    var stepBackPace = item.RequireFix("stepBackPace");
                    var withdrawClearance = item.RequireFix("withdrawClearance");
                    var closeInRange = item.RequireFix("closeInRange");
                    item.RejectUnknownMembers(MovementMembers);
                    movement = Build(item, movementValue, () => new MovementDef(pivotTolerance, slotCatchUpTime, personalSpace,
                        separationTime, holdingReach, formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead,
                        regroupMaxWait, regroupCost, reformPace, stepBackPace, withdrawClearance,
                        closeInRange));
                }

                JsonObjectReader? combatReader = root.OptionalObject("combat");
                if (combatReader != null)
                {
                    JsonValue combatValue = rootValue.Get("combat")!;
                    Claim(seen, "combat", "rules", combatReader, combatValue, file.Name);
                    combat = ReadCombat(combatReader, combatValue);
                }

                JsonObjectReader? moraleReader = root.OptionalObject("morale");
                if (moraleReader != null)
                {
                    JsonValue moraleValue = rootValue.Get("morale")!;
                    Claim(seen, "morale", "rules", moraleReader, moraleValue, file.Name);
                    morale = ReadMorale(moraleReader, moraleValue);
                }

                root.RejectUnknownMembers(FileMembers);
            }

            CombatDef rules = combat ?? CombatDef.Standard;
            for (int i = 0; i < pendingFighters.Count; i++)
            {
                PendingFighter pending = pendingFighters[i];
                FighterKit kit = pending.Kit.Resolve(rules, rules.DefaultKit, pending.Item);
                fighters.Add(Build(pending.Item, pending.At, () => pending.Create(kit)));
            }

            return new DefLibrary(fighters, formations, movement, combat, morale);
        }

        private static CombatDef ReadCombat(JsonObjectReader item, JsonValue at)
        {
            var weapons = new List<WeaponDef>();
            IReadOnlyList<JsonValue> weaponItems = NonEmpty(item, "weapons", "weapon");
            for (int i = 0; i < weaponItems.Count; i++)
            {
                var weapon = JsonObjectReader.Of(weaponItems[i], item.MemberPath($"weapons[{i}]"), item.Source);
                string id = weapon.RequireString("id");
                UniqueId(weapons, id, "weapon", weapon);
                Fix reach = weapon.RequireFix("reach");
                Fix windup = weapon.RequireFix("windup");
                Fix recovery = weapon.RequireFix("recovery");
                var wounds = weapon.RequireObject("wounds");
                int light = wounds.RequireInt("light");
                int heavy = wounds.RequireInt("heavy");
                int down = wounds.RequireInt("down");
                wounds.RejectUnknownMembers("light, heavy, down");
                weapon.RejectUnknownMembers(WeaponMembers);
                weapons.Add(Build(weapon, weaponItems[i], () => new WeaponDef(id, reach, windup, recovery, light, heavy, down)));
            }

            List<ProtectionDef> shields = ReadPieces(item, "shields", "shield", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ProtectionDef> bodyArmours = ReadPieces(item, "bodyArmour", "body armour", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ProtectionDef> headCovers = ReadPieces(item, "headCover", "head cover", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ExperienceDef> levels = ReadExperience(item);
            List<WarriorTypeDef> types = ReadPieces(item, "warriorTypes", "warrior type", "defence", (id, v) => new WarriorTypeDef(id, v), d => d.Id);

            // The matrix: one object per weapon, one value per experience level, keyed by ids so a tuner can't shift a
            // column by accident. Every cell is required; strangers are errors.
            var matrix = item.RequireObject("hitChance");
            var rows = new List<IReadOnlyList<int>>(weapons.Count);
            var weaponIds = new string[weapons.Count];
            for (int w = 0; w < weapons.Count; w++)
            {
                weaponIds[w] = weapons[w].Id;
                var row = matrix.RequireObject(weapons[w].Id);
                var cells = new int[levels.Count];
                var levelIds = new string[levels.Count];
                for (int e = 0; e < levels.Count; e++)
                {
                    levelIds[e] = levels[e].Id;
                    cells[e] = Permille(row, levels[e].Id);
                }

                row.RejectUnknownMembers(string.Join(", ", levelIds));
                rows.Add(cells);
            }

            matrix.RejectUnknownMembers(string.Join(", ", weaponIds));

            var kitReader = item.RequireObject("defaultKit");
            KitIds kitIds = KitIds.Read(kitReader);
            kitReader.RejectUnknownMembers(KitMembers);
            if (!kitIds.Complete)
            {
                throw kitReader.Error($"the default kit names every part ({KitMembers})");
            }

            var limits = item.RequireObject("hitLimits");
            int minHit = Permille(limits, "min");
            int maxHit = Permille(limits, "max");
            limits.RejectUnknownMembers("min, max");
            var position = item.RequireObject("position");
            int flank = Permille(position, "flank");
            int rear = Permille(position, "rear");
            int shieldFromFlank = Permille(position, "shieldFromFlank");
            int shieldFromRear = Permille(position, "shieldFromRear");
            int shieldWall = Permille(position, "shieldWall");
            Fix frontArc = position.RequireFix("frontArc");
            Fix rearArc = position.RequireFix("rearArc");
            position.RejectUnknownMembers("frontArc, rearArc, flank, rear, shieldFromFlank, shieldFromRear, shieldWall");
            Fix strikeArc = item.RequireFix("strikeArc");
            var wounded = item.RequireObject("wounded");
            int lightPenalty = Permille(wounded, "lightPenalty");
            int heavyPenalty = Permille(wounded, "heavyPenalty");
            Fix lightPace = wounded.RequireFix("lightPace");
            Fix heavyPace = wounded.RequireFix("heavyPace");
            wounded.RejectUnknownMembers("lightPenalty, heavyPenalty, lightPace, heavyPace");
            item.RejectUnknownMembers(CombatMembers);

            // Catalogue checks (duplicates, matrix shape) first, so the default kit resolves against sound lists.
            CombatDef draft = Build(item, at, () => new CombatDef(weapons, shields, bodyArmours, headCovers, levels, types, rows,
                new FighterKit(weapons[0], shields[0], bodyArmours[0], headCovers[0], levels[0], types[0]), minHit, maxHit, flank,
                rear, shieldFromFlank, shieldFromRear, shieldWall, lightPenalty, heavyPenalty, lightPace, heavyPace,
                frontArc, rearArc, strikeArc));
            FighterKit defaultKit = kitIds.Resolve(draft, null, kitReader);
            return Build(item, at, () => new CombatDef(draft.Weapons, draft.Shields, draft.BodyArmours, draft.HeadCovers,
                draft.ExperienceLevels, draft.WarriorTypes, rows, defaultKit, minHit, maxHit, flank, rear, shieldFromFlank,
                shieldFromRear, shieldWall, lightPenalty, heavyPenalty, lightPace, heavyPace,
                frontArc, rearArc, strikeArc));
        }

        private static MoraleDef ReadMorale(JsonObjectReader m, JsonValue at)
        {
            int shaken = m.RequireInt("shaken");
            int wavering = m.RequireInt("wavering");
            int routing = m.RequireInt("routing");
            int hysteresis = m.RequireInt("hysteresis");
            Fix lossWeight = m.RequireFix("lossWeight");
            int ownLight = m.RequireInt("ownLightWound");
            int ownHeavy = m.RequireInt("ownHeavyWound");
            int mateHeavy = m.RequireInt("mateHeavyWound");
            int flank = m.RequireInt("flankBlow");
            int rear = m.RequireInt("rearBlow");
            int mateFlank = m.RequireInt("mateFlankBlow");
            int mateRear = m.RequireInt("mateRearBlow");
            int mateRouts = m.RequireInt("mateRouts");
            int allyBroke = m.RequireInt("allySquadBroke");
            int enemyBroke = m.RequireInt("enemySquadBroke");
            Fix nearby = m.RequireFix("nearby");
            int recovery = m.RequireInt("recoveryPerSecond");
            Fix safe = m.RequireFix("safeDistance");
            int rallyAt = m.RequireInt("rallyAt");
            int breakShare = m.RequireInt("breakShare");
            int reformShare = m.RequireInt("reformShare");
            int waveringPenalty = m.RequireInt("waveringHitPenalty");
            m.RejectUnknownMembers(MoraleMembers);
            return Build(m, at, () => new MoraleDef(shaken, wavering, routing, hysteresis, lossWeight, ownLight, ownHeavy, mateHeavy,
                flank, rear, mateFlank, mateRear, mateRouts, allyBroke, enemyBroke, nearby, recovery, safe, rallyAt, breakShare,
                reformShare, waveringPenalty));
        }

        private static List<ExperienceDef> ReadExperience(JsonObjectReader combat)
        {
            var list = new List<ExperienceDef>();
            IReadOnlyList<JsonValue> items = NonEmpty(combat, "experience", "experience level");
            for (int i = 0; i < items.Count; i++)
            {
                var level = JsonObjectReader.Of(items[i], combat.MemberPath($"experience[{i}]"), combat.Source);
                string id = level.RequireString("id");
                UniqueId(list, id, "experience level", level, d => d.Id);
                int defence = Permille(level, "defence");
                int morale = Permille(level, "morale");
                level.RejectUnknownMembers("id, defence, morale");
                list.Add(Build(level, items[i], () => new ExperienceDef(id, defence, morale)));
            }

            return list;
        }

        private static List<T> ReadPieces<T>(JsonObjectReader combat, string name, string kind, string valueName,
            Func<string, int, T> create, Func<T, string> idOf)
        {
            var list = new List<T>();
            IReadOnlyList<JsonValue> items = NonEmpty(combat, name, kind);
            for (int i = 0; i < items.Count; i++)
            {
                var piece = JsonObjectReader.Of(items[i], combat.MemberPath($"{name}[{i}]"), combat.Source);
                string id = piece.RequireString("id");
                UniqueId(list, id, kind, piece, idOf);
                int value = Permille(piece, valueName);
                piece.RejectUnknownMembers("id, " + valueName);
                list.Add(Build(piece, items[i], () => create(id, value)));
            }

            return list;
        }

        /// <summary>A required array with at least one entry, located error otherwise (whole-rules checks would report it
        /// against the combat object, in runtime-dependent words).</summary>
        private static IReadOnlyList<JsonValue> NonEmpty(JsonObjectReader combat, string name, string kind)
        {
            IReadOnlyList<JsonValue> items = combat.RequireArray(name);
            if (items.Count == 0)
            {
                throw combat.Error(combat.Member(name)!, combat.MemberPath(name), $"the rules need at least one {kind}");
            }

            return items;
        }

        private static void UniqueId(List<WeaponDef> weapons, string id, string kind, JsonObjectReader item) =>
            UniqueId(weapons, id, kind, item, w => w.Id);

        private static void UniqueId<T>(List<T> earlier, string id, string kind, JsonObjectReader item, Func<T, string> idOf)
        {
            for (int i = 0; i < earlier.Count; i++)
            {
                if (string.Equals(idOf(earlier[i]), id, StringComparison.Ordinal))
                {
                    throw item.Error(item.Member("id")!, item.MemberPath("id"), $"{kind} '{id}' is listed twice");
                }
            }
        }

        /// <summary>A required whole number 0..1000, with the error at the member itself.</summary>
        private static int Permille(JsonObjectReader item, string name)
        {
            int value = item.RequireInt(name);
            if (value < 0 || value > 1000)
            {
                throw item.Error(item.Member(name)!, item.MemberPath(name), $"must be 0..1000 (got {value})");
            }

            return value;
        }

        /// <summary>The fighter def with <paramref name="id"/>, or null.</summary>
        public FighterDef? FindFighter(string id) => _fighters.TryGetValue(id, out FighterDef? def) ? def : null;

        /// <summary>The formation def with <paramref name="id"/>, or null.</summary>
        public FormationDef? FindFormation(string id) => _formations.TryGetValue(id, out FormationDef? def) ? def : null;

        /// <summary>Known fighter ids, for "unknown fighter" messages.</summary>
        public string FighterIds() => JoinIds(Fighters, d => d.Id);

        /// <summary>Known formation ids, for "unknown formation" messages.</summary>
        public string FormationIds() => JoinIds(Formations, d => d.Id);

        /// <summary>A fighter read from a file, waiting for the combat rules to resolve his kit.</summary>
        private sealed class PendingFighter
        {
            public PendingFighter(JsonObjectReader item, JsonValue at, KitIds kit, Func<FighterKit, FighterDef> create)
            {
                Item = item;
                At = at;
                Kit = kit;
                Create = create;
            }

            public JsonObjectReader Item { get; }

            public JsonValue At { get; }

            public KitIds Kit { get; }

            public Func<FighterKit, FighterDef> Create { get; }
        }

        /// <summary>The kit members of a fighter or of the default kit, as ids (null = not named), with where each was.</summary>
        private sealed class KitIds
        {
            private readonly string?[] _ids = new string?[6];
            private readonly JsonValue?[] _at = new JsonValue?[6];
            private static readonly string[] Names = { "weapon", "shield", "bodyArmour", "headCover", "experience", "type" };
            private static readonly string[] Kinds = { "weapon", "shield", "body armour", "head cover", "experience level", "warrior type" };

            public bool Complete => Array.IndexOf(_ids, null) < 0;

            public static KitIds Read(JsonObjectReader item)
            {
                var kit = new KitIds();
                for (int i = 0; i < Names.Length; i++)
                {
                    kit._ids[i] = item.OptionalString(Names[i]);
                    kit._at[i] = item.Member(Names[i]);
                }

                return kit;
            }

            /// <summary>Looks the ids up in <paramref name="rules"/>, taking the rest from <paramref name="fallback"/>.</summary>
            public FighterKit Resolve(CombatDef rules, FighterKit? fallback, JsonObjectReader item)
            {
                if (fallback != null && Array.TrueForAll(_ids, id => id == null))
                {
                    return fallback; // names nothing: share the default kit itself
                }

                WeaponDef? weapon = Pick(0, rules.Weapons, w => w.Id, fallback?.Weapon, item);
                ProtectionDef? shield = Pick(1, rules.Shields, p => p.Id, fallback?.Shield, item);
                ProtectionDef? body = Pick(2, rules.BodyArmours, p => p.Id, fallback?.BodyArmour, item);
                ProtectionDef? head = Pick(3, rules.HeadCovers, p => p.Id, fallback?.HeadCover, item);
                ExperienceDef? experience = Pick(4, rules.ExperienceLevels, e => e.Id, fallback?.Experience, item);
                WarriorTypeDef? type = Pick(5, rules.WarriorTypes, t => t.Id, fallback?.Type, item);
                return new FighterKit(weapon!, shield!, body!, head!, experience!, type!);
            }

            private T? Pick<T>(int part, IReadOnlyList<T> catalogue, Func<T, string> idOf, T? fallback, JsonObjectReader item)
                where T : class
            {
                string? id = _ids[part];
                if (id == null)
                {
                    return fallback;
                }

                for (int i = 0; i < catalogue.Count; i++)
                {
                    if (string.Equals(idOf(catalogue[i]), id, StringComparison.Ordinal))
                    {
                        return catalogue[i];
                    }
                }

                throw item.Error(_at[part]!, item.MemberPath(Names[part]),
                    $"no {Kinds[part]} '{id}' (known: {JoinIds(catalogue, idOf)})");
            }
        }

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
