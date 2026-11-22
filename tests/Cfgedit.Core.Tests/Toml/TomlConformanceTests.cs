using System.Globalization;
using System.Text.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Core.Toml;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The toml-test suite (v2.2.0, the TOML 1.0.0 selection), copied into Fixtures/toml-test. Every valid case parses byte for byte
/// through the syntax layer, and through the format, where Tomlyn must agree; its data must equal the case's expected JSON. Every
/// invalid case is refused by the format with cfgedit's own message, not by the Tomlyn cross-check. The top-level values of the valid
/// cases that have only top-level key/value pairs are also compared with their expected JSON, through the syntax tree.
/// </summary>
public sealed class TomlConformanceTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "toml-test");

    public static IEnumerable<object[]> ValidCases()
    {
        return Cases("valid");
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        return Cases("invalid");
    }

    /// <summary>The valid cases whose documents hold only top-level key/value pairs, each with a single-part key.</summary>
    public static IEnumerable<object[]> ValueCases()
    {
        foreach (object[] row in ValidCases())
        {
            string relative = (string)row[0];
            TomlDocument? doc = TryParseFile(relative);
            if (doc is not null && IsTopLevelOnly(doc))
            {
                yield return [relative];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_case_parses_and_round_trips_byte_for_byte(string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(FixtureRoot, relative));
        SourceText source = SourceText.Decode(bytes, relative);
        TomlDocument doc = TomlDocument.Parse(source);

        TomlTestSupport.AssertTiles(doc);
        Assert.Equal(source.Text, TomlTestSupport.Reassemble(doc));
        Assert.Equal(bytes, source.Encode());
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Valid_case_is_accepted_by_the_format_and_its_data_matches_the_expected_json(string relative)
    {
        var format = new TomlFormat();
        ConfigDocument document = format.Parse(Decode(relative));
        LogicalNode data = format.ToLogical(document).Children[0];

        using System.Text.Json.JsonDocument expected = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(Path.Combine(FixtureRoot, relative), ".json")));
        AssertSameData(ExpectedObject(expected.RootElement, null), data, "$");
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_case_is_refused_by_the_format_with_cfgedit_s_own_message(string relative)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => new TomlFormat().Parse(Decode(relative)));
        Assert.False(ex.Message.StartsWith("internal parse mismatch", StringComparison.Ordinal), ex.Message);
    }

    [Theory]
    [MemberData(nameof(ValueCases))]
    public void Top_level_values_match_the_expected_json(string relative)
    {
        TomlDocument doc = Assert.IsType<TomlDocument>(TryParseFile(relative));
        string full = Path.Combine(FixtureRoot, relative);
        using System.Text.Json.JsonDocument expected = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(full, ".json")));
        JsonElement root = expected.RootElement;

        List<TomlKeyValueItem> items = doc.Items.OfType<TomlKeyValueItem>().ToList();
        Assert.Equal(root.EnumerateObject().Count(), items.Count);
        foreach (TomlKeyValueItem item in items)
        {
            string name = item.Key.Parts[0].Name;
            Assert.True(root.TryGetProperty(name, out JsonElement element), $"no expected value for {name}");
            AssertMatches(item.Value, element, name);
        }
    }

    private static IEnumerable<object[]> Cases(string directory)
    {
        return Directory.EnumerateFiles(Path.Combine(FixtureRoot, directory), "*.toml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(FixtureRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { path });
    }

    private static SourceText Decode(string relative)
    {
        return SourceText.Decode(File.ReadAllBytes(Path.Combine(FixtureRoot, relative)), relative);
    }

    /// <summary>
    /// The logical data of an expected JSON object, as the format's data is written: a plain object is an object, and an object of a
    /// <c>type</c> and a <c>value</c>, both strings, is a tagged scalar. The canonical texts come from the same definitions as the
    /// format's, so the date-times are compared as the canonical form (see <see cref="TomlValues"/>).
    /// </summary>
    private static LogicalNode ExpectedObject(JsonElement element, string? name)
    {
        var children = new List<LogicalNode>();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            children.Add(Expected(property.Value, property.Name));
        }

        return new LogicalNode(LogicalKind.Object, name, null, null, [], children);
    }

    private static LogicalNode Expected(JsonElement element, string? name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var items = new List<LogicalNode>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(Expected(item, null));
                }

                return new LogicalNode(LogicalKind.Array, name, null, null, [], items);
            }

            case JsonValueKind.Object when IsTaggedScalar(element):
                return ExpectedScalar(name, element.GetProperty("type").GetString()!, element.GetProperty("value").GetString()!);

            case JsonValueKind.Object:
                return ExpectedObject(element, name);

            default:
                throw new InvalidOperationException("Unexpected JSON value kind in the expected data: " + element.ValueKind);
        }
    }

    private static LogicalNode ExpectedScalar(string? name, string type, string text)
    {
        (TomlValueKind kind, string value) = type switch
        {
            "string" => (TomlValueKind.String, text),
            "integer" => (TomlValueKind.Integer, long.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            "bool" => (TomlValueKind.Boolean, text),
            "float" => (TomlValueKind.Float, TomlValues.FloatText(ExpectedFloat(text))),
            "datetime" => (TomlValueKind.OffsetDateTime, TomlValues.DateTimeText(text)),
            "datetime-local" => (TomlValueKind.LocalDateTime, TomlValues.DateTimeText(text)),
            "date-local" => (TomlValueKind.LocalDate, TomlValues.DateTimeText(text)),
            "time-local" => (TomlValueKind.LocalTime, TomlValues.DateTimeText(text)),
            _ => throw new InvalidOperationException("Unknown expected type: " + type),
        };
        return new LogicalNode(LogicalKind.Scalar, name, null, value, [], [], tomlKind: kind);
    }

    private static double ExpectedFloat(string text)
    {
        return text switch
        {
            "nan" or "+nan" or "-nan" => double.NaN,
            "inf" or "+inf" => double.PositiveInfinity,
            "-inf" => double.NegativeInfinity,
            _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// The value to compare for a node. toml-test writes the fraction of a date-time to milliseconds (<c>56.600</c> for a source
    /// <c>56.6</c>), where cfgedit keeps the digits as written, so trailing zeros of a fraction are not compared. Every other digit is.
    /// The exact comparison is the Tomlyn cross-check's.
    /// </summary>
    private static string? ForgiveFraction(LogicalNode node)
    {
        if (node.TomlKind is not (TomlValueKind.OffsetDateTime or TomlValueKind.LocalDateTime or TomlValueKind.LocalTime) || node.Value is null)
        {
            return node.Value;
        }

        int dot = node.Value.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return node.Value;
        }

        int end = dot + 1;
        while (end < node.Value.Length && char.IsAsciiDigit(node.Value[end]))
        {
            end++;
        }

        string fraction = node.Value[(dot + 1)..end].TrimEnd('0');
        return node.Value[..dot] + (fraction.Length > 0 ? "." + fraction : string.Empty) + node.Value[end..];
    }

    /// <summary>
    /// Asserts that the data equals the expected data: the same kinds, values and member names, and the same items in the same order.
    /// Tables are compared by name, since TOML tables have no order.
    /// </summary>
    private static void AssertSameData(LogicalNode expected, LogicalNode actual, string path)
    {
        Assert.True(
            expected.Kind == actual.Kind && expected.TomlKind == actual.TomlKind && ForgiveFraction(expected) == ForgiveFraction(actual),
            $"{path}: expected {expected} but found {actual}");
        if (expected.Kind == LogicalKind.Object)
        {
            Assert.Equal(expected.Children.Count, actual.Children.Count);
            foreach (LogicalNode child in expected.Children)
            {
                LogicalNode match = Assert.Single(actual.Children, other => other.Name == child.Name);
                AssertSameData(child, match, $"{path}.{child.Name}");
            }
        }
        else if (expected.Kind == LogicalKind.Array)
        {
            Assert.Equal(expected.Children.Count, actual.Children.Count);
            for (int i = 0; i < expected.Children.Count; i++)
            {
                AssertSameData(expected.Children[i], actual.Children[i], $"{path}[{i}]");
            }
        }
    }

    private static TomlDocument? TryParseFile(string relative)
    {
        try
        {
            return TomlDocument.Parse(SourceText.Decode(File.ReadAllBytes(Path.Combine(FixtureRoot, relative)), relative));
        }
        catch (CfgeditException)
        {
            return null;
        }
    }

    private static bool IsTopLevelOnly(TomlDocument doc)
    {
        return doc.Items.All(item => item is TomlBlankItem or TomlCommentItem
            || (item is TomlKeyValueItem kv && kv.Key.Parts.Count == 1));
    }

    private static void AssertMatches(TomlValue value, JsonElement expected, string path)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Array:
                {
                    TomlArray array = Assert.IsType<TomlArray>(value);
                    List<TomlValue> items = array.Entries.OfType<TomlArrayItem>().Select(item => item.Value).ToList();
                    Assert.Equal(expected.GetArrayLength(), items.Count);
                    int index = 0;
                    foreach (JsonElement element in expected.EnumerateArray())
                    {
                        AssertMatches(items[index], element, $"{path}[{index}]");
                        index++;
                    }

                    return;
                }

            case JsonValueKind.Object when IsTaggedScalar(expected):
                AssertScalar(value, expected.GetProperty("type").GetString()!, expected.GetProperty("value").GetString()!, path);
                return;

            case JsonValueKind.Object:
                {
                    TomlInlineTable table = Assert.IsType<TomlInlineTable>(value);
                    var members = table.Entries.OfType<TomlInlineMember>()
                        .Select(m => (Path: (IReadOnlyList<TomlKeyPart>)m.Key.Parts, m.Value))
                        .ToList();
                    AssertObject(members, expected, path);
                    return;
                }

            default:
                Assert.Fail($"{path}: unexpected JSON value kind {expected.ValueKind}");
                return;
        }
    }

    /// <summary>
    /// Compares the members of a table with an expected object. A dotted key (<c>a.b = 1</c>) is a member of the table <c>a</c>,
    /// so members are grouped by their first key part, and the rest of each dotted key is compared inside that group.
    /// </summary>
    private static void AssertObject(List<(IReadOnlyList<TomlKeyPart> Path, TomlValue Value)> members, JsonElement expected, string path)
    {
        var groups = members.GroupBy(m => m.Path[0].Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Assert.Equal(expected.EnumerateObject().Count(), groups.Count);
        foreach (JsonProperty property in expected.EnumerateObject())
        {
            Assert.True(groups.TryGetValue(property.Name, out List<(IReadOnlyList<TomlKeyPart> Path, TomlValue Value)>? group), $"{path}.{property.Name} is missing");
            string childPath = $"{path}.{property.Name}";
            if (group!.Count == 1 && group[0].Path.Count == 1)
            {
                AssertMatches(group[0].Value, property.Value, childPath);
                continue;
            }

            Assert.Equal(JsonValueKind.Object, property.Value.ValueKind);
            Assert.All(group, m => Assert.True(m.Path.Count > 1, $"{childPath} is defined both as a value and by dotted keys"));
            AssertObject(group.Select(m => (m.Path.Skip(1).ToList() as IReadOnlyList<TomlKeyPart>, m.Value)).ToList(), property.Value, childPath);
        }
    }

    private static bool IsTaggedScalar(JsonElement expected)
    {
        return expected.EnumerateObject().Count() == 2
            && expected.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && expected.TryGetProperty("value", out JsonElement value)
            && value.ValueKind == JsonValueKind.String;
    }

    private static void AssertScalar(TomlValue value, string type, string text, string path)
    {
        switch (type)
        {
            case "string":
                Assert.Equal(text, Assert.IsType<TomlString>(value).Value);
                break;

            case "integer":
                Assert.Equal(long.Parse(text, CultureInfo.InvariantCulture), Assert.IsType<TomlInteger>(value).Value);
                break;

            case "bool":
                Assert.Equal(text == "true", Assert.IsType<TomlBoolean>(value).Value);
                break;

            case "float":
                {
                    double actual = Assert.IsType<TomlFloat>(value).Value;
                    if (text is "nan" or "+nan" or "-nan")
                    {
                        Assert.True(double.IsNaN(actual), path);
                    }
                    else if (text is "inf" or "+inf")
                    {
                        Assert.Equal(double.PositiveInfinity, actual);
                    }
                    else if (text == "-inf")
                    {
                        Assert.Equal(double.NegativeInfinity, actual);
                    }
                    else
                    {
                        Assert.Equal(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), actual);
                    }

                    break;
                }

            case "datetime":
                AssertDateTime(value, TomlDateTimeKind.OffsetDateTime, text, path);
                break;

            case "datetime-local":
                AssertDateTime(value, TomlDateTimeKind.LocalDateTime, text, path);
                break;

            case "date-local":
                AssertDateTime(value, TomlDateTimeKind.LocalDate, text, path);
                break;

            case "time-local":
                AssertDateTime(value, TomlDateTimeKind.LocalTime, text, path);
                break;

            default:
                Assert.Fail($"{path}: unknown type {type}");
                break;
        }
    }

    /// <summary>
    /// Compares a date-time with the expected text in the form toml-test writes: T for the separator, Z in upper case, and the
    /// fraction without trailing zeros. The source text is not normalized, so only these differences are forgiven.
    /// </summary>
    private static void AssertDateTime(TomlValue value, TomlDateTimeKind kind, string expected, string path)
    {
        TomlDateTime dateTime = Assert.IsType<TomlDateTime>(value);
        Assert.Equal(kind, dateTime.Kind);
        Assert.Equal(Normalize(expected), Normalize(dateTime.Raw));
    }

    private static string Normalize(string text)
    {
        string result = text.Replace('z', 'Z');
        bool hasDate = result.Length > 10 && result[4] == '-';
        if (hasDate && result[10] is ' ' or 't')
        {
            result = result[..10] + "T" + result[11..];
        }

        int dot = result.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            int end = dot + 1;
            while (end < result.Length && char.IsAsciiDigit(result[end]))
            {
                end++;
            }

            string fraction = result[(dot + 1)..end].TrimEnd('0');
            result = result[..dot] + (fraction.Length > 0 ? "." + fraction : string.Empty) + result[end..];
        }

        return result;
    }
}
