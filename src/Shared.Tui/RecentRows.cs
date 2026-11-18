namespace Shared.Tui;

/// <summary>The rows of the recent-configs picker. Pure: the file system and the home folder are passed in.</summary>
public static class RecentRows
{
    private const string NotFound = "(not found)";

    /// <summary>
    /// One row per file, most recent first: the 1-based number, the file name, and its directory with the home folder
    /// shortened to <c>~</c>. When some file does not <paramref name="exists"/>, a column before the directory marks those
    /// files <c>(not found)</c>; it comes before the directory so that a long directory clipped at the right edge never hides it.
    /// </summary>
    /// <param name="files">The file paths, in display order.</param>
    /// <param name="exists">Whether a file exists.</param>
    /// <param name="home">The home folder, or null or empty for none.</param>
    public static IReadOnlyList<string> Build(IReadOnlyList<string> files, Func<string, bool> exists, string? home)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(exists);

        if (files.Count == 0)
        {
            return Array.Empty<string>();
        }

        int numberWidth = files.Count.ToString().Length;
        int nameWidth = files.Max(file => TextWidth.Columns(Path.GetFileName(file)));
        string? homeRoot = HomeRoot(home);
        bool[] missing = files.Select(file => !exists(file)).ToArray();
        bool anyMissing = missing.Any(m => m);

        var rows = new List<string>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            string directory = ShortenHome(Path.GetDirectoryName(path) ?? string.Empty, homeRoot);

            string row = (i + 1).ToString().PadLeft(numberWidth) + "  " + TextWidth.Fit(name, nameWidth);
            if (anyMissing)
            {
                row += "  " + (missing[i] ? NotFound : new string(' ', NotFound.Length));
            }

            if (directory.Length > 0)
            {
                row += "  " + directory;
            }

            rows.Add(row.TrimEnd());
        }

        return rows;
    }

    // The home folder without trailing separators, or null when there is none (or it is a root, which is not shortened).
    private static string? HomeRoot(string? home)
    {
        string? trimmed = home?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string ShortenHome(string directory, string? homeRoot)
    {
        if (homeRoot is null || directory.Length == 0)
        {
            return directory;
        }

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory.Equals(homeRoot, comparison))
        {
            return "~";
        }

        string prefix = homeRoot + Path.DirectorySeparatorChar;
        if (directory.StartsWith(prefix, comparison))
        {
            return "~" + Path.DirectorySeparatorChar + directory[prefix.Length..];
        }

        return directory;
    }
}
