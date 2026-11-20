namespace Knez.Sim.Content.Json
{
    /// <summary>One <c>"name": value</c> pair of a JSON object.</summary>
    public readonly struct JsonMember
    {
        /// <summary>Pairs a name with its value.</summary>
        public JsonMember(string name, JsonValue value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>Member name.</summary>
        public string Name { get; }

        /// <summary>Member value.</summary>
        public JsonValue Value { get; }
    }
}
