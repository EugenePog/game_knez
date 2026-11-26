#nullable enable
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// Plain white shapes made at run time, tinted per use (overlays, and the fallback where the art has no sprite — docs/art-pipeline.md): a disc for fighters and a
    /// square for walls, ticks and markers. Each is one Unity unit across, so a transform's scale is its size in metres.
    /// </summary>
    public static class ShapeSprites
    {
        private const int Size = 64;
        private static Sprite? _disc;
        private static Sprite? _square;
        private static Material? _lineMaterial;

        // Unity's == on purpose, not ??: objects made in Play mode are destroyed when it ends, and with domain reload off
        // (this project's Enter Play Mode setting) the statics survive, holding destroyed objects that only == sees as null.

        /// <summary>A white disc one unit across.</summary>
        public static Sprite Disc
        {
            get
            {
                if (_disc == null)
                {
                    _disc = Make(disc: true);
                }

                return _disc;
            }
        }

        /// <summary>A white square one unit across.</summary>
        public static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    _square = Make(disc: false);
                }

                return _square;
            }
        }

        /// <summary>An unlit material for LineRenderers (routes), tinted by the line's colours.</summary>
        public static Material LineMaterial
        {
            get
            {
                if (_lineMaterial == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    _lineMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
                }

                return _lineMaterial;
            }
        }

        private static Sprite Make(bool disc)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            float r = (Size / 2f) - 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - r;
                    float dy = y - r;
                    float alpha = disc ? Mathf.Clamp01(r + 0.5f - Mathf.Sqrt((dx * dx) + (dy * dy))) : 1f; // soft 1-pixel edge
                    pixels[(y * Size) + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }
    }
}
