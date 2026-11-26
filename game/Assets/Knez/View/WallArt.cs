#nullable enable
using System;
using System.Collections.Generic;
using Knez.Sim.Defs;

namespace Knez.Game.View
{
    /// <summary>
    /// Which map sprites stand for a sim wall (docs/art-pipeline.md). Scenarios still place bare rectangles, not catalogue
    /// objects, so the view picks: a house wall gets the building whose footprint fits it best; an obstacle gets rocks; a
    /// long thin obstacle gets a palisade repeated along it. Sprites are only scaled uniformly and turned by 0 or 90°, and
    /// centred on the wall; a wall nothing fits within the scale limits keeps the plain rectangle. Plain C#.
    /// </summary>
    public static class WallArt
    {
        /// <summary>The objects a plain obstacle is drawn as: the sandboxes' obstacles are rock outcrops.</summary>
        public static readonly IReadOnlyList<string> Rocks = new[] { "boulders" };

        /// <summary>The object a long thin obstacle is drawn as, repeated along it (the walls of a gate, a fence).</summary>
        public const string Fence = "palisade";

        /// <summary>An obstacle at least this many times longer than thick is a fence.</summary>
        public const float ThinAspect = 4f;

        /// <summary>Scale limits for a building: a little smaller or bigger reads fine, a giant granary doesn't.</summary>
        public const float MinHouseScale = 0.75f;

        /// <summary>Upper scale limit for a building.</summary>
        public const float MaxHouseScale = 1.5f;

        /// <summary>Scale limits for rocks: a heap of stones has no natural size.</summary>
        public const float MinRockScale = 0.5f;

        /// <summary>Upper scale limit for rocks.</summary>
        public const float MaxRockScale = 2.5f;

        /// <summary>Scale limits for one length of fence.</summary>
        public const float MinFenceScale = 0.5f;

        /// <summary>Upper scale limit for one length of fence.</summary>
        public const float MaxFenceScale = 1.5f;

        // Among fits that cover the wall equally well, prefer the sprite nearer its drawn size.
        private const double NativeBias = 0.1;

        /// <summary>
        /// Adds to <paramref name="into"/> the sprites that draw the wall from (<paramref name="minX"/>, <paramref name="minY"/>)
        /// to (<paramref name="maxX"/>, <paramref name="maxY"/>) metres; false (nothing added) when nothing fits.
        /// </summary>
        public static bool Place(ArtCatalog art, WallKind kind, float minX, float minY, float maxX, float maxY, List<WallSprite> into)
        {
            float w = maxX - minX;
            float h = maxY - minY;
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            if (kind == WallKind.Obstacle && Math.Max(w, h) >= ThinAspect * Math.Min(w, h))
            {
                return PlaceFence(art, minX, minY, w, h, into);
            }

            float lowest = kind == WallKind.House ? MinHouseScale : MinRockScale;
            float highest = kind == WallKind.House ? MaxHouseScale : MaxRockScale;
            MapObjectArt? best = null;
            float bestScale = 0;
            int bestTurn = 0;
            double bestScore = double.MinValue;
            foreach (MapObjectArt item in art.Map)
            {
                if (item.WallCount != 1 || !Stands(item, kind))
                {
                    continue;
                }

                for (int turn = 0; turn <= 90; turn += 90)
                {
                    float fw = turn == 0 ? item.WallWidth : item.WallHeight;
                    float fh = turn == 0 ? item.WallHeight : item.WallWidth;
                    if (fw <= 0 || fh <= 0)
                    {
                        continue;
                    }

                    float scale = Math.Min(w / fw, h / fh); // the footprint fits inside the wall
                    if (scale < lowest - 1e-4f || scale > highest + 1e-4f)
                    {
                        continue;
                    }

                    double cover = scale * scale * fw * fh / (w * h);
                    double score = cover - (NativeBias * Math.Abs(Math.Log(scale)));
                    if (score > bestScore + 1e-6)
                    {
                        (best, bestScale, bestTurn, bestScore) = (item, scale, turn, score);
                    }
                }
            }

            if (best == null)
            {
                return false;
            }

            into.Add(At(best, minX + (w / 2), minY + (h / 2), bestTurn, bestScale));
            return true;
        }

        // Whether a catalogue object may stand for a wall of this kind.
        private static bool Stands(MapObjectArt item, WallKind kind)
        {
            if (kind == WallKind.House)
            {
                return item.Sim == "house";
            }

            foreach (string id in Rocks)
            {
                if (item.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        // Lengths of fence along the wall's long side, each scaled so that a whole number of them fills it.
        private static bool PlaceFence(ArtCatalog art, float minX, float minY, float w, float h, List<WallSprite> into)
        {
            MapObjectArt? fence = art.FindMap(Fence);
            if (fence == null || fence.WallCount != 1 || fence.WallWidth <= 0 || fence.WallHeight <= 0)
            {
                return false;
            }

            bool alongX = w >= h;
            float length = alongX ? w : h;
            float piece = Math.Max(fence.WallWidth, fence.WallHeight);
            int count = Math.Max(1, (int)Math.Round(length / piece));
            float scale = length / (count * piece);
            if (scale < MinFenceScale - 1e-4f || scale > MaxFenceScale + 1e-4f)
            {
                return false;
            }

            int ownTurn = fence.WallWidth >= fence.WallHeight ? 0 : 90; // turn that lays the drawn fence east-west
            int turn = (ownTurn + (alongX ? 0 : 90)) % 180;
            for (int i = 0; i < count; i++)
            {
                float along = (i + 0.5f) * length / count;
                into.Add(At(fence, alongX ? minX + along : minX + (w / 2), alongX ? minY + (h / 2) : minY + along, turn, scale));
            }

            return true;
        }

        // The sprite placed so that the object's wall, turned and scaled, is centred on (cx, cy).
        private static WallSprite At(MapObjectArt item, float cx, float cy, int turn, float scale)
        {
            float ox = item.WallX * scale;
            float oy = item.WallY * scale;
            (float rx, float ry) = turn == 90 ? (-oy, ox) : (ox, oy); // counter-clockwise, as Unity turns about z
            return new WallSprite(item.Sprite, item.Id, cx - rx, cy - ry, turn, scale);
        }
    }
}
