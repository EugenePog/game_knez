using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Knez.Sim.Content.Json
{
    /// <summary>
    /// Strict RFC 8259 JSON parser for content (ADR-0004): no comments, no trailing commas, no duplicate member names,
    /// numbers kept as source text. Errors are <see cref="ContentException"/>s located as "name:line:column".
    /// </summary>
    public static class JsonReader
    {
        /// <summary>Deepest nesting accepted; content needs far less, and a bound keeps recursion safe.</summary>
        public const int MaxDepth = 64;

        /// <summary>Parses one JSON document (a leading byte-order mark is skipped).</summary>
        public static JsonValue Parse(SourceText source)
        {
            var parser = new Parser(source);
            return parser.ParseDocument();
        }

        private sealed class Parser
        {
            private readonly string _name;
            private readonly string _text;
            private int _pos;
            private int _line = 1;
            private int _lineStart;

            public Parser(SourceText source)
            {
                _name = source.Name;
                _text = source.Text ?? string.Empty;
                if (_text.Length > 0 && _text[0] == '\uFEFF')
                {
                    _pos = 1;
                    _lineStart = 1;
                }
            }

            public JsonValue ParseDocument()
            {
                SkipWhitespace();
                if (AtEnd)
                {
                    throw Error("the file is empty; expected a JSON value");
                }

                JsonValue root = ParseValue(0);
                SkipWhitespace();
                if (!AtEnd)
                {
                    throw Error($"unexpected '{Printable(_text[_pos])}' after the end of the JSON value");
                }

                return root;
            }

            private bool AtEnd => _pos >= _text.Length;

            private int Column => _pos - _lineStart + 1;

            private JsonValue ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Error($"nesting deeper than {MaxDepth} levels");
                }

                if (AtEnd)
                {
                    throw Error("unexpected end of file; expected a value");
                }

                char c = _text[_pos];
                switch (c)
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                    {
                        int line = _line;
                        int column = Column;
                        return JsonValue.Scalar(JsonKind.String, ParseString(), line, column);
                    }

                    case 't':
                        return ParseLiteral("true", JsonKind.Boolean);
                    case 'f':
                        return ParseLiteral("false", JsonKind.Boolean);
                    case 'n':
                        return ParseLiteral("null", JsonKind.Null);
                    default:
                        if (c == '-' || IsDigit(c))
                        {
                            return ParseNumber();
                        }

                        throw Error($"unexpected '{Printable(c)}'; expected a value");
                }
            }

            private JsonValue ParseObject(int depth)
            {
                int line = _line;
                int column = Column;
                _pos++; // {
                var members = new List<JsonMember>();
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == '}')
                {
                    _pos++;
                    return JsonValue.NewObject(members, line, column);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_pos] != '"')
                    {
                        throw Error(AtEnd ? "unexpected end of file; expected a member name" : "expected a member name in double quotes");
                    }

                    int nameLine = _line;
                    int nameColumn = Column;
                    string name = ParseString();
                    for (int i = 0; i < members.Count; i++)
                    {
                        if (members[i].Name == name)
                        {
                            throw ErrorAt(nameLine, nameColumn, $"duplicate member '{name}'");
                        }
                    }

                    SkipWhitespace();
                    Expect(':', "expected ':' after the member name");
                    SkipWhitespace();
                    members.Add(new JsonMember(name, ParseValue(depth + 1)));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("unexpected end of file; expected ',' or '}'");
                    }

                    char c = _text[_pos++];
                    if (c == '}')
                    {
                        return JsonValue.NewObject(members, line, column);
                    }

                    if (c != ',')
                    {
                        _pos--;
                        throw Error("expected ',' or '}'");
                    }

                    SkipWhitespace();
                    if (!AtEnd && _text[_pos] == '}')
                    {
                        throw Error("trailing comma before '}'");
                    }
                }
            }

            private JsonValue ParseArray(int depth)
            {
                int line = _line;
                int column = Column;
                _pos++; // [
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _text[_pos] == ']')
                {
                    _pos++;
                    return JsonValue.NewArray(items, line, column);
                }

                while (true)
                {
                    SkipWhitespace();
                    items.Add(ParseValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error("unexpected end of file; expected ',' or ']'");
                    }

                    char c = _text[_pos++];
                    if (c == ']')
                    {
                        return JsonValue.NewArray(items, line, column);
                    }

                    if (c != ',')
                    {
                        _pos--;
                        throw Error("expected ',' or ']'");
                    }

                    SkipWhitespace();
                    if (!AtEnd && _text[_pos] == ']')
                    {
                        throw Error("trailing comma before ']'");
                    }
                }
            }

            private string ParseString()
            {
                _pos++; // opening quote
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Error("unterminated string");
                    }

                    char c = _text[_pos];
                    if (c == '"')
                    {
                        _pos++;
                        return builder.ToString();
                    }

                    if (c < ' ')
                    {
                        throw Error(c == '\n' ? "unterminated string (line break inside)" : "control character inside a string; escape it");
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        _pos++;
                        continue;
                    }

                    if (_pos + 1 >= _text.Length)
                    {
                        throw Error("unterminated escape sequence");
                    }

                    char escape = _text[_pos + 1];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            continue; // ParseUnicodeEscape advanced past the six characters
                        default:
                            throw Error($"invalid escape '\\{Printable(escape)}'");
                    }

                    _pos += 2;
                }
            }

            private char ParseUnicodeEscape()
            {
                if (_pos + 6 > _text.Length)
                {
                    throw Error("\\u needs four hex digits");
                }

                int value = 0;
                for (int i = 2; i < 6; i++)
                {
                    int digit = HexValue(_text[_pos + i]);
                    if (digit < 0)
                    {
                        throw Error("\\u needs four hex digits");
                    }

                    value = (value << 4) | digit;
                }

                _pos += 6;
                return (char)value;
            }

            private JsonValue ParseNumber()
            {
                int start = _pos;
                int line = _line;
                int column = Column;
                if (_text[_pos] == '-')
                {
                    _pos++;
                }

                if (AtEnd || !IsDigit(_text[_pos]))
                {
                    throw Error("expected a digit");
                }

                if (_text[_pos] == '0')
                {
                    _pos++;
                    if (!AtEnd && IsDigit(_text[_pos]))
                    {
                        throw Error("leading zeros are not allowed in numbers");
                    }
                }
                else
                {
                    SkipDigits();
                }

                if (!AtEnd && _text[_pos] == '.')
                {
                    _pos++;
                    if (AtEnd || !IsDigit(_text[_pos]))
                    {
                        throw Error("expected a digit after '.'");
                    }

                    SkipDigits();
                }

                if (!AtEnd && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    _pos++;
                    if (!AtEnd && (_text[_pos] == '+' || _text[_pos] == '-'))
                    {
                        _pos++;
                    }

                    if (AtEnd || !IsDigit(_text[_pos]))
                    {
                        throw Error("expected a digit in the exponent");
                    }

                    SkipDigits();
                }

                return JsonValue.Scalar(JsonKind.Number, _text.Substring(start, _pos - start), line, column);
            }

            private JsonValue ParseLiteral(string literal, JsonKind kind)
            {
                int line = _line;
                int column = Column;
                if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                {
                    throw Error($"unexpected '{Printable(_text[_pos])}'; expected a value");
                }

                _pos += literal.Length;
                return JsonValue.Scalar(kind, literal, line, column);
            }

            private void SkipDigits()
            {
                while (!AtEnd && IsDigit(_text[_pos]))
                {
                    _pos++;
                }
            }

            private void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    char c = _text[_pos];
                    if (c == '\n')
                    {
                        _pos++;
                        _line++;
                        _lineStart = _pos;
                    }
                    else if (c == ' ' || c == '\t' || c == '\r')
                    {
                        _pos++;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            private void Expect(char expected, string message)
            {
                if (AtEnd || _text[_pos] != expected)
                {
                    throw Error(message);
                }

                _pos++;
            }

            private ContentException Error(string message) => ErrorAt(_line, Column, message);

            private ContentException ErrorAt(int line, int column, string message) =>
                new ContentException(_name + ":" + line.ToString(CultureInfo.InvariantCulture) + ":" +
                                     column.ToString(CultureInfo.InvariantCulture) + ": " + message);

            // Unicode-aware char.IsDigit would accept other scripts' digits; JSON allows only ASCII.
            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private static int HexValue(char c) =>
                c >= '0' && c <= '9' ? c - '0' :
                c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

            private static string Printable(char c) =>
                c < ' ' ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : c.ToString();
        }
    }
}
