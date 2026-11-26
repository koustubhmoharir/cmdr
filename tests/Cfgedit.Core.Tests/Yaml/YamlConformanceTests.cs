using System.Text;
using Cfgedit.Core.Yaml;
using Shared.Core;
using Xunit.Abstractions;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The yaml-test-suite (data-2022-01-17, MIT), copied into Fixtures/yaml-test-suite. Every case marked as an error is refused. Every
/// other case is either refused, with the message counted, or accepted and round-trips byte for byte. The summary reports the totals
/// and the refusal messages, and compares the scalar values of accepted single-document cases with their <c>test.event</c> lines.
/// </summary>
public sealed class YamlConformanceTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "yaml-test-suite");

    private readonly ITestOutputHelper _output;

    public YamlConformanceTests(ITestOutputHelper output)
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
    public void Error_case_is_refused(string id)
    {
        SourceText source = Load(id);
        Assert.Throws<CfgeditException>(() => YamlDocument.Parse(source));
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Non_error_case_is_refused_or_round_trips_byte_for_byte(string id)
    {
        SourceText source = Load(id);
        YamlDocument doc;
        try
        {
            doc = YamlDocument.Parse(source);
        }
        catch (CfgeditException)
        {
            return;
        }

        YamlTestSupport.AssertTiles(doc);
        Assert.Equal(source.Text, YamlTestSupport.Reassemble(doc));
        Assert.Equal(File.ReadAllBytes(Path.Combine(FixtureRoot, id, "in.yaml")), source.Encode());
    }

    [Fact]
    public void Summary_reports_refusals_and_value_coverage()
    {
        var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
        var roundTripFailures = new List<string>();
        var valueMismatches = new List<string>();
        int total = 0;
        int errorTotal = 0;
        int errorRefused = 0;
        int errorAccepted = 0;
        int validTotal = 0;
        int accepted = 0;
        int refused = 0;
        int compared = 0;
        int singleDocumentAccepted = 0;

        foreach (string id in AllIds())
        {
            total++;
            bool isError = File.Exists(Path.Combine(FixtureRoot, id, "error"));
            SourceText source = Load(id);
            YamlDocument? doc = null;
            string? message = null;
            try
            {
                doc = YamlDocument.Parse(source);
            }
            catch (CfgeditException ex)
            {
                message = ex.Message;
            }

            if (isError)
            {
                errorTotal++;
                if (doc is null)
                {
                    errorRefused++;
                }
                else
                {
                    errorAccepted++;
                }

                continue;
            }

            validTotal++;
            if (doc is null)
            {
                refused++;
                refusals[message!] = refusals.GetValueOrDefault(message!) + 1;
                continue;
            }

            accepted++;
            if (!string.Equals(YamlTestSupport.Reassemble(doc), source.Text, StringComparison.Ordinal)
                || !source.Encode().SequenceEqual(File.ReadAllBytes(Path.Combine(FixtureRoot, id, "in.yaml"))))
            {
                roundTripFailures.Add(id);
            }

            List<string>? expected = ExpectedValues(Path.Combine(FixtureRoot, id, "test.event"));
            if (expected is null)
            {
                continue;
            }

            singleDocumentAccepted++;
            compared++;
            var actual = new List<string>();
            CollectValues(doc.Documents.Count == 1 ? doc.Documents[0].Root : null, actual);
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                valueMismatches.Add(id);
            }
        }

        _output.WriteLine($"cases: {total} (error {errorTotal}, not error {validTotal})");
        _output.WriteLine($"error cases refused: {errorRefused} of {errorTotal}; accepted: {errorAccepted}");
        _output.WriteLine($"not-error cases accepted: {accepted}; refused: {refused}");
        _output.WriteLine($"accepted cases that do not round-trip: {roundTripFailures.Count}");
        _output.WriteLine($"accepted single-document cases with a test.event: {singleDocumentAccepted}; value-compared: {compared}; mismatches: {valueMismatches.Count}");
        _output.WriteLine("refusal messages for not-error cases:");
        foreach (KeyValuePair<string, int> pair in refusals.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            _output.WriteLine($"  {pair.Value,4}  {pair.Key}");
        }

        _output.WriteLine("value mismatches: " + string.Join(", ", valueMismatches));
        _output.WriteLine("round-trip failures: " + string.Join(", ", roundTripFailures));

        Assert.Equal(errorTotal, errorRefused);
        Assert.Empty(roundTripFailures);
        Assert.Equal(validTotal, accepted + refused);
        Assert.Empty(valueMismatches);
    }

    /// <summary>The scalar values of a single-document test.event, in order, or null when the case has other than one document.</summary>
    private static List<string>? ExpectedValues(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var values = new List<string>();
        int documents = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (line == "+DOC" || line.StartsWith("+DOC ", StringComparison.Ordinal))
            {
                documents++;
            }

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

        return documents == 1 ? values : null;
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

    /// <summary>The scalars of a document in source order: keys and values, with an empty value as the empty string. Aliases are not scalars.</summary>
    private static void CollectValues(YamlValueNode? node, List<string> values)
    {
        switch (node)
        {
            case null:
                values.Add(string.Empty);
                break;
            case YamlAlias:
                break;
            case YamlScalar scalar:
                values.Add(scalar.Value);
                break;
            case YamlBlockMapping mapping:
                foreach (YamlMappingEntry entry in mapping.Entries)
                {
                    CollectValues(entry.Key, values);
                    CollectValues(entry.Value, values);
                }

                break;
            case YamlFlowMapping flow:
                foreach (YamlFlowMappingEntry entry in flow.Entries)
                {
                    CollectValues(entry.Key, values);
                    CollectValues(entry.Value, values);
                }

                break;
            case YamlBlockSequence sequence:
                foreach (YamlSequenceItem item in sequence.Items)
                {
                    CollectValues(item.Value, values);
                }

                break;
            case YamlFlowSequence flowSequence:
                foreach (YamlValueNode item in flowSequence.Items)
                {
                    CollectValues(item, values);
                }

                break;
        }
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
