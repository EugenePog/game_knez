using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Knez.Sim.Content.Json
{
    /// <summary>
    /// Writes JSON text in the layout content files use: objects one member per line, arrays of scalars and small
    /// objects (no nested objects or arrays of arrays) on one line. Numbers are written from their source text, so a
    /// tree read by <see cref="JsonReader"/> writes back to the same values.
    /// </summary>
    internal sealed class JsonWriter
    {
        private const string Indent = "  ";
        private readonly StringBuilder _text = new StringBuilder();

        /// <summary>The text written so far.</summary>
        public override string ToString() => _text.ToString();

        /// <summary>Appends text as it is: a value someone already formatted.</summary>
        public JsonWriter Raw(string text)
        {
            _text.Append(text);
            return this;
        }

        /// <summary>Appends a line break and <paramref name="depth"/> levels of indentation.</summary>
        public JsonWriter NewLine(int depth)
        {
            _text.Append('\n');
            for (int i = 0; i < depth; i++)
            {
                _text.Append(Indent);
            }

            return this;
        }

        /// <summary>Appends <paramref name="value"/> at nesting <paramref name="depth"/>.</summary>
        public JsonWriter Value(JsonValue value, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.String:
                    return String(value.Text);
                case JsonKind.Array:
                    return Array(value, depth);
                case JsonKind.Object:
                    return IsInline(value) ? InlineObject(value) : Object(value.Members, depth);
                default:
                    return Raw(value.Text); // number (source text), true, false, null
            }
        }

        /// <summary>Appends an object, one member per line, at nesting <paramref name="depth"/>.</summary>
        public JsonWriter Object(IReadOnlyList<JsonMember> members, int depth)
        {
            if (members.Count == 0)
            {
                return Raw("{}");
            }

            _text.Append('{');
            for (int i = 0; i < members.Count; i++)
            {
                NewLine(depth + 1).String(members[i].Name).Raw(": ").Value(members[i].Value, depth + 1);
                if (i + 1 < members.Count)
                {
                    _text.Append(',');
                }
            }

            return NewLine(depth).Raw("}");
        }

        /// <summary>Appends a quoted, escaped string.</summary>
        public JsonWriter String(string value)
        {
            _text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': _text.Append("\\\""); break;
                    case '\\': _text.Append("\\\\"); break;
                    case '\n': _text.Append("\\n"); break;
                    case '\r': _text.Append("\\r"); break;
                    case '\t': _text.Append("\\t"); break;
                    case '\b': _text.Append("\\b"); break;
                    case '\f': _text.Append("\\f"); break;
                    default:
                        if (c < ' ')
                        {
                            _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _text.Append(c);
                        }

                        break;
                }
            }

            _text.Append('"');
            return this;
        }

        private JsonWriter Array(JsonValue array, int depth)
        {
            IReadOnlyList<JsonValue> items = array.Items;
            if (items.Count == 0)
            {
                return Raw("[]");
            }

            bool inline = true;
            for (int i = 0; i < items.Count && inline; i++)
            {
                inline = IsScalar(items[i]);
            }

            _text.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (inline)
                {
                    if (i > 0)
                    {
                        _text.Append(", ");
                    }
                }
                else
                {
                    NewLine(depth + 1);
                }

                Value(items[i], depth + 1);
                if (!inline && i + 1 < items.Count)
                {
                    _text.Append(',');
                }
            }

            if (!inline)
            {
                NewLine(depth);
            }

            return Raw("]");
        }

        private JsonWriter InlineObject(JsonValue value)
        {
            _text.Append("{ ");
            for (int i = 0; i < value.Members.Count; i++)
            {
                if (i > 0)
                {
                    _text.Append(", ");
                }

                String(value.Members[i].Name).Raw(": ").Value(value.Members[i].Value, 0);
            }

            return Raw(" }");
        }

        /// <summary>An object of scalars and arrays of scalars, e.g. a squad or a wall: short enough for one line.</summary>
        private static bool IsInline(JsonValue value)
        {
            if (value.Members.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < value.Members.Count; i++)
            {
                JsonValue member = value.Members[i].Value;
                if (member.Kind == JsonKind.Object)
                {
                    return false;
                }

                if (member.Kind == JsonKind.Array)
                {
                    for (int j = 0; j < member.Items.Count; j++)
                    {
                        if (!IsScalar(member.Items[j]))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool IsScalar(JsonValue value) => value.Kind != JsonKind.Array && value.Kind != JsonKind.Object;
    }
}
