#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Knez.Game.View;
using Knez.Sim;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Morale;
using Knez.Sim.Units;
using NUnit.Framework;

namespace Knez.Game.Tests
{
    /// <summary>
    /// The art pipeline's mapping (docs/art-pipeline.md): which token a man wears, how his state draws it, which map
    /// sprites stand for a wall — against the real manifest and sprites in <c>Assets/Knez/Art/Resources</c>. Plain C#:
    /// no Unity objects, so these also run under .NET.
    /// </summary>
    public class ArtTests
    {
        private static readonly string[] ArtSides = { "clan", "ruler", "bandits" };

        private static string ArtFolder() =>
            Path.GetFullPath(Path.Combine(ContentFiles.DefaultRoot(), "..", "game", "Assets", "Knez", "Art", "Resources"));

        private static ArtCatalog RepoArt() =>
            ArtCatalog.Parse(File.ReadAllText(Path.Combine(ArtFolder(), "art-manifest.json")),
                File.ReadAllText(Path.Combine(ArtFolder(), "palette.json")));

        private static bool Exists(string resourcesPath) => File.Exists(Path.Combine(ArtFolder(), resourcesPath + ".png"));

        private static ScenarioDef Villages() => ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), "scenarios/sandbox/villages.json");

        private static KitDef Kit(ScenarioDef scenario, string id) => scenario.Combat.Kits.First(k => k.Id == id);

        [Test]
        public void Parse_RepoManifest_ReadsTokensMapAndPalette()
        {
            ArtCatalog art = RepoArt();
            Assert.That(art.Fighters.Count, Is.GreaterThan(0));
            Assert.That(art.Heroes.Any(h => h.Named), Is.True);
            Assert.That(art.FindMap("house"), Is.Not.Null);
            Assert.That(art.FindMap("house")!.WallCount, Is.EqualTo(1));
            Assert.That(art.MapColours.ContainsKey("grass"), Is.True);
            foreach (TokenArt token in art.Fighters.Concat(art.Heroes))
            {
                Assert.That(Exists(token.Path), Is.True, "sprite file for " + token.Path);
            }

            foreach (MapObjectArt item in art.Map)
            {
                Assert.That(Exists(item.Sprite), Is.True, "sprite file for " + item.Sprite);
            }
        }

        [Test]
        public void Parse_OtherVersion_Throws()
        {
            Assert.Throws<FormatException>(() => ArtCatalog.Parse("{\"version\": 2, \"fighters\": [], \"heroes\": [], \"map\": []}"));
        }

        [Test]
        public void Token_EveryContentKitOnEverySide_HasADrawnToken()
        {
            ArtCatalog art = RepoArt();
            ScenarioDef scenario = Villages();
            foreach (string side in ArtSides)
            {
                foreach (KitDef kit in scenario.Combat.Kits)
                {
                    foreach (ExperienceDef level in scenario.Combat.ExperienceLevels)
                    {
                        string? man = FighterArt.Token(art, side, kit, level.Id, null, 7, scenario.Combat.Kits);
                        string? retainer = FighterArt.Token(art, side, kit, level.Id, "Nobody", 7, scenario.Combat.Kits);
                        Assert.That(man, Is.Not.Null, $"{side} {kit.Id} {level.Id}");
                        Assert.That(Exists(man!), Is.True, man);
                        Assert.That(retainer, Is.Not.Null, $"{side} {kit.Id} {level.Id} Retainer");
                        Assert.That(Exists(retainer!), Is.True, retainer);
                    }
                }
            }
        }

        [Test]
        public void Token_AMan_WearsHisSideAndKit()
        {
            ScenarioDef scenario = Villages();
            string? token = FighterArt.Token(RepoArt(), "ruler", Kit(scenario, "spear-shield-padded"), "experienced", null, 0, scenario.Combat.Kits);
            Assert.That(token, Is.EqualTo("Tokens/tok_ruler_spear-shield-padded_0"));
        }

        [Test]
        public void Token_ASquadsMen_CycleThroughTheVariantsById()
        {
            ArtCatalog art = RepoArt();
            ScenarioDef scenario = Villages();
            var tokens = new HashSet<string>();
            for (int id = 20; id < 24; id++)
            {
                tokens.Add(FighterArt.Token(art, "clan", Kit(scenario, "axe-shield"), "new", null, id, scenario.Combat.Kits)!);
            }

            Assert.That(tokens.Count, Is.EqualTo(4));
            Assert.That(FighterArt.Token(art, "clan", Kit(scenario, "axe-shield"), "new", null, 24, scenario.Combat.Kits),
                Is.EqualTo(FighterArt.Token(art, "clan", Kit(scenario, "axe-shield"), "new", null, 20, scenario.Combat.Kits)));
        }

        [Test]
        public void Token_ANamedRetainer_WearsHisOwnLook_OnlyOnHisSideInHisKit()
        {
            ArtCatalog art = RepoArt();
            ScenarioDef scenario = Villages();
            KitDef mail = Kit(scenario, "sword-shield-mail");
            Assert.That(FighterArt.Token(art, "clan", mail, "elite", "Hrafn", 3, scenario.Combat.Kits), Is.EqualTo("Tokens/tok_hero_hrafn"));
            Assert.That(FighterArt.Token(art, "ruler", mail, "elite", "Hrafn", 3, scenario.Combat.Kits), Does.StartWith("Tokens/tok_hero_ruler_"));
            Assert.That(FighterArt.Token(art, "clan", Kit(scenario, "axe-shield-mail"), "elite", "Hrafn", 3, scenario.Combat.Kits),
                Does.StartWith("Tokens/tok_hero_clan_axe-shield-mail_"));
        }

        [Test]
        public void Token_AGenericRetainer_MatchesHisExperience()
        {
            ArtCatalog art = RepoArt();
            ScenarioDef scenario = Villages();
            KitDef mail = Kit(scenario, "sword-shield-mail");
            foreach (string level in new[] { "professional", "elite" })
            {
                for (int id = 0; id < 4; id++)
                {
                    string token = FighterArt.Token(art, "clan", mail, level, "Gaut", id, scenario.Combat.Kits)!;
                    TokenArt hero = art.Heroes.Single(h => h.Path == token);
                    Assert.That(hero.Named, Is.False, token);
                    Assert.That(hero.Experience, Is.EqualTo(level), token);
                    Assert.That(hero.Kit, Is.EqualTo("sword-shield-mail"), token);
                }
            }
        }

        [Test]
        public void Token_ARetainerOfASideWithoutRetainerArt_WearsAFightersToken()
        {
            ScenarioDef scenario = Villages();
            string? token = FighterArt.Token(RepoArt(), "bandits", Kit(scenario, "sword-shield-mail"), "elite", "Ulfr", 0, scenario.Combat.Kits);
            Assert.That(token, Does.StartWith("Tokens/tok_bandits_sword-shield-mail_"));
        }

        [Test]
        public void Token_AKitTheArtLacks_TakesTheNearestDrawnKit_WeaponFirst()
        {
            ScenarioDef scenario = Villages();
            CombatDef combat = scenario.Combat;
            var spearInMail = new KitDef("spear-shield-mail", combat.Weapons.First(w => w.Id == "spear"), combat.Shields.First(s => s.Id == "round"),
                combat.BodyArmours.First(a => a.Id == "mail"), combat.HeadCovers.First(c => c.Id == "helm"));
            string? token = FighterArt.Token(RepoArt(), "clan", spearInMail, "experienced", null, 0, combat.Kits);
            Assert.That(token, Does.StartWith("Tokens/tok_clan_spear-shield"));
        }

        [Test]
        public void Token_ASideTheArtLacks_IsNull()
        {
            ScenarioDef scenario = Villages();
            Assert.That(FighterArt.Token(RepoArt(), "sorcerer", Kit(scenario, "axe-shield"), "new", null, 0, scenario.Combat.Kits), Is.Null);
            Assert.That(FighterArt.Token(ArtCatalog.Empty, "clan", Kit(scenario, "axe-shield"), "new", "Hrafn", 0, scenario.Combat.Kits), Is.Null);
        }

        [Test]
        public void Side_ByIdWhenSet_ElseClanAndRuler()
        {
            Assert.That(FighterArt.Side(null, 0), Is.EqualTo("clan"));
            Assert.That(FighterArt.Side(null, 1), Is.EqualTo("ruler"));
            Assert.That(FighterArt.Side(new[] { "clan", " Bandits " }, 1), Is.EqualTo("bandits"));
            Assert.That(FighterArt.Side(new[] { "clan", "" }, 1), Is.EqualTo("ruler"));
            Assert.That(FighterArt.Side(new[] { "clan" }, 2), Is.EqualTo("ruler"));
        }

        [TestCase(true, WoundLevel.None, MoraleState.Steady, TokenState.Fighting)]
        [TestCase(true, WoundLevel.Heavy, MoraleState.Wavering, TokenState.Fighting)]
        [TestCase(true, WoundLevel.Light, MoraleState.Rallying, TokenState.Fighting)]
        [TestCase(true, WoundLevel.Light, MoraleState.Routing, TokenState.Routing)]
        [TestCase(false, WoundLevel.Down, MoraleState.Steady, TokenState.Fallen)]
        [TestCase(false, WoundLevel.Down, MoraleState.Routing, TokenState.Fallen)]
        [TestCase(false, WoundLevel.None, MoraleState.Fled, TokenState.Gone)]
        [TestCase(false, WoundLevel.Light, MoraleState.Left, TokenState.Gone)]
        public void State_FromWoundAndMorale(bool alive, WoundLevel wound, MoraleState morale, TokenState expected)
        {
            Assert.That(FighterArt.State(alive, wound, morale), Is.EqualTo(expected));
        }

        [Test]
        public void Token_EveryManOfTheVillagesSandbox_WearsADrawnToken()
        {
            ArtCatalog art = RepoArt();
            ScenarioDef scenario = Villages();
            var sim = new BattleSimulation(BattleSetup.Create(scenario, 1));
            foreach (Fighter fighter in sim.State.Fighters)
            {
                string? token = FighterArt.Token(art, fighter, FighterArt.Side(null, fighter.SideId), scenario.Combat.Kits);
                Assert.That(token, Is.Not.Null, fighter.ToString());
                Assert.That(Exists(token!), Is.True, token);
                TokenArt drawn = art.Fighters.Concat(art.Heroes).Single(t => t.Path == token);
                Assert.That(drawn.Side, Is.EqualTo(fighter.SideId == 0 ? "clan" : "ruler"), token);
                Assert.That(drawn.Kit, Is.EqualTo(fighter.Kit.Id), token);
                Assert.That(art.Heroes.Contains(drawn), Is.EqualTo(fighter.IsRetainer), token);
            }
        }

        [Test]
        public void Place_EveryWallOfTheVillagesSandbox_IsDrawn_HousesAsBuildingsRocksAsBoulders()
        {
            ArtCatalog art = RepoArt();
            var placed = new List<WallSprite>();
            foreach (WallDef wall in Villages().Map.Walls)
            {
                placed.Clear();
                float x0 = Metres(wall.Min.X), y0 = Metres(wall.Min.Y), x1 = Metres(wall.Max.X), y1 = Metres(wall.Max.Y);
                Assert.That(WallArt.Place(art, wall.Kind, x0, y0, x1, y1, placed), Is.True, wall.ToString());
                Assert.That(placed.Count, Is.EqualTo(1), wall.ToString());
                MapObjectArt item = art.FindMap(placed[0].ObjectId)!;
                Assert.That(item.Sim, Is.EqualTo(wall.Kind == WallKind.House ? "house" : "obstacle"), wall.ToString());
                if (wall.Kind == WallKind.Obstacle)
                {
                    Assert.That(item.Id, Is.EqualTo("boulders"));
                }

                float min = wall.Kind == WallKind.House ? WallArt.MinHouseScale : WallArt.MinRockScale;
                float max = wall.Kind == WallKind.House ? WallArt.MaxHouseScale : WallArt.MaxRockScale;
                Assert.That(placed[0].Scale, Is.InRange(min - 1e-3f, max + 1e-3f), placed[0].ToString());
                AssertFootprintInside(item, placed[0], x0, y0, x1, y1);
            }
        }

        [Test]
        public void Place_AHouseOfTheHousesShape_IsTheHouseAtItsOwnSize_TurnedToFit()
        {
            var placed = new List<WallSprite>();
            Assert.That(WallArt.Place(RepoArt(), WallKind.House, 10, 20, 15, 26, placed), Is.True); // 5 × 6: the house (6 × 5) turned
            Assert.That(placed[0].ObjectId, Is.EqualTo("house"));
            Assert.That(placed[0].Rotation, Is.EqualTo(90));
            Assert.That(placed[0].Scale, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(placed[0].X, Is.EqualTo(12.5f).Within(1e-4f));
            Assert.That(placed[0].Y, Is.EqualTo(23f).Within(1e-4f));
        }

        [Test]
        public void Place_ALongThinObstacle_IsAFenceRepeatedAlongIt()
        {
            ArtCatalog art = RepoArt();
            var placed = new List<WallSprite>();
            Assert.That(WallArt.Place(art, WallKind.Obstacle, 0, 10, 28.5f, 11, placed), Is.True); // the gate bench's walls
            MapObjectArt fence = art.FindMap(WallArt.Fence)!;
            int count = (int)Math.Round(28.5f / fence.WallWidth);
            Assert.That(placed.Count, Is.EqualTo(count));
            Assert.That(placed.All(p => p.ObjectId == WallArt.Fence && p.Rotation == 0), Is.True);
            Assert.That(placed[0].Scale * fence.WallWidth * count, Is.EqualTo(28.5f).Within(1e-3f));
            Assert.That(placed[count - 1].X - placed[0].X, Is.EqualTo(28.5f - (28.5f / count)).Within(1e-3f));

            placed.Clear();
            Assert.That(WallArt.Place(art, WallKind.Obstacle, 5, 0, 5.5f, 6, placed), Is.True); // 0.5 × 6, north-south
            Assert.That(placed.Single().Rotation, Is.EqualTo(90));
            Assert.That(placed.Single().Y, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void Place_AWallNothingFits_KeepsThePlainRectangle()
        {
            var placed = new List<WallSprite>();
            Assert.That(WallArt.Place(RepoArt(), WallKind.House, 0, 0, 60, 60, placed), Is.False); // no building is 60 m
            Assert.That(WallArt.Place(ArtCatalog.Empty, WallKind.Obstacle, 0, 0, 6, 6, placed), Is.False);
            Assert.That(placed, Is.Empty);
        }

        [Test]
        public void Place_AnObjectWhoseWallIsOffItsPivot_CentresTheWall()
        {
            var rocks = new MapObjectArt("boulders", "obstacle", "nature", "Map/map_boulders", 1, 1f, 0.5f, 4f, 2f);
            var art = new ArtCatalog(new TokenArt[0], new TokenArt[0], new[] { rocks }, new Dictionary<string, string>());
            var placed = new List<WallSprite>();
            Assert.That(WallArt.Place(art, WallKind.Obstacle, 0, 0, 4, 2, placed), Is.True); // as drawn
            Assert.That(placed[0].X, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(placed[0].Y, Is.EqualTo(0.5f).Within(1e-4f));

            placed.Clear();
            Assert.That(WallArt.Place(art, WallKind.Obstacle, 0, 0, 4, 8, placed), Is.True); // turned, twice the size
            Assert.That(placed[0].Rotation, Is.EqualTo(90));
            Assert.That(placed[0].Scale, Is.EqualTo(2f).Within(1e-4f));
            Assert.That(placed[0].X, Is.EqualTo(2f + 1f).Within(1e-4f)); // the wall's offset (1, 0.5) × 2, turned: (-1, 2)
            Assert.That(placed[0].Y, Is.EqualTo(4f - 2f).Within(1e-4f));
        }

        private static float Metres(Knez.Sim.Numerics.Fix value) => value.Raw / (float)Knez.Sim.Numerics.Fix.OneRaw; // as FixViewExtensions, without Unity types

        // The object's own wall, turned and scaled as placed, lies inside the sim's wall and is centred on it.
        private static void AssertFootprintInside(MapObjectArt item, WallSprite placed, float x0, float y0, float x1, float y1)
        {
            bool turned = placed.Rotation == 90;
            float w = (turned ? item.WallHeight : item.WallWidth) * placed.Scale;
            float h = (turned ? item.WallWidth : item.WallHeight) * placed.Scale;
            float ox = item.WallX * placed.Scale, oy = item.WallY * placed.Scale;
            float cx = placed.X + (turned ? -oy : ox);
            float cy = placed.Y + (turned ? ox : oy);
            Assert.That(cx, Is.EqualTo((x0 + x1) / 2).Within(1e-3f), placed.ToString());
            Assert.That(cy, Is.EqualTo((y0 + y1) / 2).Within(1e-3f), placed.ToString());
            Assert.That(w, Is.LessThanOrEqualTo(x1 - x0 + 1e-3f), placed.ToString());
            Assert.That(h, Is.LessThanOrEqualTo(y1 - y0 + 1e-3f), placed.ToString());
        }
    }
}
