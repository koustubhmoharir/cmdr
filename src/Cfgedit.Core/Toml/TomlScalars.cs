using System.Globalization;
using System.Text.RegularExpressions;

namespace Cfgedit.Core.Toml;

/// <summary>
/// The grammar of TOML's numbers and date-times, shared by the parser and by <see cref="TomlEncoder"/>. Each check returns the
/// message for the first rule the text breaks, or null when the text is valid. The patterns are written from the TOML 1.0
/// grammar; they use ASCII digits only, and <c>\z</c> rather than <c>$</c>, which also matches before a final line feed.
/// </summary>
internal static class TomlScalars
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    private static readonly Regex DecimalInteger = new(@"\A[+-]?(0|[1-9](_?[0-9])*)\z", Options);
    private static readonly Regex HexInteger = new(@"\A0x[0-9A-Fa-f](_?[0-9A-Fa-f])*\z", Options);
    private static readonly Regex OctalInteger = new(@"\A0o[0-7](_?[0-7])*\z", Options);
    private static readonly Regex BinaryInteger = new(@"\A0b[01](_?[01])*\z", Options);

    private static readonly Regex DecimalFloat = new(
        @"\A[+-]?(0|[1-9](_?[0-9])*)((\.[0-9](_?[0-9])*)([eE][+-]?[0-9](_?[0-9])*)?|[eE][+-]?[0-9](_?[0-9])*)\z",
        Options);

    private static readonly Regex SpecialFloat = new(@"\A[+-]?(inf|nan)\z", Options);

    private static readonly Regex DateTimePattern = new(
        @"\A(?<y>[0-9]{4})-(?<mo>[0-9]{2})-(?<d>[0-9]{2})(?:[Tt ](?<time>(?<h>[0-9]{2}):(?<mi>[0-9]{2}):(?<s>[0-9]{2})(?:\.[0-9]+)?)(?<off>[Zz]|(?<oh>[+-][0-9]{2}):(?<om>[0-9]{2}))?)?\z",
        Options);

    private static readonly Regex LocalTime = new(@"\A(?<h>[0-9]{2}):(?<mi>[0-9]{2}):(?<s>[0-9]{2})(?:\.[0-9]+)?\z", Options);

    /// <summary>The message for an integer that is not valid, or null. On success <paramref name="value"/> and <paramref name="radix"/> are set.</summary>
    internal static string? ValidateInteger(string text, out long value, out int radix)
    {
        value = 0;
        radix = 10;
        if (DecimalInteger.IsMatch(text))
        {
            string digits = text.Replace("_", string.Empty, StringComparison.Ordinal);
            if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                return OutOfRange(text);
            }

            return null;
        }

        if (HexInteger.IsMatch(text))
        {
            radix = 16;
        }
        else if (OctalInteger.IsMatch(text))
        {
            radix = 8;
        }
        else if (BinaryInteger.IsMatch(text))
        {
            radix = 2;
        }
        else
        {
            return $"invalid integer '{text}'";
        }

        // The prefix is two characters, and the digits follow it. Accumulate in ulong and refuse anything past long.MaxValue.
        ulong acc = 0;
        for (int i = 2; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_')
            {
                continue;
            }

            int digit = DigitValue(c);
            if (acc > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
            {
                return OutOfRange(text);
            }

            acc = acc * (ulong)radix + (ulong)digit;
        }

        if (acc > long.MaxValue)
        {
            return OutOfRange(text);
        }

        value = (long)acc;
        return null;
    }

    /// <summary>The message for a float that is not valid, or null. On success <paramref name="value"/> is set.</summary>
    internal static string? ValidateFloat(string text, out double value)
    {
        value = 0;
        if (SpecialFloat.IsMatch(text))
        {
            bool negative = text[0] == '-';
            if (text.EndsWith("nan", StringComparison.Ordinal))
            {
                value = double.NaN;
            }
            else
            {
                value = negative ? double.NegativeInfinity : double.PositiveInfinity;
            }

            return null;
        }

        if (!DecimalFloat.IsMatch(text))
        {
            return $"invalid float '{text}'";
        }

        string digits = text.Replace("_", string.Empty, StringComparison.Ordinal);
        value = double.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>
    /// The message for a date or time that is not valid, or null. On success <paramref name="kind"/> is set to the kind the
    /// text has: a date with a time and an offset, a date with a time and no offset, a date, or a time.
    /// </summary>
    internal static string? ValidateDateTime(string text, out TomlDateTimeKind kind)
    {
        kind = TomlDateTimeKind.LocalDate;
        Match m = DateTimePattern.Match(text);
        if (!m.Success)
        {
            Match t = LocalTime.Match(text);
            if (!t.Success)
            {
                return $"invalid date-time '{text}'";
            }

            kind = TomlDateTimeKind.LocalTime;
            return CheckTime(text, t.Groups["h"].Value, t.Groups["mi"].Value, t.Groups["s"].Value);
        }

        string? dateError = CheckDate(text, m);
        if (dateError is not null)
        {
            return dateError;
        }

        if (!m.Groups["time"].Success)
        {
            kind = TomlDateTimeKind.LocalDate;
            return null;
        }

        string? timeError = CheckTime(text, m.Groups["h"].Value, m.Groups["mi"].Value, m.Groups["s"].Value);
        if (timeError is not null)
        {
            return timeError;
        }

        if (!m.Groups["off"].Success)
        {
            kind = TomlDateTimeKind.LocalDateTime;
            return null;
        }

        kind = TomlDateTimeKind.OffsetDateTime;
        if (m.Groups["oh"].Success)
        {
            int offsetHour = int.Parse(m.Groups["oh"].Value.AsSpan(1), CultureInfo.InvariantCulture);
            int offsetMinute = int.Parse(m.Groups["om"].Value, CultureInfo.InvariantCulture);
            if (offsetHour > 23 || offsetMinute > 59)
            {
                return $"invalid date-time '{text}': the offset is out of range";
            }
        }

        return null;
    }

    /// <summary>True when the year is a leap year in the proleptic Gregorian calendar, year 0 included.</summary>
    internal static bool IsLeapYear(int year)
    {
        return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    }

    private static string? CheckDate(string text, Match m)
    {
        int year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
        int month = int.Parse(m.Groups["mo"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        if (month < 1 || month > 12)
        {
            return $"invalid date-time '{text}': the month must be 01 to 12";
        }

        int daysInMonth = month switch
        {
            2 => IsLeapYear(year) ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31,
        };
        if (day < 1 || day > daysInMonth)
        {
            return $"invalid date-time '{text}': the day must be 01 to {daysInMonth:D2} for that month";
        }

        return null;
    }

    private static string? CheckTime(string text, string hour, string minute, string second)
    {
        int h = int.Parse(hour, CultureInfo.InvariantCulture);
        int mi = int.Parse(minute, CultureInfo.InvariantCulture);
        int s = int.Parse(second, CultureInfo.InvariantCulture);
        if (h > 23)
        {
            return $"invalid date-time '{text}': the hour must be 00 to 23";
        }

        if (mi > 59)
        {
            return $"invalid date-time '{text}': the minute must be 00 to 59";
        }

        // Leap seconds: TOML allows 60.
        if (s > 60)
        {
            return $"invalid date-time '{text}': the second must be 00 to 60";
        }

        return null;
    }

    private static string OutOfRange(string text)
    {
        return $"integer '{text}' is outside the 64-bit signed range";
    }

    private static int DigitValue(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        return char.ToLowerInvariant(c) - 'a' + 10;
    }
}
