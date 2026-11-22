using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using Tomlyn;
using TomlyArray = Tomlyn.Model.TomlArray;
using TomlyDateTime = Tomlyn.TomlDateTime;
using TomlyDateTimeKind = Tomlyn.TomlDateTimeKind;
using TomlyTable = Tomlyn.Model.TomlTable;
using TomlyTableArray = Tomlyn.Model.TomlTableArray;

namespace Cfgedit.Core.Toml;

/// <summary>
/// The TOML cross-check: the text is parsed again by Tomlyn, an independent parser, and its data is converted to the logical shape
/// of cfgedit's data (see <see cref="TomlModel.DataLogical"/>). The two must be equal. A disagreement is cfgedit's bug, never the
/// file's, so it is reported as an internal parse mismatch. Tables are compared by name, since a table's members have no order in
/// TOML; arrays are compared in order.
/// </summary>
internal static class TomlCrossCheck
{
    /// <summary>
    /// Tomlyn's nesting limit. cfgedit caps the depth of tables and the depth of values (arrays and inline tables) at
    /// <see cref="JsonDocument.MaxDepth"/> each, and Tomlyn counts both in one number, so its limit covers both at once. It counts a
    /// header's segments a level higher than cfgedit does, which the margin covers. cfgedit parses the text first, so the limit only
    /// matters for text that cfgedit has accepted.
    /// </summary>
    private static readonly TomlSerializerOptions Options = new() { MaxDepth = (2 * JsonDocument.MaxDepth) + 4 };

    /// <summary>Parses the text with Tomlyn and checks that its data is the given data.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">Tomlyn refuses the text, or its data differs from <paramref name="data"/>.</exception>
    internal static void Verify(string text, LogicalNode data)
    {
        TomlyTable? parsed;
        try
        {
            parsed = TomlSerializer.Deserialize<TomlyTable>(text, Options);
        }
        catch (TomlException ex)
        {
            throw Mismatch("Tomlyn refuses text that cfgedit accepts: " + ex.Message);
        }

        if (parsed is null)
        {
            throw Mismatch("Tomlyn returns no data for text that cfgedit accepts");
        }

        Compare(data, FromTomlyn(null, parsed));
    }

    /// <summary>Compares two data trees, and throws an internal parse mismatch that names the first difference.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">The trees differ.</exception>
    internal static void Compare(LogicalNode data, LogicalNode other)
    {
        string? difference = FirstDifference(data, other);
        if (difference is not null)
        {
            throw Mismatch(difference);
        }
    }

    /// <summary>The first difference between cfgedit's data and another data tree, described for a message, or null when they are equal.</summary>
    internal static string? FirstDifference(LogicalNode expected, LogicalNode actual)
    {
        return Diff(expected, actual, string.Empty);
    }

    /// <summary>The error for a disagreement. It names the difference and asks for a report.</summary>
    internal static CfgeditException Mismatch(string detail)
    {
        return new CfgeditException("internal parse mismatch: " + detail + " (please report this)");
    }

    /// <summary>
    /// Converts a Tomlyn value to the logical shape: a table is an object, an array of tables and an array are arrays, and a scalar
    /// has its kind and canonical value.
    /// </summary>
    internal static LogicalNode FromTomlyn(string? name, object value)
    {
        switch (value)
        {
            case TomlyTable table:
            {
                var children = new List<LogicalNode>();
                foreach (var entry in table)
                {
                    children.Add(FromTomlyn(entry.Key, entry.Value!));
                }

                return new LogicalNode(LogicalKind.Object, name, null, null, [], children);
            }

            case TomlyTableArray tables:
            {
                var children = new List<LogicalNode>();
                foreach (var item in tables)
                {
                    children.Add(FromTomlyn(null, item));
                }

                return new LogicalNode(LogicalKind.Array, name, null, null, [], children);
            }

            case TomlyArray array:
            {
                var children = new List<LogicalNode>();
                foreach (var item in array)
                {
                    children.Add(FromTomlyn(null, item!));
                }

                return new LogicalNode(LogicalKind.Array, name, null, null, [], children);
            }

            case string text:
                return new LogicalNode(LogicalKind.Scalar, name, null, text, [], [], tomlKind: TomlValueKind.String);

            case long integer:
                return new LogicalNode(
                    LogicalKind.Scalar,
                    name,
                    null,
                    integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [],
                    [],
                    tomlKind: TomlValueKind.Integer);

            case double number:
                return new LogicalNode(LogicalKind.Scalar, name, null, TomlValues.FloatText(number), [], [], tomlKind: TomlValueKind.Float);

            case bool boolean:
                return new LogicalNode(
                    LogicalKind.Scalar,
                    name,
                    null,
                    boolean ? "true" : "false",
                    [],
                    [],
                    tomlKind: TomlValueKind.Boolean);

            case TomlyDateTime dateTime:
                return new LogicalNode(
                    LogicalKind.Scalar,
                    name,
                    null,
                    TomlValues.DateTimeText(dateTime.ToString()),
                    [],
                    [],
                    tomlKind: KindOf(dateTime.Kind));

            default:
                throw new InvalidOperationException("Unknown Tomlyn value type: " + value.GetType().Name);
        }
    }

    private static string? Diff(LogicalNode expected, LogicalNode actual, string path)
    {
        if (expected.Kind != actual.Kind || expected.TomlKind != actual.TomlKind || expected.Value != actual.Value)
        {
            return Where(path) + "cfgedit has " + Describe(expected) + ", Tomlyn has " + Describe(actual);
        }

        switch (expected.Kind)
        {
            case LogicalKind.Object:
            {
                var actualByName = new Dictionary<string, LogicalNode>(StringComparer.Ordinal);
                foreach (LogicalNode child in actual.Children)
                {
                    actualByName[child.Name!] = child;
                }

                var expectedNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (LogicalNode child in expected.Children)
                {
                    string name = child.Name!;
                    expectedNames.Add(name);
                    string childPath = path.Length == 0 ? name : path + "." + name;
                    if (!actualByName.TryGetValue(name, out LogicalNode? other))
                    {
                        return Where(childPath) + "cfgedit has it, Tomlyn does not";
                    }

                    string? difference = Diff(child, other, childPath);
                    if (difference is not null)
                    {
                        return difference;
                    }
                }

                foreach (LogicalNode child in actual.Children)
                {
                    if (!expectedNames.Contains(child.Name!))
                    {
                        string childPath = path.Length == 0 ? child.Name! : path + "." + child.Name;
                        return Where(childPath) + "Tomlyn has it, cfgedit does not";
                    }
                }

                return null;
            }

            case LogicalKind.Array:
            {
                if (expected.Children.Count != actual.Children.Count)
                {
                    return Where(path) + "cfgedit has " + expected.Children.Count + " items, Tomlyn has " + actual.Children.Count;
                }

                for (int i = 0; i < expected.Children.Count; i++)
                {
                    string? difference = Diff(expected.Children[i], actual.Children[i], path + "[" + i + "]");
                    if (difference is not null)
                    {
                        return difference;
                    }
                }

                return null;
            }

            default:
                return null;
        }
    }

    private static string Where(string path)
    {
        return path.Length == 0 ? string.Empty : "at " + path + ": ";
    }

    private static string Describe(LogicalNode node)
    {
        return node.Kind switch
        {
            LogicalKind.Object => "a table",
            LogicalKind.Array => "an array of " + node.Children.Count,
            _ => node.TomlKind + " " + DisplayText.Quote(node.Value ?? string.Empty),
        };
    }

    private static TomlValueKind KindOf(TomlyDateTimeKind kind)
    {
        return kind switch
        {
            TomlyDateTimeKind.OffsetDateTimeByZ or TomlyDateTimeKind.OffsetDateTimeByNumber => TomlValueKind.OffsetDateTime,
            TomlyDateTimeKind.LocalDateTime => TomlValueKind.LocalDateTime,
            TomlyDateTimeKind.LocalDate => TomlValueKind.LocalDate,
            TomlyDateTimeKind.LocalTime => TomlValueKind.LocalTime,
            _ => throw new InvalidOperationException("Unknown Tomlyn date-time kind: " + kind),
        };
    }
}
