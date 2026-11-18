using System.Text;
using System.Text.Json;

namespace Logvu.Core.Parsing;

/// <summary>Renders a Serilog message template from an event's properties.</summary>
internal static class SerilogTemplate
{
    /// <summary>
    /// Renders <paramref name="template"/>. <c>{{</c> and <c>}}</c> are literal braces. A hole names a property, with
    /// an optional <c>@</c> or <c>$</c> prefix and optional <c>,alignment</c> and <c>:format</c> that are ignored. A hole
    /// whose property is missing, or that is malformed, stays as written. A positional <c>{0}</c> reads property "0".
    /// </summary>
    public static string Render(string template, JsonElement? properties)
    {
        var sb = new StringBuilder(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                string hole = template[(i + 1)..close];
                if (TryRenderHole(hole, properties, out string value))
                {
                    sb.Append(value);
                }
                else
                {
                    sb.Append(template, i, close - i + 1);
                }

                i = close + 1;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool TryRenderHole(string hole, JsonElement? properties, out string value)
    {
        value = string.Empty;
        int p = hole.Length > 0 && (hole[0] == '@' || hole[0] == '$') ? 1 : 0;
        int nameStart = p;
        while (p < hole.Length && IsNameChar(hole[p]))
        {
            p++;
        }

        if (p == nameStart || (p < hole.Length && hole[p] != ',' && hole[p] != ':'))
        {
            return false;
        }

        if (properties is not { ValueKind: JsonValueKind.Object } obj
            || !obj.TryGetProperty(hole[nameStart..p], out JsonElement property))
        {
            return false;
        }

        value = JsonValues.AsText(property);
        return true;
    }

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
