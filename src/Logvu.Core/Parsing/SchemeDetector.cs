using System.Text.Json;

namespace Logvu.Core.Parsing;

/// <summary>Chooses the scheme of a file from the first lines of its content.</summary>
public static class SchemeDetector
{
    /// <summary>The number of non-blank lines examined.</summary>
    public const int SampleSize = 20;

    /// <summary>
    /// Detects the scheme. The first non-blank line decides between the JSON schemes: when it is a JSON object, the result
    /// is <c>serilog-json</c> if it has <c>Timestamp</c> or <c>MessageTemplate</c>, or <c>slog-json</c> if it has
    /// <c>time</c> or <c>msg</c>, and only if that scheme is among <paramref name="candidates"/>; otherwise there is no
    /// result. Any other first line selects the text scheme whose header matches the most of the sampled lines. Ties go
    /// to the earlier candidate.
    /// </summary>
    /// <param name="lines">The first lines of the file.</param>
    /// <param name="candidates">The schemes to choose from, in preference order.</param>
    /// <returns>The chosen scheme, or <c>null</c> when none applies.</returns>
    public static ILogScheme? Detect(IReadOnlyList<string> lines, IReadOnlyList<ILogScheme> candidates)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(candidates);

        var sample = new List<string>(SampleSize);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            sample.Add(line);
            if (sample.Count == SampleSize)
            {
                break;
            }
        }

        if (sample.Count == 0)
        {
            return null;
        }

        if (TryReadObjectMembers(sample[0], out bool hasSerilogKeys, out bool hasSlogKeys))
        {
            if (hasSerilogKeys)
            {
                return candidates.OfType<SerilogJsonScheme>().FirstOrDefault();
            }

            return hasSlogKeys ? candidates.OfType<SlogJsonScheme>().FirstOrDefault() : null;
        }

        ILogScheme? best = null;
        int bestCount = 0;
        foreach (ILogScheme candidate in candidates)
        {
            if (candidate.IsJson)
            {
                continue;
            }

            int count = 0;
            foreach (string line in sample)
            {
                if (candidate.IsHeader(line))
                {
                    count++;
                }
            }

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>Tells whether the line is a JSON object, and which of the key sets that picks out the Serilog or slog shape.</summary>
    private static bool TryReadObjectMembers(string line, out bool serilogKeys, out bool slogKeys)
    {
        serilogKeys = false;
        slogKeys = false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            serilogKeys = root.TryGetProperty("Timestamp", out _) || root.TryGetProperty("MessageTemplate", out _);
            slogKeys = root.TryGetProperty("time", out _) || root.TryGetProperty("msg", out _);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
