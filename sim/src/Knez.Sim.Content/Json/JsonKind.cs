namespace Knez.Sim.Content.Json
{
    /// <summary>Kind of a <see cref="JsonValue"/>.</summary>
    public enum JsonKind
    {
        /// <summary><c>null</c>.</summary>
        Null,

        /// <summary><c>true</c> or <c>false</c>.</summary>
        Boolean,

        /// <summary>A number, kept as its source text.</summary>
        Number,

        /// <summary>A string.</summary>
        String,

        /// <summary>An array.</summary>
        Array,

        /// <summary>An object.</summary>
        Object,
    }
}
