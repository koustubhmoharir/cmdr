namespace Cmdr.Core.Config;

/// <summary>
/// Resolves symbolic links in a path the way the file system does when it opens it, so two paths that name the same file through
/// different links resolve to the same string.
/// </summary>
/// <remarks>
/// The path is walked one component at a time from its root. A component that is a link is replaced by its target, which is
/// walked in turn; a relative target is based at the directory that holds the link. A <c>..</c> moves to the parent of the
/// directory reached so far, so <c>alias/../x</c> with <c>alias</c> a link to <c>a/b</c> names <c>a/x</c>, not the <c>x</c> next to
/// <c>alias</c> (which lexical normalization such as <see cref="Path.GetFullPath(string)"/> or
/// <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/> would give). Components below the first one that does not exist are kept
/// as written: nothing under a missing directory can be a link. <see cref="Resolve"/> is for a path that may not exist yet, and so
/// keeps going past a missing component, where a later <c>..</c> would then skip back over it. <see cref="ResolveExisting"/> is for
/// a path that is about to be opened, and rejects such a path instead.
/// </remarks>
internal static class PhysicalPath
{
    /// <summary>The most links followed for one path, as Linux allows (ELOOP).</summary>
    private const int MaxLinks = 40;

    private static readonly char[] Separators = Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
        ? [Path.DirectorySeparatorChar]
        : [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// The path with every link resolved. A component that does not exist does not stop the walk: the rest of the path is kept as
    /// written, so the result may name a file that cannot be opened (for example <c>missing/../x</c> gives <c>x</c>). Use this for a
    /// path that may not exist yet.
    /// </summary>
    /// <param name="path">A fully qualified path. Its <c>..</c> components are resolved physically too.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not fully qualified.</exception>
    /// <exception cref="IOException">More than 40 links had to be followed (a loop), or a link could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">A directory on the path cannot be searched.</exception>
    public static string Resolve(string path) => Walk(path, strict: false);

    /// <summary>
    /// <see cref="Resolve(string)"/> for a path that is about to be opened. Every component that is followed by another component must
    /// be an existing directory after link resolution, so that a <c>..</c> cannot skip past a missing or non-directory component the
    /// way the file system refuses to. The last component may be missing.
    /// </summary>
    /// <param name="path">A fully qualified path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not fully qualified.</exception>
    /// <exception cref="IOException">
    /// A component followed by more components does not exist or is not a directory, or the links could not be resolved (see
    /// <see cref="Resolve(string)"/>).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">A directory on the path cannot be searched.</exception>
    public static string ResolveExisting(string path) => Walk(path, strict: true);

    private static string Walk(string path, bool strict)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"The path must be fully qualified: '{path}'.", nameof(path));
        }

        string current = Path.GetPathRoot(path)!;
        var pending = new Stack<string>();
        Push(pending, path[current.Length..]);
        bool exists = true;
        int links = 0;

        while (pending.Count > 0)
        {
            string part = pending.Pop();
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current; // the parent of a root is the root
                continue;
            }

            string next = Path.Join(current, part);
            if (exists)
            {
                string? target = new FileInfo(next).LinkTarget;
                if (target is not null)
                {
                    if (++links > MaxLinks)
                    {
                        throw new IOException($"Too many levels of symbolic links in '{path}'.");
                    }

                    // A fully qualified target starts again at its root; a rooted one (Windows "\dir") at the current root;
                    // a relative one at the directory that holds the link, which is where the walk is.
                    if (Path.IsPathFullyQualified(target))
                    {
                        current = Path.GetPathRoot(target)!;
                        target = target[current.Length..];
                    }
                    else if (Path.IsPathRooted(target))
                    {
                        current = Path.GetPathRoot(current)!;
                    }

                    Push(pending, target);
                    continue;
                }

                exists = Path.Exists(next);
            }

            if (strict && HasPending(pending) && !Directory.Exists(next))
            {
                throw new IOException(Path.Exists(next)
                    ? $"Cannot resolve '{path}': '{part}' is not a directory."
                    : $"Cannot resolve '{path}': '{part}' does not exist.");
            }

            current = next;
        }

        return current;
    }

    /// <summary>True when a component that is not empty or <c>.</c> is still to be walked, so the one just reached is not the last.</summary>
    private static bool HasPending(Stack<string> pending)
    {
        foreach (string part in pending)
        {
            if (part.Length > 0 && part != ".")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Pushes the components of <paramref name="relative"/> so that the first one is popped first.</summary>
    private static void Push(Stack<string> pending, string relative)
    {
        string[] parts = relative.Split(Separators);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            pending.Push(parts[i]);
        }
    }
}
