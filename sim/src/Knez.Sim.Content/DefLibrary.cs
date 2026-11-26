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
    /// optional <c>bodies</c> and <c>formations</c> arrays, optional <c>movement</c>, <c>combat</c> and <c>morale</c>
    /// objects (each in at most one file) and an optional <c>description</c>; ids are unique across all files. Lists are
    /// sorted by id (ordinal), so the order of files never matters. Kits and experience levels belong to the combat rules
    /// (M3 step 9, ADR-0005): a scenario names them per group of a squad's men.
    /// </summary>
    public sealed class DefLibrary
    {
        private const string FileMembers = "description, bodies, formations, movement, combat, morale, traits, ai";
        private const string AiMembers = "attackAt, holdBelow, leaveBelow, regroupMorale, readyMorale, regroupDistance, helpBonus, flankOffset, " +
            "maxOnTarget, jitter, cryEnemies, cryContact, closeRanksRange, standShare";
        private const string MoraleMembers = "shaken, wavering, routing, hysteresis, lossWeight, ownLightWound, ownHeavyWound, " +
            "mateHeavyWound, flankBlow, rearBlow, mateFlankBlow, mateRearBlow, mateRouts, allySquadBroke, enemySquadBroke, nearby, " +
            "recoveryPerSecond, safeDistance, rallyAt, breakShare, reformShare, waveringHitPenalty, leaderFell, routPace";
        private const string MovementMembers = "pivotTolerance, slotCatchUpTime, personalSpace, separationTime, holdingReach, " +
            "formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead, regroupMaxWait, regroupCost, reformPace, stepBackPace, withdrawClearance, closeInRange, turnLagTime, advanceRange, pursuitTime, pursuitRange";
        private const string BodyMembers = "id, radius, walkSpeed, runSpeed, turnRate";
        private const string CombatMembers = "weapons, shields, bodyArmour, headCover, experience, kits, hitChance, " +
            "hitLimits, position, strikeArc, wounded";
        private const string WeaponMembers = "id, reach, windup, recovery, wounds, penetration, defence";
        private const string KitMembers = "id, weapon, shield, bodyArmour, headCover";
        private const string FormationMembers = "id, spacing, rankSpacing, ranks, width, turnRate, holdWeight, allyPassSpeed, shieldWall, keepsRanks";

        private readonly Dictionary<string, BodyDef> _bodies;
        private readonly Dictionary<string, TraitDef> _traits;
        private readonly Dictionary<string, FormationDef> _formations;

        private DefLibrary(List<BodyDef> bodies, List<FormationDef> formations, MovementDef? movement, CombatDef? combat,
            MoraleDef? morale, List<TraitDef> traits, AiDef? ai)
        {
            Ai = ai;
            traits.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Traits = new ReadOnlyCollection<TraitDef>(traits);
            _traits = new Dictionary<string, TraitDef>(StringComparer.Ordinal);
            for (int i = 0; i < traits.Count; i++)
            {
                _traits.Add(traits[i].Id, traits[i]);
            }

            Movement = movement;
            Morale = morale;
            Combat = combat;
            bodies.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            formations.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Bodies = new ReadOnlyCollection<BodyDef>(bodies);
            Formations = new ReadOnlyCollection<FormationDef>(formations);
            _bodies = new Dictionary<string, BodyDef>(StringComparer.Ordinal);
            _formations = new Dictionary<string, FormationDef>(StringComparer.Ordinal);
            for (int i = 0; i < bodies.Count; i++)
            {
                _bodies.Add(bodies[i].Id, bodies[i]);
            }

            for (int i = 0; i < formations.Count; i++)
            {
                _formations.Add(formations[i].Id, formations[i]);
            }
        }

        /// <summary>Body defs sorted by id.</summary>
        public IReadOnlyList<BodyDef> Bodies { get; }

        /// <summary>Formation defs sorted by id.</summary>
        public IReadOnlyList<FormationDef> Formations { get; }

        /// <summary>Retainers' traits (<c>retainers.json</c>, M3 step 10), sorted by id.</summary>
        public IReadOnlyList<TraitDef> Traits { get; }

        /// <summary>The movement rules, or null when no def file has a <c>movement</c> object.</summary>
        public MovementDef? Movement { get; }

        /// <summary>The morale rules, or null when no def file has a <c>morale</c> object.</summary>
        public MoraleDef? Morale { get; }

        /// <summary>The enemy-AI rules (<c>ai.json</c>, M4 step 2), or null when no def file has an <c>ai</c> object.</summary>
        public AiDef? Ai { get; }

        /// <summary>The combat rules, or null when no def file has a <c>combat</c> object.</summary>
        public CombatDef? Combat { get; }

        /// <summary>The combat rules scenarios resolve kits and experience levels against: the file's, or
        /// <see cref="CombatDef.Standard"/>.</summary>
        public CombatDef Rules => Combat ?? CombatDef.Standard;

        /// <summary>Parses def files; throws <see cref="ContentException"/> on the first problem.</summary>
        public static DefLibrary Parse(IReadOnlyList<SourceText> files)
        {
            var bodies = new List<BodyDef>();
            var formations = new List<FormationDef>();
            var traits = new List<TraitDef>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal); // "kind:id" → where it was defined
            MovementDef? movement = null;
            CombatDef? combat = null;
            MoraleDef? morale = null;
            AiDef? ai = null;

            for (int f = 0; f < files.Count; f++)
            {
                SourceText file = files[f];
                JsonValue rootValue = JsonReader.Parse(file);
                var root = JsonObjectReader.Of(rootValue, string.Empty, file.Name);
                root.OptionalString("description");

                JsonValue? oldFighters = root.Member("fighters");
                if (oldFighters != null)
                {
                    throw root.Error(oldFighters, "fighters",
                        "fighter types are gone (M3 step 9, ADR-0005): bodies go in 'bodies', kits in combat 'kits', and a scenario's squads name experience and kit per group of men");
                }

                IReadOnlyList<JsonValue> bodyItems = root.OptionalArray("bodies");
                for (int i = 0; i < bodyItems.Count; i++)
                {
                    var item = JsonObjectReader.Of(bodyItems[i], $"bodies[{i}]", file.Name);
                    string id = item.RequireString("id");
                    var radius = item.RequireFix("radius");
                    var walkSpeed = item.RequireFix("walkSpeed");
                    var runSpeed = item.RequireFix("runSpeed");
                    var turnRate = item.RequireFix("turnRate");
                    item.RejectUnknownMembers(BodyMembers);
                    Claim(seen, "body", id, item, bodyItems[i], file.Name);
                    bodies.Add(Build(item, bodyItems[i], () => new BodyDef(id, radius, walkSpeed, runSpeed, turnRate)));
                }

                IReadOnlyList<JsonValue> traitItems = root.OptionalArray("traits");
                for (int i = 0; i < traitItems.Count; i++)
                {
                    var item = JsonObjectReader.Of(traitItems[i], $"traits[{i}]", file.Name);
                    string id = item.RequireString("id");
                    AbilityDef ability = ReadAbility(item.RequireObject("ability"), item.Member("ability")!);
                    item.RejectUnknownMembers("id, ability");
                    Claim(seen, "trait", id, item, traitItems[i], file.Name);
                    traits.Add(Build(item, traitItems[i], () => new TraitDef(id, ability)));
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
                    var turnLagTime = item.RequireFix("turnLagTime");
                    var advanceRange = item.RequireFix("advanceRange");
                    var pursuitTime = item.RequireFix("pursuitTime");
                    var pursuitRange = item.RequireFix("pursuitRange");
                    item.RejectUnknownMembers(MovementMembers);
                    movement = Build(item, movementValue, () => new MovementDef(pivotTolerance, slotCatchUpTime, personalSpace,
                        separationTime, holdingReach, formedUpReach, collisionPasses, allyPartingDepth, pathBudget, regroupLead,
                        regroupMaxWait, regroupCost, reformPace, stepBackPace, withdrawClearance,
                        closeInRange, turnLagTime, advanceRange, pursuitTime, pursuitRange));
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

                JsonObjectReader? aiReader = root.OptionalObject("ai");
                if (aiReader != null)
                {
                    JsonValue aiValue = rootValue.Get("ai")!;
                    Claim(seen, "ai", "rules", aiReader, aiValue, file.Name);
                    ai = ReadAi(aiReader, aiValue);
                }

                root.RejectUnknownMembers(FileMembers);
            }

            return new DefLibrary(bodies, formations, movement, combat, morale, traits, ai);
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
                int penetration = Permille(weapon, "penetration");
                int defence = Permille(weapon, "defence");
                weapon.RejectUnknownMembers(WeaponMembers);
                weapons.Add(Build(weapon, weaponItems[i], () => new WeaponDef(id, reach, windup, recovery, light, heavy, down, penetration, defence)));
            }

            List<ProtectionDef> shields = ReadPieces(item, "shields", "shield", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ProtectionDef> bodyArmours = ReadPieces(item, "bodyArmour", "body armour", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ProtectionDef> headCovers = ReadPieces(item, "headCover", "head cover", "protection", (id, v) => new ProtectionDef(id, v), d => d.Id);
            List<ExperienceDef> levels = ReadExperience(item);
            List<KitDef> kits = ReadKits(item, weapons, shields, bodyArmours, headCovers);

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

            return Build(item, at, () => new CombatDef(weapons, shields, bodyArmours, headCovers, levels, kits, rows, minHit, maxHit,
                flank, rear, shieldFromFlank, shieldFromRear, shieldWall, lightPenalty, heavyPenalty, lightPace, heavyPace,
                frontArc, rearArc, strikeArc));
        }

        private static AiDef ReadAi(JsonObjectReader a, JsonValue at)
        {
            int attackAt = a.RequireInt("attackAt");
            int holdBelow = a.RequireInt("holdBelow");
            int leaveBelow = a.RequireInt("leaveBelow");
            int regroupMorale = a.RequireInt("regroupMorale");
            int readyMorale = a.RequireInt("readyMorale");
            Fix regroupDistance = a.RequireFix("regroupDistance");
            Fix helpBonus = a.RequireFix("helpBonus");
            Fix flankOffset = a.RequireFix("flankOffset");
            int maxOnTarget = a.RequireInt("maxOnTarget");
            Fix jitter = a.RequireFix("jitter");
            int cryEnemies = a.RequireInt("cryEnemies");
            Fix cryContact = a.RequireFix("cryContact");
            Fix closeRanksRange = a.RequireFix("closeRanksRange");
            int standShare = a.RequireInt("standShare");
            a.RejectUnknownMembers(AiMembers);
            return Build(a, at, () => new AiDef(attackAt, holdBelow, leaveBelow, regroupMorale, readyMorale, regroupDistance, helpBonus,
                flankOffset, maxOnTarget, jitter, cryEnemies, cryContact, closeRanksRange, standShare));
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
            Fix leaderFell = m.RequireFix("leaderFell");
            Fix routPace = m.RequireFix("routPace");
            m.RejectUnknownMembers(MoraleMembers);
            return Build(m, at, () => new MoraleDef(shaken, wavering, routing, hysteresis, lossWeight, ownLight, ownHeavy, mateHeavy,
                flank, rear, mateFlank, mateRear, mateRouts, allyBroke, enemyBroke, nearby, recovery, safe, rallyAt, breakShare,
                reformShare, waveringPenalty, leaderFell, routPace));
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
                int leadership = Permille(level, "leadership");
                bool retainerOnly = level.RequireBool("retainerOnly");
                level.RejectUnknownMembers("id, defence, morale, leadership, retainerOnly");
                list.Add(Build(level, items[i], () => new ExperienceDef(id, defence, morale, leadership, retainerOnly)));
            }

            return list;
        }

        /// <summary>The named kits (M3 step 9): each part by id from the lists just read, located errors otherwise.</summary>
        private static List<KitDef> ReadKits(JsonObjectReader combat, List<WeaponDef> weapons, List<ProtectionDef> shields,
            List<ProtectionDef> bodyArmours, List<ProtectionDef> headCovers)
        {
            var list = new List<KitDef>();
            IReadOnlyList<JsonValue> items = NonEmpty(combat, "kits", "kit");
            for (int i = 0; i < items.Count; i++)
            {
                var kit = JsonObjectReader.Of(items[i], combat.MemberPath($"kits[{i}]"), combat.Source);
                string id = kit.RequireString("id");
                UniqueId(list, id, "kit", kit, d => d.Id);
                WeaponDef weapon = Pick(kit, "weapon", "weapon", weapons, w => w.Id);
                ProtectionDef shield = Pick(kit, "shield", "shield", shields, p => p.Id);
                ProtectionDef body = Pick(kit, "bodyArmour", "body armour", bodyArmours, p => p.Id);
                ProtectionDef head = Pick(kit, "headCover", "head cover", headCovers, p => p.Id);
                kit.RejectUnknownMembers(KitMembers);
                list.Add(Build(kit, items[i], () => new KitDef(id, weapon, shield, body, head)));
            }

            return list;
        }

        /// <summary>The entry of <paramref name="catalogue"/> a required member names, or a located "no such" error.</summary>
        private static T Pick<T>(JsonObjectReader item, string member, string kind, IReadOnlyList<T> catalogue, Func<T, string> idOf)
        {
            string id = item.RequireString(member);
            for (int i = 0; i < catalogue.Count; i++)
            {
                if (string.Equals(idOf(catalogue[i]), id, StringComparison.Ordinal))
                {
                    return catalogue[i];
                }
            }

            throw item.Error(item.Member(member)!, item.MemberPath(member), $"no {kind} '{id}' (known: {JoinIds(catalogue, idOf)})");
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

        /// <summary>The body def with <paramref name="id"/>, or null.</summary>
        public BodyDef? FindBody(string id) => _bodies.TryGetValue(id, out BodyDef? def) ? def : null;

        /// <summary>The trait with <paramref name="id"/>, or null.</summary>
        public TraitDef? FindTrait(string id) => _traits.TryGetValue(id, out TraitDef? def) ? def : null;

        /// <summary>Known trait ids, for "unknown trait" messages.</summary>
        public string TraitIds() => JoinIds(Traits, d => d.Id);

        /// <summary>The formation def with <paramref name="id"/>, or null.</summary>
        public FormationDef? FindFormation(string id) => _formations.TryGetValue(id, out FormationDef? def) ? def : null;

        /// <summary>Known body ids, for "unknown body" messages.</summary>
        public string BodyIds() => JoinIds(Bodies, d => d.Id);

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

        /// <summary>A trait's ability (M3 step 11): <c>id</c> and <c>cooldown</c> (s), then the numbers its kind needs —
        /// battle cry <c>radius</c>, <c>ownMorale</c>, <c>enemyMorale</c>; close ranks <c>duration</c>, <c>frontHit</c>,
        /// <c>pace</c>; stand to the death <c>duration</c>; the abilities not built yet nothing more.</summary>
        private static AbilityDef ReadAbility(JsonObjectReader a, JsonValue at)
        {
            string id = a.RequireString("id");
            AbilityKind? kind = AbilityDef.KindOf(id);
            if (kind == null)
            {
                throw a.Error(a.Member("id")!, a.MemberPath("id"), $"unknown ability '{id}' (known: {AbilityDef.Ids})");
            }

            Fix cooldown = a.RequireFix("cooldown");
            switch (kind.Value)
            {
                case AbilityKind.BattleCry:
                {
                    Fix radius = a.RequireFix("radius");
                    int own = a.RequireInt("ownMorale");
                    int enemy = a.RequireInt("enemyMorale");
                    a.RejectUnknownMembers("id, cooldown, radius, ownMorale, enemyMorale");
                    return Build(a, at, () => AbilityDef.BattleCry(cooldown, radius, own, enemy));
                }

                case AbilityKind.CloseRanks:
                {
                    Fix duration = a.RequireFix("duration");
                    int frontHit = a.RequireInt("frontHit");
                    Fix pace = a.RequireFix("pace");
                    a.RejectUnknownMembers("id, cooldown, duration, frontHit, pace");
                    return Build(a, at, () => AbilityDef.CloseRanks(cooldown, duration, frontHit, pace));
                }

                case AbilityKind.StandToTheDeath:
                {
                    Fix duration = a.RequireFix("duration");
                    a.RejectUnknownMembers("id, cooldown, duration");
                    return Build(a, at, () => AbilityDef.StandToTheDeath(cooldown, duration));
                }

                default:
                    a.RejectUnknownMembers("id, cooldown"); // not built yet: its numbers come when it is (M3 step 11)
                    return Build(a, at, () => AbilityDef.NotBuilt(kind.Value, cooldown));
            }
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
