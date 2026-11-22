using System;
using System.Collections.Generic;
using System.IO;
using Knez.Game.View;
using Knez.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Knez.Game.Tests
{
    /// <summary>
    /// Cross-runtime determinism (M2 step 3, battle-sim.md §15): runs every entry of the repo's
    /// <c>sim/tests/Golden/content-traces.json</c> inside Unity and compares the per-tick hash trace and the final hash
    /// with the values the .NET tests record and check (<c>ContentTraceGoldenTests</c>). The same file, the same content,
    /// two runtimes: any difference is a determinism bug in the sim, never something to re-record here.
    /// </summary>
    public class CrossRuntimeGoldenTests
    {
        [Serializable]
        private class Entry
        {
            public string scenario = string.Empty;
            public int seed;
            public int ticks;
            public string trace = string.Empty;
            public string final = string.Empty;
        }

        [Serializable]
        private class Golden
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static string GoldenFile =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "sim", "tests", "Golden", "content-traces.json"));

        private static IEnumerable<TestCaseData> Cases()
        {
            var golden = JsonUtility.FromJson<Golden>(File.ReadAllText(GoldenFile));
            foreach (var entry in golden.entries)
            {
                yield return new TestCaseData(entry.scenario, entry.seed, entry.ticks, entry.trace, entry.final).SetName("Golden_" + entry.scenario);
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void ContentScenario_MatchesTheDotnetTrace(string scenario, int seed, int ticks, string trace, string final)
        {
            var sim = new BattleSimulation(BattleSetup.Create(ContentFiles.LoadScenario(ContentFiles.DefaultRoot(), scenario), (ulong)seed));
            ulong fold = 14695981039346656037UL;
            for (int i = 0; i < ticks; i++)
            {
                sim.Step();
                fold = unchecked((fold ^ sim.ComputeHash().Total) * 1099511628211UL);
            }

            Assert.That(fold.ToString("x16"), Is.EqualTo(trace), scenario + ": per-tick trace");
            Assert.That(sim.ComputeHash().Total.ToString("x16"), Is.EqualTo(final), scenario + ": final hash");
        }

        [Test]
        public void TheGoldenFile_HasEveryScenario()
        {
            var golden = JsonUtility.FromJson<Golden>(File.ReadAllText(GoldenFile));
            Assert.That(golden.entries.Count, Is.GreaterThanOrEqualTo(11));
        }
    }
}
