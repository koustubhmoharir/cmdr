using System.Globalization;
using Cfgedit.Core.Model;

namespace Cfgedit.Core.Toml;

/// <summary>
/// The text of TOML scalars in two forms: the display value, which is the decoded text of a string and the source text of every
/// other kind, and the canonical value, which the logical tree compares. The canonical value of a string is its decoded text, of an
/// integer its decimal form, of a float its shortest round-trip form (<c>inf</c>, <c>-inf</c> and <c>nan</c> for the specials), of a
/// boolean <c>true</c> or <c>false</c>, and of a date-time the source text with the separator written as <c>T</c> and the offset
/// letter as <c>Z</c>. Fractional seconds keep the digits they were written with.
/// </summary>
internal static class TomlValues
{
    /// <summary>The kind and canonical text of a scalar. Throws for an array or a table.</summary>
    public static (TomlValueKind Kind, string Text) Canonical(TomlValue value)
    {
        switch (value)
        {
            case TomlString text:
                return (TomlValueKind.String, text.Value);
            case TomlInteger integer:
                return (TomlValueKind.Integer, integer.Value.ToString(CultureInfo.InvariantCulture));
            case TomlFloat number:
                return (TomlValueKind.Float, FloatText(number.Value));
            case TomlBoolean boolean:
                return (TomlValueKind.Boolean, boolean.Value ? "true" : "false");
            case TomlDateTime dateTime:
                return (KindOf(dateTime.Kind), DateTimeText(dateTime.Raw));
            default:
                throw new InvalidOperationException("Not a TOML scalar: " + value.GetType().Name);
        }
    }

    /// <summary>The display value of a scalar: the decoded text of a string, and the source text of every other kind.</summary>
    public static string Display(TomlValue value)
    {
        return value switch
        {
            TomlString text => text.Value,
            TomlInteger integer => integer.Raw,
            TomlFloat number => number.Raw,
            TomlBoolean boolean => boolean.Value ? "true" : "false",
            TomlDateTime dateTime => dateTime.Raw,
            _ => throw new InvalidOperationException("Not a TOML scalar: " + value.GetType().Name),
        };
    }

    /// <summary>The canonical text of a float: <c>inf</c>, <c>-inf</c> or <c>nan</c> for the specials, else the shortest round-trip form.</summary>
    public static string FloatText(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The canonical text of a date-time: a space between the date and the time becomes <c>T</c>, and a lower-case <c>t</c> or <c>z</c>
    /// becomes upper case. Nothing else changes, so the fraction and the offset are as written.
    /// </summary>
    public static string DateTimeText(string text)
    {
        char[] chars = text.ToCharArray();
        if (chars.Length > 10 && chars[4] == '-' && chars[10] is ' ' or 't')
        {
            chars[10] = 'T';
        }

        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == 'z')
            {
                chars[i] = 'Z';
            }
        }

        return new string(chars);
    }

    /// <summary>The logical kind of a date-time kind.</summary>
    public static TomlValueKind KindOf(TomlDateTimeKind kind)
    {
        return kind switch
        {
            TomlDateTimeKind.OffsetDateTime => TomlValueKind.OffsetDateTime,
            TomlDateTimeKind.LocalDateTime => TomlValueKind.LocalDateTime,
            TomlDateTimeKind.LocalDate => TomlValueKind.LocalDate,
            TomlDateTimeKind.LocalTime => TomlValueKind.LocalTime,
            _ => throw new InvalidOperationException("Unknown date-time kind: " + kind),
        };
    }
}
