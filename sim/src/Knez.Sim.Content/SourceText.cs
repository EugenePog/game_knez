namespace Knez.Sim.Content
{
    /// <summary>Content text plus the name used in error messages (usually the repo-relative file path).</summary>
    public readonly struct SourceText
    {
        /// <summary>Pairs a name with its text.</summary>
        public SourceText(string name, string text)
        {
            Name = name;
            Text = text;
        }

        /// <summary>Name for messages, e.g. "content/defs/fighters.json".</summary>
        public string Name { get; }

        /// <summary>The JSON text.</summary>
        public string Text { get; }
    }
}
