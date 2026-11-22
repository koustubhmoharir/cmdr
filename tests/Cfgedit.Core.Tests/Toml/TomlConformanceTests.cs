using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cfgedit.Core.Text;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The toml-test suite (v2.2.0, the TOML 1.0.0 selection), copied into Fixtures/toml-test. Every valid case parses and round-trips
/// byte for byte. Every invalid case is refused, or is one of the semantic cases listed in semantic-invalid.txt. The top-level
/// values of the valid cases that have only top-level key/value pairs are compared with their expected JSON.
/// </summary>
public sealed class TomlConformanceTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "toml-test");
    private static readonly HashSet<string> SemanticInvalid = LoadSemanticList();

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
    [MemberData(nameof(InvalidCases))]
    public void Invalid_case_is_refused_or_is_a_listed_semantic_case(string relative)
    {
        bool refused = ParseRefused(relative, out _);
        if (!refused)
        {
            Assert.Contains(relative, SemanticInvalid);
        }
    }

    [Fact]
    public void Every_listed_semantic_case_is_an_invalid_case_that_the_parser_accepts()
    {
        Assert.NotEmpty(SemanticInvalid);
        foreach (string entry in SemanticInvalid)
        {
            Assert.StartsWith("invalid/", entry, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(FixtureRoot, entry)), $"{entry} is not in the fixtures");
            Assert.False(ParseRefused(entry, out _), $"{entry} is listed but the parser refuses it, so it need not be listed");
        }
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

    /// <summary>
    /// The semantic cases, one relative path per line. The file sits next to this class. It is found from this source file's
    /// location, so the test reads the checked-in list and not a copy.
    /// </summary>
    private static HashSet<string> LoadSemanticList([CallerFilePath] string sourcePath = "")
    {
        string listPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "semantic-invalid.txt");
        return File.ReadAllLines(listPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>True when the parser refuses the case, with the message. A case that cannot be decoded is refused too.</summary>
    private static bool ParseRefused(string relative, out string? message)
    {
        try
        {
            SourceText source = SourceText.Decode(File.ReadAllBytes(Path.Combine(FixtureRoot, relative)), relative);
            TomlDocument.Parse(source);
            message = null;
            return false;
        }
        catch (CfgeditException ex)
        {
            message = ex.Message;
            return true;
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
