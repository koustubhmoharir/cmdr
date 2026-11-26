using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Yaml;
using Shared.Core;
using Xunit.Abstractions;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The yaml-test-suite (data-2022-01-17, MIT) through the format. Every case marked as an error is refused by cfgedit itself, not by the
/// YamlDotNet cross-check. Every other case is refused with its message counted, or parsed, and then its data is the case's expected data:
/// its <c>in.json</c> (each JSON value is one document, with the YAML scalars read by the core schema and aliases resolved), or, for a case
/// whose tags or anchors JSON cannot show, its <c>test.event</c> scalars in order. A case where cfgedit and YamlDotNet disagree fails.
/// </summary>
public sealed class YamlModelConformanceTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "yaml-test-suite");

    private static readonly YamlFormat Format = new();

    private readonly ITestOutputHelper _output;

    public YamlModelConformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> ErrorCases()
    {
        return Cases(isError: true);
    }

    public static IEnumerable<object[]> ValidCases()
    {
        return Cases(isError: false);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void Error_case_is_refused_by_cfgedit(string id)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Format.Parse(Load(id)));
        Assert.DoesNotContain("internal parse mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Non_error_case_is_refused_or_its_data_is_the_expected_data(string id)
    {
        ConfigDocument document;
        try
        {
            document = Format.Parse(Load(id));
        }
        catch (CfgeditException ex)
        {
            Assert.DoesNotContain("internal parse mismatch", ex.Message, StringComparison.Ordinal);
            return;
        }

        Assert.Null(Compare(id, Format.ToLogical(document)));
    }

    [Fact]
    public void Summary_reports_refusals_and_data_coverage()
    {
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var mismatches = new List<string>();
        var dataFailures = new List<string>();
        int total = 0;
        int errorTotal = 0;
        int errorRefused = 0;
        int validTotal = 0;
        int accepted = 0;
        int refused = 0;
        int compared = 0;
        int comparedByEvents = 0;

        foreach (string id in AllIds())
        {
            total++;
            bool isError = File.Exists(Path.Combine(FixtureRoot, id, "error"));
            ConfigDocument? document = null;
            string? message = null;
            try
            {
                document = Format.Parse(Load(id));
            }
            catch (CfgeditException ex)
            {
                message = ex.Message;
            }

            if (isError)
            {
                errorTotal++;
                if (document is null && message is not null && !message.StartsWith("internal parse mismatch", StringComparison.Ordinal))
                {
                    errorRefused++;
                }
                else
                {
                    dataFailures.Add(id + " (error case not refused by cfgedit)");
                }

                continue;
            }

            validTotal++;
            if (document is null)
            {
                if (message!.StartsWith("internal parse mismatch", StringComparison.Ordinal))
                {
                    mismatches.Add(id + ": " + message);
                }
                else
                {
                    refused++;
                    refusals[message] = refusals.GetValueOrDefault(message) + 1;
                }

                continue;
            }

            accepted++;
            bool byEvents = UsesProperties(Path.Combine(FixtureRoot, id, "test.event"));
            comparedByEvents += byEvents ? 1 : 0;
            compared++;
            string? difference = Compare(id, Format.ToLogical(document));
            if (difference is not null)
            {
                dataFailures.Add(id + ": " + difference);
            }
        }

        _output.WriteLine($"cases: {total} (error {errorTotal}, not error {validTotal})");
        _output.WriteLine($"error cases refused by cfgedit: {errorRefused} of {errorTotal}");
        _output.WriteLine($"not-error cases accepted: {accepted}; refused by cfgedit: {refused}; YamlDotNet mismatches: {mismatches.Count}");
        _output.WriteLine($"accepted cases whose data was compared: {compared} (by test.event, for tags and anchors: {comparedByEvents})");
        _output.WriteLine($"accepted cases whose data differs: {dataFailures.Count}");
        _output.WriteLine("refusal messages for not-error cases:");
        foreach (KeyValuePair<string, int> pair in refusals.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"  {pair.Value,4}  {pair.Key}");
        }

        _output.WriteLine("YamlDotNet mismatches: " + string.Join("; ", mismatches));
        _output.WriteLine("data failures: " + string.Join("; ", dataFailures));

        Assert.Equal(errorTotal, errorRefused);
        Assert.Empty(mismatches);
        Assert.Empty(dataFailures);
        Assert.Equal(validTotal, accepted + refused);
    }

    /// <summary>The difference between a case's data and its expected data, or null when they agree.</summary>
    /// <param name="id">The case.</param>
    /// <param name="logical">The document's logical tree: its data, then its comments.</param>
    private static string? Compare(string id, LogicalNode logical)
    {
        LogicalNode data = logical.Children[0];
        string path = Path.Combine(FixtureRoot, id);
        SourceText source = Load(id);
        int documents = YamlDocuments.Counted(YamlDocument.Parse(source)).Count;
        string eventPath = Path.Combine(path, "test.event");
        if (UsesProperties(eventPath) || !File.Exists(Path.Combine(path, "in.json")))
        {
            List<string>? expected = ExpectedEventValues(eventPath);
            if (expected is null)
            {
                return "no test.event to compare with";
            }

            var actual = new List<string>();
            CollectEventValues(documents == 0 ? null : data, documents, actual);
            return actual.SequenceEqual(expected, StringComparer.Ordinal) ? null : "scalars differ from test.event";
        }

        List<object?> expectedValues = ReadJsonValues(File.ReadAllBytes(Path.Combine(path, "in.json")));
        var actualValues = new List<object?>();
        var anchors = new Dictionary<string, LogicalNode>(StringComparer.Ordinal);
        if (documents == 1)
        {
            actualValues.Add(ToJson(data, anchors));
        }
        else if (documents > 1)
        {
            foreach (LogicalNode root in data.Children)
            {
                actualValues.Add(ToJson(root, anchors));
            }
        }

        if (actualValues.Count != expectedValues.Count)
        {
            return $"{actualValues.Count} documents, expected {expectedValues.Count}";
        }

        for (int i = 0; i < actualValues.Count; i++)
        {
            if (!SameJson(actualValues[i], expectedValues[i]))
            {
                return $"document {i} differs from in.json";
            }
        }

        return null;
    }

    /// <summary>True when the event file shows tags or anchors, which the JSON oracle cannot express.</summary>
    private static bool UsesProperties(string eventPath)
    {
        if (!File.Exists(eventPath))
        {
            return false;
        }

        return File.ReadLines(eventPath).Any(line => line.Contains(" &", StringComparison.Ordinal) || line.Contains(" <", StringComparison.Ordinal)
            || line.Contains(" !", StringComparison.Ordinal));
    }

    /// <summary>The data's scalars in order, keys and values, as test.event lists them. Aliases are not scalars there.</summary>
    private static void CollectEventValues(LogicalNode? data, int documents, List<string> values)
    {
        if (data is null)
        {
            return;
        }

        IEnumerable<LogicalNode> roots = documents == 1 ? [data] : data.Children;
        foreach (LogicalNode root in roots)
        {
            CollectNode(root, values);
        }
    }

    private static void CollectNode(LogicalNode node, List<string> values)
    {
        switch (node.Kind)
        {
            case LogicalKind.Object:
                foreach (LogicalNode child in node.Children)
                {
                    values.Add(child.Name!);
                    CollectNode(child, values);
                }

                break;
            case LogicalKind.Array:
                foreach (LogicalNode child in node.Children)
                {
                    CollectNode(child, values);
                }

                break;
            case LogicalKind.Scalar:
                values.Add(node.Value!);
                break;
        }
    }

    /// <summary>
    /// The JSON value of a logical node: objects are dictionaries, arrays are lists, numbers are integers (BigInteger) or floats (double),
    /// and booleans, nulls and strings are what JSON has. An alias is the value of the node it names.
    /// </summary>
    private static object? ToJson(LogicalNode node, Dictionary<string, LogicalNode> anchors)
    {
        object? value;
        switch (node.Kind)
        {
            case LogicalKind.Alias:
                return ToJson(anchors[node.Value!], anchors);
            case LogicalKind.Object:
            {
                var members = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (LogicalNode child in node.Children)
                {
                    members[child.Name!] = ToJson(child, anchors);
                }

                value = members;
                break;
            }

            case LogicalKind.Array:
            {
                var items = new List<object?>();
                foreach (LogicalNode child in node.Children)
                {
                    items.Add(ToJson(child, anchors));
                }

                value = items;
                break;
            }

            default:
                value = Scalar(node);
                break;
        }

        if (node.YamlAnchor is { } name)
        {
            anchors[name] = node;
        }

        return value;
    }

    private static object? Scalar(LogicalNode node)
    {
        string text = node.Value!;
        switch (node.YamlKind)
        {
            case YamlValueKind.Null:
                return null;
            case YamlValueKind.Boolean:
                return text.Equals("true", StringComparison.OrdinalIgnoreCase);
            case YamlValueKind.Integer:
                return ParseInteger(text);
            case YamlValueKind.Float:
                return ParseFloat(text);
            default:
                return text;
        }
    }

    private static BigInteger ParseInteger(string text)
    {
        if (text.StartsWith("0x", StringComparison.Ordinal))
        {
            return BigInteger.Parse("0" + text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        if (text.StartsWith("0o", StringComparison.Ordinal))
        {
            BigInteger result = BigInteger.Zero;
            foreach (char digit in text[2..])
            {
                result = (result * 8) + (digit - '0');
            }

            return result;
        }

        return BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static double ParseFloat(string text)
    {
        return text switch
        {
            ".inf" or "+.inf" => double.PositiveInfinity,
            "-.inf" => double.NegativeInfinity,
            ".nan" or ".NaN" or ".NAN" => double.NaN,
            _ => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        };
    }

    private static bool SameJson(object? actual, object? expected)
    {
        switch (actual)
        {
            case null:
                return expected is null;
            case string text:
                return expected is string expectedText && text == expectedText;
            case bool flag:
                return expected is bool expectedFlag && flag == expectedFlag;
            case BigInteger integer:
                return expected switch
                {
                    BigInteger expectedInteger => integer == expectedInteger,
                    double expectedNumber => !double.IsInfinity(expectedNumber) && (double)integer == expectedNumber,
                    _ => false,
                };
            case double number:
                return expected switch
                {
                    double expectedNumber => number.Equals(expectedNumber),
                    BigInteger expectedInteger => (double)expectedInteger == number,
                    _ => false,
                };
            case Dictionary<string, object?> members:
            {
                if (expected is not Dictionary<string, object?> expectedMembers || members.Count != expectedMembers.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, object?> pair in members)
                {
                    if (!expectedMembers.TryGetValue(pair.Key, out object? expectedMember) || !SameJson(pair.Value, expectedMember))
                    {
                        return false;
                    }
                }

                return true;
            }

            case List<object?> items:
            {
                if (expected is not List<object?> expectedItems || items.Count != expectedItems.Count)
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    if (!SameJson(items[i], expectedItems[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>Every JSON value of an in.json file, in order: one per document.</summary>
    private static List<object?> ReadJsonValues(byte[] bytes)
    {
        var values = new List<object?>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            values.Add(FromJson(document.RootElement));
        }

        return values;
    }

    private static object? FromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
            {
                var members = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    members[property.Name] = FromJson(property.Value);
                }

                return members;
            }

            case System.Text.Json.JsonValueKind.Array:
                return element.EnumerateArray().Select(FromJson).ToList();
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.True:
                return true;
            case System.Text.Json.JsonValueKind.False:
                return false;
            case System.Text.Json.JsonValueKind.Number:
            {
                string raw = element.GetRawText();
                if (raw.IndexOfAny(['.', 'e', 'E']) >= 0)
                {
                    return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                return BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }

            default:
                return null;
        }
    }

    /// <summary>The scalar values of a test.event file, in order, or null when it is missing.</summary>
    private static List<string>? ExpectedEventValues(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var values = new List<string>();
        foreach (string line in File.ReadLines(path))
        {
            if (!line.StartsWith("=VAL ", StringComparison.Ordinal))
            {
                continue;
            }

            string rest = line[5..];
            while (rest.Length > 0 && (rest[0] == '&' || rest[0] == '<'))
            {
                int space = rest.IndexOf(' ');
                rest = space < 0 ? string.Empty : rest[(space + 1)..];
            }

            values.Add(rest.Length == 0 ? string.Empty : Unescape(rest[1..]));
        }

        return values;
    }

    /// <summary>The event escapes: a backslash, a line feed, a tab, a carriage return, a backspace and a NUL.</summary>
    private static string Unescape(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                sb.Append(c);
                continue;
            }

            char e = text[++i];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                default: sb.Append('\\').Append(e); break;
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<object[]> Cases(bool isError)
    {
        return AllIds().Where(id => File.Exists(Path.Combine(FixtureRoot, id, "error")) == isError).Select(id => new object[] { id });
    }

    /// <summary>Every case: a directory with an in.yaml, at the top or as a numbered variant, by its relative path.</summary>
    private static List<string> AllIds()
    {
        return Directory.EnumerateFiles(FixtureRoot, "in.yaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(FixtureRoot, Path.GetDirectoryName(path)!).Replace('\\', '/'))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static SourceText Load(string id)
    {
        return SourceText.Decode(File.ReadAllBytes(Path.Combine(FixtureRoot, id, "in.yaml")), id);
    }
}
