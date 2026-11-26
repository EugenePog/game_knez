#nullable enable
using System;
using System.Collections.Generic;
using Knez.Sim.Content;
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// Loads the art for one battle view (docs/art-pipeline.md): the manifest and palette from
    /// <c>Assets/Knez/Art/Resources</c>, and sprites by the manifest's Resources paths. Anything missing is reported
    /// once and drawn with the primitives instead. An instance per <see cref="BattleView.Build"/>, not a static: with
    /// domain reload off a static cache would outlive Play mode.
    /// </summary>
    public sealed class ArtLibrary
    {
        /// <summary>Resources path of the manifest (<c>art-manifest.json</c>).</summary>
        public const string ManifestPath = "art-manifest";

        /// <summary>Resources path of the palette (<c>palette.json</c>).</summary>
        public const string PalettePath = "palette";

        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Loads the manifest and palette; on any problem <see cref="Catalog"/> is empty and <see cref="Problem"/> says why.</summary>
        public ArtLibrary()
        {
            TextAsset manifest = Resources.Load<TextAsset>(ManifestPath);
            if (manifest == null)
            {
                Catalog = ArtCatalog.Empty;
                Problem = $"no {ManifestPath}.json in a Resources folder (run node tools/art/export_game_art.js)";
                return;
            }

            TextAsset palette = Resources.Load<TextAsset>(PalettePath);
            try
            {
                Catalog = ArtCatalog.Parse(manifest.text, palette != null ? palette.text : null);
            }
            catch (Exception e) when (e is ContentException || e is FormatException)
            {
                Catalog = ArtCatalog.Empty;
                Problem = e.Message;
            }
        }

        /// <summary>What the art holds; empty when it couldn't be read.</summary>
        public ArtCatalog Catalog { get; }

        /// <summary>Why there is no art, or null.</summary>
        public string? Problem { get; }

        /// <summary>The sprite at Resources path <paramref name="path"/>, or null (warned once) when it isn't there.</summary>
        public Sprite? Sprite(string? path)
        {
            if (path == null || _missing.Contains(path))
            {
                return null;
            }

            if (_sprites.TryGetValue(path, out Sprite cached) && cached != null)
            {
                return cached;
            }

            Sprite loaded = Resources.Load<Sprite>(path);
            if (loaded == null)
            {
                _missing.Add(path);
                Debug.LogWarning($"Art: no sprite at Resources/{path} — drawing the primitive instead");
                return null;
            }

            _sprites[path] = loaded;
            return loaded;
        }

        /// <summary>The palette's map colour <paramref name="name"/> (e.g. <c>grass</c>), or <paramref name="fallback"/>.</summary>
        public Color MapColour(string name, Color fallback) =>
            Catalog.MapColours.TryGetValue(name, out string hex) && ColorUtility.TryParseHtmlString(hex, out Color colour) ? colour : fallback;
    }
}
