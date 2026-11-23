namespace Shared.Tui;

/// <summary>
/// One row of the recent-configs picker, as data. <see cref="Text"/> is the row as plain text; the picker draws the parts itself
/// (see <see cref="RecentRowsSource"/>).
/// </summary>
/// <param name="Number">The 1-based number, right-aligned to the widest number.</param>
/// <param name="Name">The file name, padded on the right to the widest name's columns.</param>
/// <param name="Missing">Whether the file does not exist.</param>
/// <param name="MissingColumn">Whether the <c>(not found)</c> column is shown: it is, when some file of the list is missing.</param>
/// <param name="Directory">The directory, with the home folder shortened to <c>~</c>.</param>
/// <param name="RepoStart">The index in <paramref name="Directory"/> where the name of the outermost repository folder starts, or 0.</param>
/// <param name="RepoLength">The length of that name in <paramref name="Directory"/>, or 0 when no name of a repository is shown.</param>
public sealed record RecentRow(
    string Number,
    string Name,
    bool Missing,
    bool MissingColumn,
    string Directory,
    int RepoStart,
    int RepoLength)
{
    private const string NotFound = "(not found)";

    /// <summary>
    /// The row as plain text: the number, the name, the <c>(not found)</c> column when shown, then the directory. No trailing spaces.
    /// </summary>
    public string Text
    {
        get
        {
            string row = Number + "  " + Name;
            if (MissingColumn)
            {
                row += "  " + (Missing ? NotFound : new string(' ', NotFound.Length));
            }

            if (Directory.Length > 0)
            {
                row += "  " + Directory;
            }

            return row.TrimEnd();
        }
    }
}

/// <summary>The rows of the recent-configs picker. Pure: the file system, the home folder and the repositories are passed in.</summary>
public static class RecentRows
{
    /// <summary>
    /// One row per file, most recent first. When some file does not <paramref name="exists"/>, the rows show a <c>(not found)</c> column
    /// before the directory, so that a long directory clipped at the right edge never hides it. The directory has the home folder
    /// shortened to <c>~</c>, and the name of the outermost repository it lies in (from <paramref name="repoRootOf"/>, which is called
    /// once per distinct directory) is located for the picker to colour.
    /// </summary>
    /// <param name="files">The file paths, in display order.</param>
    /// <param name="exists">Whether a file exists.</param>
    /// <param name="home">The home folder, or null or empty for none.</param>
    /// <param name="repoRootOf">The outermost repository root of a directory, or null when it is in none. See <see cref="RepoRoot.Outermost"/>.</param>
    public static IReadOnlyList<RecentRow> Build(
        IReadOnlyList<string> files,
        Func<string, bool> exists,
        string? home,
        Func<string, string?> repoRootOf)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(repoRootOf);

        if (files.Count == 0)
        {
            return Array.Empty<RecentRow>();
        }

        int numberWidth = files.Count.ToString().Length;
        int nameWidth = files.Max(file => TextWidth.Columns(Path.GetFileName(file)));
        string? homeRoot = HomeRoot(home);
        bool[] missing = files.Select(file => !exists(file)).ToArray();
        bool anyMissing = missing.Any(m => m);
        var reposByDirectory = new Dictionary<string, string?>(StringComparer.Ordinal);

        var rows = new List<RecentRow>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            string fullDirectory = Path.GetDirectoryName(path) ?? string.Empty;
            if (!reposByDirectory.TryGetValue(fullDirectory, out string? repo))
            {
                repo = fullDirectory.Length == 0 ? null : repoRootOf(fullDirectory);
                reposByDirectory[fullDirectory] = repo;
            }

            string directory = ShortenHome(fullDirectory, homeRoot);
            (int repoStart, int repoLength) = RepoSegment(directory, repo, homeRoot);
            rows.Add(new RecentRow(
                Number: (i + 1).ToString().PadLeft(numberWidth),
                Name: TextWidth.Fit(Path.GetFileName(path), nameWidth),
                Missing: missing[i],
                MissingColumn: anyMissing,
                Directory: directory,
                RepoStart: repoStart,
                RepoLength: repoLength));
        }

        return rows;
    }

    // The home folder without trailing separators, or null when there is none (or it is a root, which is not shortened).
    private static string? HomeRoot(string? home)
    {
        string? trimmed = home?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string ShortenHome(string directory, string? homeRoot)
    {
        if (homeRoot is null || directory.Length == 0)
        {
            return directory;
        }

        if (directory.Equals(homeRoot, PathComparison))
        {
            return "~";
        }

        string prefix = homeRoot + Path.DirectorySeparatorChar;
        if (directory.StartsWith(prefix, PathComparison))
        {
            return "~" + Path.DirectorySeparatorChar + directory[prefix.Length..];
        }

        return directory;
    }

    // Where the name of the repository root is in the shown directory. The root is shown as the directory is (shortened or not), so
    // its shown form is a prefix of the shown directory when it is shown at all. A root with no name (a drive or file system root),
    // or one that is above the home folder (so not shown), gives none.
    private static (int Start, int Length) RepoSegment(string directory, string? repoRoot, string? homeRoot)
    {
        if (repoRoot is null || directory.Length == 0)
        {
            return (0, 0);
        }

        string shownRoot = Path.TrimEndingDirectorySeparator(ShortenHome(repoRoot, homeRoot));
        string name = Path.GetFileName(shownRoot);
        if (name.Length == 0 || !directory.StartsWith(shownRoot, PathComparison))
        {
            return (0, 0);
        }

        bool endsAtName = directory.Length == shownRoot.Length
            || directory[shownRoot.Length] == Path.DirectorySeparatorChar
            || directory[shownRoot.Length] == Path.AltDirectorySeparatorChar;
        if (!endsAtName)
        {
            return (0, 0);
        }

        return (shownRoot.Length - name.Length, name.Length);
    }
}
