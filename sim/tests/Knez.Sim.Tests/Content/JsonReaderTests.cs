using Knez.Sim.Content;
using Knez.Sim.Content.Json;

namespace Knez.Sim.Tests.Content;

[TestFixture]
public class JsonReaderTests
{
    private static JsonValue Parse(string text) => JsonReader.Parse(new SourceText("test.json", text));

    private static string ErrorOf(string text) => Assert.Throws<ContentException>(() => Parse(text))!.Message;

    [Test]
    public void Parse_Object_KeepsMembersInFileOrderWithPositions()
    {
        var root = Parse("{\n  \"b\": 1,\n  \"a\": [true, false, null],\n  \"c\": {}\n}");
        Assert.Multiple(() =>
        {
            Assert.That(root.Kind, Is.EqualTo(JsonKind.Object));
            Assert.That(root.Members.Select(m => m.Name), Is.EqualTo(new[] { "b", "a", "c" }));
            Assert.That(root.Get("a")!.Items.Select(i => i.Kind),
                Is.EqualTo(new[] { JsonKind.Boolean, JsonKind.Boolean, JsonKind.Null }));
            Assert.That(root.Get("a")!.Items[1].Text, Is.EqualTo("false"));
            Assert.That(root.Get("c")!.Members, Is.Empty);
            Assert.That(root.Get("missing"), Is.Null);
            Assert.That((root.Get("b")!.Line, root.Get("b")!.Column), Is.EqualTo((2, 8)));
            Assert.That((root.Get("a")!.Line, root.Get("a")!.Column), Is.EqualTo((3, 8)));
        });
    }

    [TestCase("0")]
    [TestCase("-0.5")]
    [TestCase("1.250")]
    [TestCase("12e-3")]
    [TestCase("1E+9")]
    public void Parse_Number_KeepsTheSourceText(string number)
    {
        var value = Parse("[" + number + "]").Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(value.Kind, Is.EqualTo(JsonKind.Number));
            Assert.That(value.Text, Is.EqualTo(number));
        });
    }

    [Test]
    public void Parse_String_DecodesEscapes()
    {
        var value = Parse("\"a\\\"b\\\\c\\/d\\n\\t\\u00e9\\u0416\"");
        Assert.That(value.Text, Is.EqualTo("a\"b\\c/d\n\téЖ"));
    }

    [Test]
    public void Parse_ByteOrderMarkAndSurroundingWhitespace_AreSkipped()
    {
        Assert.That(Parse("\uFEFF \r\n\t[1] \n").Items, Has.Count.EqualTo(1));
    }

    [TestCase("", "1:1: the file is empty; expected a JSON value")]
    [TestCase("{\"a\": 1,}", "1:9: trailing comma before '}'")]
    [TestCase("[1, 2,]", "1:7: trailing comma before ']'")]
    [TestCase("{\"a\": 1 \"b\": 2}", "1:9: expected ',' or '}'")]
    [TestCase("{\"a\": 1, \"a\": 2}", "1:10: duplicate member 'a'")]
    [TestCase("{a: 1}", "1:2: expected a member name in double quotes")]
    [TestCase("[01]", "1:3: leading zeros are not allowed in numbers")]
    [TestCase("[1.]", "1:4: expected a digit after '.'")]
    [TestCase("[.5]", "1:2: unexpected '.'; expected a value")]
    [TestCase("[NaN]", "1:2: unexpected 'N'; expected a value")]
    [TestCase("[tru]", "1:2: unexpected 't'; expected a value")]
    [TestCase("\"abc", "1:5: unterminated string")]
    [TestCase("\"a\nb\"", "1:3: unterminated string (line break inside)")]
    [TestCase("\"\\x\"", "1:2: invalid escape '\\x'")]
    [TestCase("[1] [2]", "1:5: unexpected '[' after the end of the JSON value")]
    [TestCase("// comment\n{}", "1:1: unexpected '/'; expected a value")]
    [TestCase("{\n  \"a\": [1,\n  2", "3:4: unexpected end of file; expected ',' or ']'")]
    public void Parse_Malformed_ThrowsWithFileLineAndColumn(string text, string expected)
    {
        Assert.That(ErrorOf(text), Is.EqualTo("test.json:" + expected));
    }

    [Test]
    public void Parse_TooDeep_Throws()
    {
        string deep = new string('[', JsonReader.MaxDepth + 2) + new string(']', JsonReader.MaxDepth + 2);
        Assert.That(ErrorOf(deep), Does.Contain("nesting deeper than"));
    }
}
