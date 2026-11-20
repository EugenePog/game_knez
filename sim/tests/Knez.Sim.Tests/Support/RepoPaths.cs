namespace Knez.Sim.Tests.Support;

/// <summary>Locates the repository from the test binaries, so tests read the real content/ folder, never a copy.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Content => Path.Combine(Root, "content");

    public static string Scenario(string name) => Path.Combine(Content, "scenarios", name + ".json");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AGENTS.md) not found above the test directory.");
    }
}
