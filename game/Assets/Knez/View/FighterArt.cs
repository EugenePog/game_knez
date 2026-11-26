#nullable enable
using System;
using System.Collections.Generic;
using Knez.Sim.Combat;
using Knez.Sim.Defs;
using Knez.Sim.Morale;
using Knez.Sim.Units;

namespace Knez.Game.View
{
    /// <summary>
    /// Which token a man wears and how it is drawn (docs/art-pipeline.md). Plain C#, view-only: it reads a fighter and the
    /// <see cref="ArtCatalog"/>, never the other way round.
    /// <para>The token, first that exists: for a Retainer, himself by name (the roster's man on his side in his kit), then a
    /// generic Retainer of his side in his kit; an unnamed fighter of his side in his kit; for a Retainer, a generic
    /// Retainer of his side in the drawn kit nearest to his; an unnamed fighter in the nearest kit. His kit beats his rank
    /// because the kit is what the player reads (the gold ring already marks a Retainer). Nearest kit: same weapon first,
    /// then body armour, head cover, shield. Within the kit, his experience if drawn, then a variant by his id, so a
    /// squad's men look different.</para>
    /// </summary>
    public static class FighterArt
    {
        /// <summary>The art side a sim side wears when the view isn't told: side 0 the clan, any other the ruler.</summary>
        public static string DefaultSide(int simSide) => simSide == 0 ? "clan" : "ruler";

        /// <summary>The art side of <paramref name="simSide"/>: its entry in <paramref name="sides"/> (by side id) when set, else <see cref="DefaultSide"/>.</summary>
        public static string Side(IReadOnlyList<string>? sides, int simSide)
        {
            if (sides != null && simSide >= 0 && simSide < sides.Count && !string.IsNullOrWhiteSpace(sides[simSide]))
            {
                return sides[simSide].Trim().ToLowerInvariant();
            }

            return DefaultSide(simSide);
        }

        /// <summary>The Resources path of <paramref name="fighter"/>'s token, or null when the art has nothing for his side.</summary>
        public static string? Token(ArtCatalog art, Fighter fighter, string side, IReadOnlyList<KitDef> kits) =>
            Token(art, side, fighter.Kit, fighter.Experience.Id, fighter.Retainer.HasValue ? fighter.Retainer.Value.Name : null, fighter.Id.Value, kits);

        /// <summary>
        /// The Resources path of the token for a man of art side <paramref name="side"/>, carrying <paramref name="kit"/>, of
        /// <paramref name="experience"/>; <paramref name="retainer"/> is his name when he is a Retainer, else null;
        /// <paramref name="id"/> picks the variant. <paramref name="kits"/> are the kits the content knows, to find the
        /// nearest drawn kit when his own isn't drawn. Null when the art has no token for the side.
        /// </summary>
        public static string? Token(ArtCatalog art, string side, KitDef kit, string experience, string? retainer, int id, IReadOnlyList<KitDef> kits)
        {
            if (retainer != null)
            {
                foreach (TokenArt hero in art.Heroes)
                {
                    if (hero.Named && hero.Side == side && hero.Kit == kit.Id && string.Equals(hero.Id, retainer.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return hero.Path;
                    }
                }

                TokenArt? generic = Pick(art.Heroes, side, kit.Id, experience, id, generic: true);
                if (generic != null)
                {
                    return generic.Path;
                }
            }

            TokenArt? man = Pick(art.Fighters, side, kit.Id, experience, id, generic: false);
            if (man != null)
            {
                return man.Path;
            }

            string? near = retainer != null ? Nearest(art.Heroes, side, kit, kits, generic: true) : null;
            if (near != null)
            {
                return Pick(art.Heroes, side, near, experience, id, generic: true)!.Path;
            }

            near = Nearest(art.Fighters, side, kit, kits, generic: false);
            return near != null ? Pick(art.Fighters, side, near, experience, id, generic: false)!.Path : null;
        }

        /// <summary>How <paramref name="fighter"/>'s token is drawn now.</summary>
        public static TokenState State(Fighter fighter) => State(fighter.IsAlive, fighter.Wound, fighter.MoraleState);

        /// <summary>How a man's token is drawn: fallen, gone off the field, running, or in the fight.</summary>
        public static TokenState State(bool alive, WoundLevel wound, MoraleState morale)
        {
            if (wound == WoundLevel.Down)
            {
                return TokenState.Fallen;
            }

            if (!alive || morale == MoraleState.Fled || morale == MoraleState.Left)
            {
                return TokenState.Gone;
            }

            return morale == MoraleState.Routing ? TokenState.Routing : TokenState.Fighting;
        }

        // The drawn kit of this side most like his (ties: the lower kit id), or null when the side has no tokens here.
        private static string? Nearest(IReadOnlyList<TokenArt> tokens, string side, KitDef kit, IReadOnlyList<KitDef> kits, bool generic)
        {
            string? nearest = null;
            int best = -1;
            foreach (TokenArt token in tokens)
            {
                if (token.Side != side || (generic && token.Named))
                {
                    continue;
                }

                int score = Likeness(kit, Find(kits, token.Kit));
                if (score > best || (score == best && nearest != null && string.CompareOrdinal(token.Kit, nearest) < 0))
                {
                    best = score;
                    nearest = token.Kit;
                }
            }

            return nearest;
        }

        // A token of this side and kit: his experience if drawn for it, then a variant by id; null if none.
        private static TokenArt? Pick(IReadOnlyList<TokenArt> tokens, string side, string kit, string experience, int id, bool generic)
        {
            var sameKit = new List<TokenArt>();
            var sameLevel = new List<TokenArt>();
            foreach (TokenArt token in tokens)
            {
                if (token.Side == side && token.Kit == kit && !(generic && token.Named))
                {
                    sameKit.Add(token);
                    if (token.Experience == experience)
                    {
                        sameLevel.Add(token);
                    }
                }
            }

            List<TokenArt> pool = sameLevel.Count > 0 ? sameLevel : sameKit;
            if (pool.Count == 0)
            {
                return null;
            }

            pool.Sort((a, b) => a.Variant != b.Variant ? a.Variant.CompareTo(b.Variant) : string.CompareOrdinal(a.Path, b.Path));
            int index = id % pool.Count;
            return pool[index < 0 ? index + pool.Count : index];
        }

        // How alike two kits look: the weapon is the silhouette, then body armour, head cover, shield.
        private static int Likeness(KitDef kit, KitDef? other)
        {
            if (other == null)
            {
                return 0;
            }

            return (kit.Weapon.Id == other.Weapon.Id ? 8 : 0) + (kit.BodyArmour.Id == other.BodyArmour.Id ? 4 : 0)
                + (kit.HeadCover.Id == other.HeadCover.Id ? 2 : 0) + (kit.Shield.Id == other.Shield.Id ? 1 : 0);
        }

        private static KitDef? Find(IReadOnlyList<KitDef> kits, string id)
        {
            foreach (KitDef kit in kits)
            {
                if (kit.Id == id)
                {
                    return kit;
                }
            }

            return null;
        }
    }
}
