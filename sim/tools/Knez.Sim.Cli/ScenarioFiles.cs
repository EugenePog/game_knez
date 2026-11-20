using Knez.Sim.Content;
using Knez.Sim.Defs;

namespace Knez.Sim.Cli;

/// <summary>
/// Reads content from disk for the CLI (the sim and Knez.Sim.Content never touch files). The defs are every
/// <c>defs/*.json</c> in the nearest folder above the scenario that has a <c>defs</c> folder — <c>content/</c> in this repo,
/// so <c>content/scenarios/x.json</c> and <c>content/scenarios/bench/y.json</c> both find <c>content/defs/</c>.
/// </summary>
public static class ScenarioFiles
{
    /// <summary>Loads a scenario and the defs it uses. Throws <see cref="FileNotFoundException"/>,
    /// <see cref="DirectoryNotFoundException"/> or <see cref="ContentException"/> with a message fit for the user.</summary>
    public static ScenarioDef Load(string scenarioPath)
    {
        string fullPath = Path.GetFullPath(scenarioPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"scenario file not found: {scenarioPath}", scenarioPath);
        }

        string defsDir = FindDefsDirectory(fullPath)
            ?? throw new DirectoryNotFoundException($"no 'defs' folder above {scenarioPath}; scenarios live under content/");

        var defFiles = Directory.GetFiles(defsDir, "*.json").Order(StringComparer.Ordinal).Select(Read).ToArray();
        var defs = DefLibrary.Parse(defFiles);
        return ScenarioLoader.Parse(Read(fullPath), defs);
    }

    private static string? FindDefsDirectory(string scenarioFile)
    {
        for (var dir = Path.GetDirectoryName(scenarioFile); dir != null; dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "defs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The file's text, named by its path relative to the working directory for error messages.</summary>
    private static SourceText Read(string path)
    {
        string name = Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
        return new SourceText(name, File.ReadAllText(path));
    }
}
