using System;
using System.Collections.Generic;

namespace Knez.Sim.Content.Json
{
    /// <summary>
    /// Immutable JSON tree node from <see cref="JsonReader"/>. Numbers keep their source text so they can be turned
    /// into <c>Fix</c> exactly (ADR-0004); object members keep file order; every node knows its line and column.
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue[] NoItems = Array.Empty<JsonValue>();
        private static readonly JsonMember[] NoMembers = Array.Empty<JsonMember>();

        private JsonValue(JsonKind kind, string text, IReadOnlyList<JsonValue> items, IReadOnlyList<JsonMember> members,
            int line, int column)
        {
            Kind = kind;
            Text = text;
            Items = items;
            Members = members;
            Line = line;
            Column = column;
        }

        /// <summary>What the node is.</summary>
        public JsonKind Kind { get; }

        /// <summary>String: the decoded value. Number: the source text. Boolean/null: the literal. Array/object: empty.</summary>
        public string Text { get; }

        /// <summary>Array items; empty for other kinds.</summary>
        public IReadOnlyList<JsonValue> Items { get; }

        /// <summary>Object members in file order (names are unique); empty for other kinds.</summary>
        public IReadOnlyList<JsonMember> Members { get; }

        /// <summary>1-based line of the node's first character.</summary>
        public int Line { get; }

        /// <summary>1-based column (UTF-16 units) of the node's first character.</summary>
        public int Column { get; }

        /// <summary>The member called <paramref name="name"/> (ordinal), or null. Linear scan: content objects are small.</summary>
        public JsonValue? Get(string name)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Name, name, StringComparison.Ordinal))
                {
                    return Members[i].Value;
                }
            }

            return null;
        }

        /// <summary>Kind name for messages: "a number", "an object", …</summary>
        public string Describe() => KindName(Kind);

        /// <summary>Kind name for messages: "a number", "an object", …</summary>
        public static string KindName(JsonKind kind) => kind switch
        {
            JsonKind.Null => "null",
            JsonKind.Boolean => "a boolean",
            JsonKind.Number => "a number",
            JsonKind.String => "a string",
            JsonKind.Array => "an array",
            _ => "an object",
        };

        internal static JsonValue Scalar(JsonKind kind, string text, int line, int column) =>
            new JsonValue(kind, text, NoItems, NoMembers, line, column);

        internal static JsonValue NewArray(IReadOnlyList<JsonValue> items, int line, int column) =>
            new JsonValue(JsonKind.Array, string.Empty, items, NoMembers, line, column);

        internal static JsonValue NewObject(IReadOnlyList<JsonMember> members, int line, int column) =>
            new JsonValue(JsonKind.Object, string.Empty, NoItems, members, line, column);
    }
}
