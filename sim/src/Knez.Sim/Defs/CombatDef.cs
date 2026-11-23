using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Knez.Sim.Numerics;

namespace Knez.Sim.Defs
{
    /// <summary>
    /// Immutable combat rules from content (<c>content/defs/combat.json</c>, battle-sim.md §11): the catalogues of
    /// weapons, protection, experience levels and warrior types, the hit-chance matrix (weapon × attacker's experience),
    /// the default kit, and the modifiers for position and wounds. No hit points: every blow that gets through leaves a
    /// wound. Shared by the whole battle and fingerprinted. Catalogues keep the file's order (experience is a ladder).
    /// </summary>
    public sealed class CombatDef
    {
        private readonly ReadOnlyCollection<int> _hitChance; // row-major: weapon index × experience level index

        /// <summary>
        /// Creates the rules and checks them as a whole: non-empty catalogues with unique ids, one matrix row per weapon
        /// and one value per experience level in each, a default kit drawn from these catalogues, sane limits. Throws
        /// <see cref="ArgumentException"/>.
        /// </summary>
        public CombatDef(IReadOnlyList<WeaponDef> weapons, IReadOnlyList<ProtectionDef> shields,
            IReadOnlyList<ProtectionDef> bodyArmours, IReadOnlyList<ProtectionDef> headCovers,
            IReadOnlyList<ExperienceDef> experienceLevels, IReadOnlyList<WarriorTypeDef> warriorTypes,
            IReadOnlyList<IReadOnlyList<int>> hitChance, FighterKit defaultKit, int minHit, int maxHit, int flankBonus,
            int rearBonus, int shieldFromFlank, int shieldFromRear, int shieldWallBonus, int lightWoundPenalty,
            int heavyWoundPenalty, Fix lightWoundPace, Fix heavyWoundPace, Fix frontArc, Fix rearArc, Fix strikeArc)
        {
            Weapons = Catalogue(weapons, "weapon");
            Shields = Catalogue(shields, "shield");
            BodyArmours = Catalogue(bodyArmours, "body armour");
            HeadCovers = Catalogue(headCovers, "head cover");
            ExperienceLevels = Catalogue(experienceLevels, "experience level");
            WarriorTypes = Catalogue(warriorTypes, "warrior type");
            _hitChance = Matrix(hitChance ?? throw new ArgumentNullException(nameof(hitChance)), Weapons, ExperienceLevels);

            DefaultKit = defaultKit ?? throw new ArgumentNullException(nameof(defaultKit));
            string? stranger = Stranger(defaultKit);
            if (stranger != null)
            {
                throw new ArgumentException($"defaultKit: {stranger} is not one of these rules' defs.");
            }

            MaxHit = DefChecks.Permille(maxHit, "hitLimits.max");
            if (minHit < 0 || minHit > maxHit)
            {
                throw new ArgumentException($"hitLimits.min must be 0..hitLimits.max ({maxHit}) (got {minHit}).");
            }

            MinHit = minHit;
            FlankBonus = DefChecks.Permille(flankBonus, "position.flank");
            RearBonus = DefChecks.Permille(rearBonus, "position.rear");
            ShieldFromFlank = DefChecks.Permille(shieldFromFlank, "position.shieldFromFlank");
            ShieldFromRear = DefChecks.Permille(shieldFromRear, "position.shieldFromRear");
            ShieldWallBonus = DefChecks.Permille(shieldWallBonus, "position.shieldWall");
            LightWoundPenalty = DefChecks.Permille(lightWoundPenalty, "wounded.lightPenalty");
            HeavyWoundPenalty = DefChecks.Permille(heavyWoundPenalty, "wounded.heavyPenalty");
            LightWoundPace = DefChecks.Fraction(lightWoundPace, "wounded.lightPace");
            HeavyWoundPace = DefChecks.Fraction(heavyWoundPace, "wounded.heavyPace");
            FrontArc = DefChecks.Positive(frontArc, 180, "°", "position.frontArc");
            RearArc = DefChecks.Positive(rearArc, 180, "°", "position.rearArc");
            if (rearArc < frontArc)
            {
                throw new ArgumentException($"position.rearArc must be at least position.frontArc (got {rearArc} < {frontArc}).");
            }

            StrikeArc = DefChecks.Positive(strikeArc, 180, "°", "strikeArc");
            Fingerprint = ComputeFingerprint();
        }

        /// <summary>
        /// The values in <c>content/defs/combat.json</c> when this was written (2026-11-22), rounded exactly as the
        /// content reader rounds decimals (a test keeps the fingerprints equal). Content always brings its own file; these
        /// serve tests and scenarios built in code, and fighter defs built in code take its <see cref="DefaultKit"/>.
        /// </summary>
        public static readonly CombatDef Standard = CreateStandard();

        /// <summary>Weapon types, in file order.</summary>
        public IReadOnlyList<WeaponDef> Weapons { get; }

        /// <summary>Shields, in file order ("none" is an entry like any other).</summary>
        public IReadOnlyList<ProtectionDef> Shields { get; }

        /// <summary>Body armours, in file order.</summary>
        public IReadOnlyList<ProtectionDef> BodyArmours { get; }

        /// <summary>Head covers, in file order.</summary>
        public IReadOnlyList<ProtectionDef> HeadCovers { get; }

        /// <summary>Experience levels, lowest first (the file's order).</summary>
        public IReadOnlyList<ExperienceDef> ExperienceLevels { get; }

        /// <summary>Warrior types, in file order.</summary>
        public IReadOnlyList<WarriorTypeDef> WarriorTypes { get; }

        /// <summary>The kit of a fighter type that names none (or only part) of his own.</summary>
        public FighterKit DefaultKit { get; }

        /// <summary>The chance (‰) a blow gets through never drops below this…</summary>
        public int MinHit { get; }

        /// <summary>…nor rises above this.</summary>
        public int MaxHit { get; }

        /// <summary>Permille added to the chance when the attacker strikes from the defender's flank.</summary>
        public int FlankBonus { get; }

        /// <summary>Permille added to the chance when the attacker strikes from the defender's rear.</summary>
        public int RearBonus { get; }

        /// <summary>The share (‰) of a shield's protection that still counts against a blow from the flank.</summary>
        public int ShieldFromFlank { get; }

        /// <summary>The share (‰) of a shield's protection that still counts against a blow from the rear.</summary>
        public int ShieldFromRear { get; }

        /// <summary>Permille more taken off a frontal blow at a man standing in a shield wall.</summary>
        public int ShieldWallBonus { get; }

        /// <summary>Permille taken off the chance of a lightly wounded man's own blows.</summary>
        public int LightWoundPenalty { get; }

        /// <summary>Permille taken off the chance of a heavily wounded man's own blows.</summary>
        public int HeavyWoundPenalty { get; }

        /// <summary>A lightly wounded man moves at this fraction of his pace.</summary>
        public Fix LightWoundPace { get; }

        /// <summary>A heavily wounded man moves at this fraction of his pace.</summary>
        public Fix HeavyWoundPace { get; }

        /// <summary>A blow comes from the defender's front when the attacker stands within this many degrees of where the
        /// defender faces (decided 2026-11-23: 60).</summary>
        public Fix FrontArc { get; }

        /// <summary>Beyond <see cref="FrontArc"/> and up to this many degrees it comes from his flank; beyond, from his
        /// rear (decided 2026-11-23: 120).</summary>
        public Fix RearArc { get; }

        /// <summary>A man strikes an enemy only within this many degrees of where he faces; engaged men turn toward their
        /// enemy (decided 2026-11-23: 60, the same as the front).</summary>
        public Fix StrikeArc { get; }

        /// <summary><see cref="FrontArc"/> in <see cref="Angle"/> units.</summary>
        public int FrontArcUnits => ToUnits(FrontArc);

        /// <summary><see cref="RearArc"/> in <see cref="Angle"/> units.</summary>
        public int RearArcUnits => ToUnits(RearArc);

        /// <summary><see cref="StrikeArc"/> in <see cref="Angle"/> units.</summary>
        public int StrikeArcUnits => ToUnits(StrikeArc);

        /// <summary>FNV-1a hash of every id and number above, in order; part of the scenario's fingerprint.</summary>
        public ulong Fingerprint { get; }

        /// <summary>The matrix: base chance (‰) that a blow with <paramref name="weapon"/> by a man of
        /// <paramref name="attacker"/> experience gets through, before the defender's protection and position.</summary>
        public int BaseHit(WeaponDef weapon, ExperienceDef attacker)
        {
            int w = IndexOf(Weapons, weapon);
            int e = IndexOf(ExperienceLevels, attacker);
            if (w < 0 || e < 0)
            {
                // A def from another catalogue would otherwise read a neighbouring cell without a word.
                throw new ArgumentException(
                    $"weapon '{weapon.Id}' or experience level '{attacker.Id}' is not one of these rules' defs.");
            }

            return _hitChance[(w * ExperienceLevels.Count) + e];
        }

        /// <summary>True when every part of <paramref name="kit"/> is an entry of these rules (the very object).</summary>
        public bool Contains(FighterKit kit) => Stranger(kit) == null;

        /// <summary>The weapon with <paramref name="id"/>, or null.</summary>
        public WeaponDef? FindWeapon(string id) => Find(Weapons, id);

        /// <summary>The shield with <paramref name="id"/>, or null.</summary>
        public ProtectionDef? FindShield(string id) => Find(Shields, id);

        /// <summary>The body armour with <paramref name="id"/>, or null.</summary>
        public ProtectionDef? FindBodyArmour(string id) => Find(BodyArmours, id);

        /// <summary>The head cover with <paramref name="id"/>, or null.</summary>
        public ProtectionDef? FindHeadCover(string id) => Find(HeadCovers, id);

        /// <summary>The experience level with <paramref name="id"/>, or null.</summary>
        public ExperienceDef? FindExperience(string id) => Find(ExperienceLevels, id);

        /// <summary>The warrior type with <paramref name="id"/>, or null.</summary>
        public WarriorTypeDef? FindWarriorType(string id) => Find(WarriorTypes, id);

        /// <summary>Names the first part of <paramref name="kit"/> that isn't one of these rules' entries, or null.</summary>
        internal string? Stranger(FighterKit kit)
        {
            if (IndexOf(Weapons, kit.Weapon) < 0)
            {
                return $"weapon '{kit.Weapon.Id}'";
            }

            if (IndexOf(Shields, kit.Shield) < 0)
            {
                return $"shield '{kit.Shield.Id}'";
            }

            if (IndexOf(BodyArmours, kit.BodyArmour) < 0)
            {
                return $"body armour '{kit.BodyArmour.Id}'";
            }

            if (IndexOf(HeadCovers, kit.HeadCover) < 0)
            {
                return $"head cover '{kit.HeadCover.Id}'";
            }

            if (IndexOf(ExperienceLevels, kit.Experience) < 0)
            {
                return $"experience level '{kit.Experience.Id}'";
            }

            return IndexOf(WarriorTypes, kit.Type) < 0 ? $"warrior type '{kit.Type.Id}'" : null;
        }

        internal void AddTo(ref Fnv1a64 hash) => hash.Add(Fingerprint);

        /// <summary>Index of the very object <paramref name="item"/> in <paramref name="list"/>, or -1. Catalogues are a
        /// handful of entries, so a scan is cheapest.</summary>
        private static int IndexOf<T>(IReadOnlyList<T> list, T item)
            where T : class
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        private static T? Find<T>(IReadOnlyList<T> list, string id)
            where T : class, IDefId
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }

        private static ReadOnlyCollection<T> Catalogue<T>(IReadOnlyList<T> given, string kind)
            where T : class, IDefId
        {
            if (given == null || given.Count == 0)
            {
                throw new ArgumentException($"the rules need at least one {kind}.");
            }

            var copy = new T[given.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = given[i] ?? throw new ArgumentException($"{kind} {i} is missing.");
                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(copy[j].Id, copy[i].Id, StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"{kind} '{copy[i].Id}' is listed twice.");
                    }
                }
            }

            return new ReadOnlyCollection<T>(copy);
        }

        private static ReadOnlyCollection<int> Matrix(IReadOnlyList<IReadOnlyList<int>> rows, IReadOnlyList<WeaponDef> weapons,
            IReadOnlyList<ExperienceDef> levels)
        {
            if (rows.Count != weapons.Count)
            {
                throw new ArgumentException($"hitChance has {rows.Count} rows; it needs one per weapon ({weapons.Count}).");
            }

            var cells = new int[weapons.Count * levels.Count];
            for (int w = 0; w < weapons.Count; w++)
            {
                IReadOnlyList<int> row = rows[w] ?? throw new ArgumentException($"hitChance '{weapons[w].Id}' is missing.");
                if (row.Count != levels.Count)
                {
                    throw new ArgumentException(
                        $"hitChance '{weapons[w].Id}' has {row.Count} values; it needs one per experience level ({levels.Count}).");
                }

                for (int e = 0; e < levels.Count; e++)
                {
                    cells[(w * levels.Count) + e] = DefChecks.Permille(row[e], $"hitChance '{weapons[w].Id}' '{levels[e].Id}'");
                }
            }

            return new ReadOnlyCollection<int>(cells);
        }

        private ulong ComputeFingerprint()
        {
            var hash = Fnv1a64.Start();
            hash.Add(Weapons.Count);
            for (int i = 0; i < Weapons.Count; i++)
            {
                Weapons[i].AddTo(ref hash);
            }

            AddAll(ref hash, Shields);
            AddAll(ref hash, BodyArmours);
            AddAll(ref hash, HeadCovers);
            hash.Add(ExperienceLevels.Count);
            for (int i = 0; i < ExperienceLevels.Count; i++)
            {
                ExperienceLevels[i].AddTo(ref hash);
            }

            hash.Add(WarriorTypes.Count);
            for (int i = 0; i < WarriorTypes.Count; i++)
            {
                WarriorTypes[i].AddTo(ref hash);
            }

            for (int i = 0; i < _hitChance.Count; i++)
            {
                hash.Add(_hitChance[i]);
            }

            DefaultKit.AddTo(ref hash);
            hash.Add(MinHit);
            hash.Add(MaxHit);
            hash.Add(FlankBonus);
            hash.Add(RearBonus);
            hash.Add(ShieldFromFlank);
            hash.Add(ShieldFromRear);
            hash.Add(ShieldWallBonus);
            hash.Add(LightWoundPenalty);
            hash.Add(HeavyWoundPenalty);
            hash.Add(LightWoundPace.Raw);
            hash.Add(HeavyWoundPace.Raw);
            hash.Add(FrontArc.Raw);
            hash.Add(RearArc.Raw);
            hash.Add(StrikeArc.Raw);
            return hash.Value;
        }

        private static void AddAll(ref Fnv1a64 hash, IReadOnlyList<ProtectionDef> pieces)
        {
            hash.Add(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                pieces[i].AddTo(ref hash);
            }
        }

        /// <summary>Degrees → <see cref="Angle"/> units, rounded like <see cref="Angle.FromDegrees(int)"/> (Raw·65536/360/65536
        /// = Raw/360), so "within 60°" includes an angle made from exactly 60°.</summary>
        private static int ToUnits(Fix degrees) => (int)((degrees.Raw + 180) / 360);

        private static Fix Seconds(int tenths) => DefChecks.Decimal(tenths, 10);

        private static CombatDef CreateStandard()
        {
            var weapons = new[]
            {
                new WeaponDef("sword", Fix.Ratio(1, 2), Seconds(7), Seconds(8), 650, 280, 70),
                new WeaponDef("axe", Fix.Ratio(1, 2), Seconds(9), Seconds(9), 500, 330, 170),
                new WeaponDef("spear", DefChecks.Decimal(18, 10), Seconds(8), Seconds(8), 600, 300, 100),
                new WeaponDef("seax", DefChecks.Decimal(3, 10), Seconds(5), Seconds(6), 720, 240, 40),
            };
            var shields = new[] { new ProtectionDef("none", 0), new ProtectionDef("round", 220) };
            var body = new[] { new ProtectionDef("none", 0), new ProtectionDef("padded", 70), new ProtectionDef("mail", 140) };
            var head = new[] { new ProtectionDef("none", 0), new ProtectionDef("cap", 30), new ProtectionDef("helm", 60) };
            var levels = new[]
            {
                new ExperienceDef("new", 0, 600), new ExperienceDef("experienced", 40, 750),
                new ExperienceDef("professional", 80, 850), new ExperienceDef("elite", 120, 950),
            };
            var types = new[] { new WarriorTypeDef("default", 0), new WarriorTypeDef("spearman", 30), new WarriorTypeDef("mounted", 50) };
            var hitChance = new IReadOnlyList<int>[]
            {
                new[] { 420, 500, 580, 660 },
                new[] { 400, 480, 560, 640 },
                new[] { 380, 460, 540, 620 },
                new[] { 340, 420, 500, 580 },
            };
            var kit = new FighterKit(weapons[0], shields[1], body[1], head[1], levels[1], types[0]);
            return new CombatDef(weapons, shields, body, head, levels, types, hitChance, kit, minHit: 20, maxHit: 950,
                flankBonus: 100, rearBonus: 200, shieldFromFlank: 500, shieldFromRear: 0, shieldWallBonus: 80,
                lightWoundPenalty: 40, heavyWoundPenalty: 120, lightWoundPace: DefChecks.Decimal(9, 10),
                heavyWoundPace: DefChecks.Decimal(7, 10), frontArc: Fix.FromInt(60), rearArc: Fix.FromInt(120),
                strikeArc: Fix.FromInt(60));
        }
    }
}
