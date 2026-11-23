using System;
using System.Collections.Generic;
using System.Globalization;
using Knez.Sim.Content.Json;
using Knez.Sim.Numerics;

namespace Knez.Sim.Content
{
    /// <summary>
    /// Strict reader over one JSON object: typed getters that know the member's JSON path, and a final check that every
    /// member was read — so a typo like <c>"rankspacing"</c> is an error instead of a silently ignored value.
    /// </summary>
    internal sealed class JsonObjectReader
    {
        private readonly JsonValue _object;
        private readonly bool[] _read;

        private JsonObjectReader(JsonValue value, string path, string source)
        {
            _object = value;
            Path = path;
            Source = source;
            _read = new bool[value.Members.Count];
        }

        /// <summary>JSON path of this object, e.g. "squads[1]"; empty for the root.</summary>
        public string Path { get; }

        /// <summary>Name of the file the object came from.</summary>
        public string Source { get; }

        public static JsonObjectReader Of(JsonValue value, string path, string source)
        {
            if (value.Kind != JsonKind.Object)
            {
                throw Error(source, value, path, $"expected an object, got {value.Describe()}");
            }

            return new JsonObjectReader(value, path, source);
        }

        public string RequireString(string name)
        {
            JsonValue value = Require(name);
            ExpectKind(value, name, JsonKind.String);
            return value.Text;
        }

        public string? OptionalString(string name)
        {
            JsonValue? value = Optional(name);
            if (value == null)
            {
                return null;
            }

            ExpectKind(value, name, JsonKind.String);
            return value.Text;
        }

        public Fix RequireFix(string name) => ToFix(Require(name), MemberPath(name));

        public Fix? OptionalFix(string name)
        {
            JsonValue? value = Optional(name);
            return value == null ? (Fix?)null : ToFix(value, MemberPath(name));
        }

        /// <summary>The named boolean, or <paramref name="fallback"/> when it is absent.</summary>
        public bool OptionalBool(string name, bool fallback)
        {
            JsonValue? value = Optional(name);
            if (value == null)
            {
                return fallback;
            }

            ExpectKind(value, name, JsonKind.Boolean);
            return string.Equals(value.Text, "true", StringComparison.Ordinal);
        }

        public int RequireInt(string name) => ToInt(Require(name), MemberPath(name));

        public int OptionalInt(string name, int fallback)
        {
            JsonValue? value = Optional(name);
            return value == null ? fallback : ToInt(value, MemberPath(name));
        }

        /// <summary>A point written as <c>[x, y]</c> in metres.</summary>
        public FixVec2 RequireVec2(string name)
        {
            JsonValue value = Require(name);
            string path = MemberPath(name);
            if (value.Kind != JsonKind.Array || value.Items.Count != 2)
            {
                throw Error(value, path, $"expected [x, y], got {value.Describe()}");
            }

            return new FixVec2(ToFix(value.Items[0], path + "[0]"), ToFix(value.Items[1], path + "[1]"));
        }

        public IReadOnlyList<JsonValue> RequireArray(string name)
        {
            JsonValue value = Require(name);
            ExpectKind(value, name, JsonKind.Array);
            return value.Items;
        }

        public IReadOnlyList<JsonValue> OptionalArray(string name)
        {
            JsonValue? value = Optional(name);
            if (value == null)
            {
                return Array.Empty<JsonValue>();
            }

            ExpectKind(value, name, JsonKind.Array);
            return value.Items;
        }

        public JsonObjectReader RequireObject(string name) => Of(Require(name), MemberPath(name), Source);

        /// <summary>The named member as an object, or null when it is absent.</summary>
        public JsonObjectReader? OptionalObject(string name)
        {
            JsonValue? value = Optional(name);
            return value == null ? null : Of(value, MemberPath(name), Source);
        }

        /// <summary>The named member's value (for locating errors about it), or null; does not count as reading it.</summary>
        public JsonValue? Member(string name)
        {
            IReadOnlyList<JsonMember> members = _object.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (string.Equals(members[i].Name, name, StringComparison.Ordinal))
                {
                    return members[i].Value;
                }
            }

            return null;
        }

        /// <summary>Throws for the first member no getter asked for.</summary>
        public void RejectUnknownMembers(string allowed)
        {
            for (int i = 0; i < _read.Length; i++)
            {
                if (!_read[i])
                {
                    JsonMember member = _object.Members[i];
                    throw Error(member.Value, MemberPath(member.Name), $"unknown member '{member.Name}' (allowed: {allowed})");
                }
            }
        }

        /// <summary>A located error about this object (or one of its members, when <paramref name="at"/> is given).</summary>
        public ContentException Error(JsonValue at, string path, string message) => Error(Source, at, path, message);

        public ContentException Error(string message) => Error(Source, _object, Path, message);

        public string MemberPath(string name) => Path.Length == 0 ? name : Path + "." + name;

        public static ContentException Error(string source, JsonValue at, string path, string message, Exception? inner = null)
        {
            string location = source + ":" + at.Line.ToString(CultureInfo.InvariantCulture) + ":" +
                              at.Column.ToString(CultureInfo.InvariantCulture) + ": ";
            string text = location + (path.Length == 0 ? string.Empty : path + ": ") + message;
            return inner == null ? new ContentException(text) : new ContentException(text, inner);
        }

        /// <summary>A def constructor's <see cref="ArgumentException"/> (message only, no parameter name) as a located
        /// content error.</summary>
        public static ContentException FromDef(string source, JsonValue at, string path, ArgumentException ex) =>
            Error(source, at, path, ex.Message, ex);

        private JsonValue Require(string name)
        {
            JsonValue? value = Optional(name);
            if (value == null)
            {
                throw Error($"missing member '{name}'");
            }

            return value;
        }

        private JsonValue? Optional(string name)
        {
            IReadOnlyList<JsonMember> members = _object.Members;
            for (int i = 0; i < members.Count; i++)
            {
                if (string.Equals(members[i].Name, name, StringComparison.Ordinal))
                {
                    _read[i] = true;
                    return members[i].Value;
                }
            }

            return null;
        }

        private void ExpectKind(JsonValue value, string name, JsonKind kind)
        {
            if (value.Kind != kind)
            {
                string expected = JsonValue.KindName(kind);
                throw Error(value, MemberPath(name), $"expected {expected}, got {value.Describe()}");
            }
        }

        private Fix ToFix(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.Number)
            {
                throw Error(value, path, $"expected a number, got {value.Describe()}");
            }

            if (!ContentNumbers.TryParseFix(value.Text, out Fix result, out string error))
            {
                throw Error(value, path, error);
            }

            return result;
        }

        private int ToInt(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.Number)
            {
                throw Error(value, path, $"expected a whole number, got {value.Describe()}");
            }

            if (!ContentNumbers.TryParseInt(value.Text, out int result, out string error))
            {
                throw Error(value, path, error);
            }

            return result;
        }
    }
}
