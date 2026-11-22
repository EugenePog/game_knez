using System;
using System.Collections.Generic;
using System.IO;
using Knez.Sim.Content;
using Knez.Sim.Defs;
using UnityEngine;

namespace Knez.Game.View
{
    /// <summary>
    /// Reads content text from disk for the view (the sim and Knez.Sim.Content never touch files): every
    /// <c>defs/*.json</c> under the content root, then one scenario file, parsed by <see cref="DefLibrary"/> and
    /// <see cref="ScenarioLoader"/>. In the editor the root is the repo's own <c>content/</c> folder, so there is one
    /// copy of the content; builds will copy it into StreamingAssets.
    /// </summary>
    public static class ContentFiles
    {
        /// <summary>The content folder: the repo's <c>content/</c> in the editor, <c>StreamingAssets/content</c> in a build.</summary>
        public static string DefaultRoot() =>
            Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "content"))
                : Path.Combine(Application.streamingAssetsPath, "content");

        /// <summary>
        /// Loads <paramref name="scenario"/> (a path relative to <paramref name="root"/>, e.g. "scenarios/drill.json") with
        /// the defs in <c>root/defs</c>. Throws <see cref="ContentException"/> for bad content and
        /// <see cref="IOException"/> for missing files.
        /// </summary>
        public static ScenarioDef LoadScenario(string root, string scenario)
        {
            string defsFolder = Path.Combine(root, "defs");
            if (!Directory.Exists(defsFolder))
            {
                throw new DirectoryNotFoundException($"no defs folder at {defsFolder}");
            }

            string[] defFiles = Directory.GetFiles(defsFolder, "*.json");
            Array.Sort(defFiles, StringComparer.Ordinal); // file order never matters to the defs, but keep it stable
            var sources = new List<SourceText>(defFiles.Length);
            foreach (string file in defFiles)
            {
                sources.Add(Read(file, root));
            }

            DefLibrary defs = DefLibrary.Parse(sources);
            string scenarioFile = Path.Combine(root, scenario);
            if (!File.Exists(scenarioFile))
            {
                throw new FileNotFoundException($"scenario file not found: {scenarioFile}");
            }

            return ScenarioLoader.Parse(Read(scenarioFile, root), defs);
        }

        private static SourceText Read(string file, string root) =>
            new SourceText(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file));
    }
}
