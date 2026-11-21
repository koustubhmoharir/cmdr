using System.Text.Json;
using Shared.Core;

namespace Cmdr.Core.Config;

/// <summary>Loads and saves the cmdr configuration file.</summary>
public static class ConfigStore
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads the configuration. A missing file yields an empty configuration with defaults.
    /// Does not validate; call <see cref="CmdrConfig.Validate"/> separately.
    /// </summary>
    /// <exception cref="ConfigException">The file is malformed, has a legacy <c>commands</c> list, or contains a JSON null document.</exception>
    public static CmdrConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigException($"Invalid configuration path '{path}': {ex.Message}", ex);
        }
        try
        {
            return Parse(fullPath);
        }
        catch (FileNotFoundException)
        {
            return new CmdrConfig();
        }
        catch (DirectoryNotFoundException)
        {
            return new CmdrConfig();
        }
    }

    /// <summary>
    /// Loads the file at <paramref name="path"/> and then, following each <see cref="CmdrConfig.Base"/> import in turn, the base files.
    /// Every file keeps its own groups and settings; nothing is merged. A missing opened file yields one empty entry, but a missing
    /// base file is an error. Does not validate; see <see cref="ValidateChain"/>.
    /// </summary>
    /// <returns>The files ordered deepest base first, with the opened file last.</returns>
    /// <exception cref="ConfigException">A file is malformed, a base file does not exist, or the imports form a cycle.</exception>
    public static IReadOnlyList<ConfigFile> LoadChain(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigException($"Invalid configuration path '{path}': {ex.Message}", ex);
        }
        StringComparer comparer = PathComparison.Comparer;
        var chain = new List<ConfigFile>();
        var visited = new List<string>();

        CmdrConfig config = Load(fullPath);
        chain.Add(new ConfigFile(fullPath, config, IsBase: false));
        visited.Add(fullPath);

        string current = fullPath;
        while (!string.IsNullOrWhiteSpace(config.Base))
        {
            string importer = current;
            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(importer)!, config.Base));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ConfigException($"Configuration file '{importer}' has an invalid base path '{config.Base}': {ex.Message}", ex);
            }

            int cycleStart = visited.FindIndex(p => comparer.Equals(p, resolved));
            if (cycleStart >= 0)
            {
                IEnumerable<string> loop = visited.Skip(cycleStart).Append(resolved);
                throw new ConfigException($"Base files form a cycle: {string.Join(" → ", loop)}");
            }

            try
            {
                config = Parse(resolved);
            }
            catch (FileNotFoundException ex)
            {
                throw new ConfigException($"Base file '{resolved}' imported by '{importer}' does not exist.", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new ConfigException($"Base file '{resolved}' imported by '{importer}' does not exist.", ex);
            }
            chain.Add(new ConfigFile(resolved, config, IsBase: true));
            visited.Add(resolved);
            current = resolved;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Validates each file of a chain (see <see cref="LoadChain"/>) on its own and returns all errors, in chain order. A group name
    /// may therefore repeat across files but not within one. Errors of base files are prefixed with the file's path relative to the
    /// directory of the opened file (the last entry); errors of the opened file are not prefixed. The log paths of all commands of
    /// the chain are then checked together (see <see cref="LogPathErrors"/>), after the per-file errors.
    /// </summary>
    public static IReadOnlyList<string> ValidateChain(IReadOnlyList<ConfigFile> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var errors = new List<string>();
        if (chain.Count == 0)
        {
            return errors;
        }

        string openedDir = chain[^1].Dir;
        foreach (ConfigFile file in chain)
        {
            string prefix = file.IsBase ? $"{Path.GetRelativePath(openedDir, file.Path)}: " : "";
            foreach (string error in file.Config.Validate())
            {
                errors.Add(prefix + error);
            }
        }

        errors.AddRange(LogPathErrors(chain, onlyInvolvingOpened: false));
        return errors;
    }

    /// <summary>
    /// Finds log path conflicts among all commands of a chain, whether or not their logs are enabled. Two commands conflict when their
    /// resolved log paths are equal, when both name one existing file (hard links, see <see cref="FileIdentity"/>), or when one log
    /// path is a folder of the other (the other path starts with it and a directory separator). Paths are compared with
    /// <see cref="PathComparison"/> after resolving symbolic links, so paths that reach one file through a link conflict too. Each file's
    /// logs directory is used for its own commands. Commands whose paths cannot be resolved are skipped here.
    /// </summary>
    /// <param name="chain">The files of the chain; the opened file is the last entry.</param>
    /// <param name="onlyInvolvingOpened">True to keep only the errors where at least one involved command comes from the opened file.</param>
    /// <returns>
    /// One error per set of equal paths (in order of first occurrence), then one per folder conflict (in order of the folder's first
    /// occurrence). Each error names the first command of a path, or every command for an equal path. Paths are relative to the
    /// directory of the opened file.
    /// </returns>
    internal static IReadOnlyList<string> LogPathErrors(IReadOnlyList<ConfigFile> chain, bool onlyInvolvingOpened)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var errors = new List<string>();
        if (chain.Count == 0)
        {
            return errors;
        }

        string openedDir = chain[^1].Dir;
        var targets = new List<LogTarget>();
        foreach (ConfigFile file in chain)
        {
            string logsDir;
            try
            {
                logsDir = ConfigPaths.ResolveLogsDir(file.Config, file.Dir);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            string suffix = file.IsBase ? $" (in {Path.GetRelativePath(openedDir, file.Path)})" : "";
            for (int g = 0; g < file.Config.Groups.Count; g++)
            {
                GroupSpec group = file.Config.Groups[g];
                CollectLogTargets(group, CmdrConfig.TopLevelLabel(group, g), Array.Empty<GroupSpec>(), logsDir, suffix, !file.IsBase, targets);
            }
        }

        // Targets are grouped twice. Equal paths (with links resolved, so two paths that reach one file through a link are equal) are
        // the unit of the folder check. Targets that name one existing file (a hard link is another name for it) share a group key,
        // which is the file's identity when it has one and the path otherwise. Messages show the path as configured (that of the
        // first command of the group).
        StringComparer comparer = PathComparison.Comparer;
        var byPath = new Dictionary<string, List<LogTarget>>(comparer);
        var paths = new List<string>();
        var byFile = new Dictionary<string, List<LogTarget>>(comparer);
        var fileKeys = new List<string>();
        var keyOfPath = new Dictionary<string, string>(comparer);
        var resolvedDirs = new Dictionary<string, string>(comparer);
        foreach (LogTarget target in targets)
        {
            string physical = ResolveLogPathLinks(target.FullPath, resolvedDirs);
            if (!byPath.TryGetValue(physical, out List<LogTarget>? samePath))
            {
                samePath = new List<LogTarget>();
                byPath[physical] = samePath;
                paths.Add(physical);
            }

            samePath.Add(target);

            if (!keyOfPath.TryGetValue(physical, out string? key))
            {
                key = FileKey(physical);
                keyOfPath[physical] = key;
            }

            if (!byFile.TryGetValue(key, out List<LogTarget>? sharing))
            {
                sharing = new List<LogTarget>();
                byFile[key] = sharing;
                fileKeys.Add(key);
            }

            sharing.Add(target);
        }

        foreach (string key in fileKeys)
        {
            List<LogTarget> sharing = byFile[key];
            if (sharing.Count < 2 || (onlyInvolvingOpened && !sharing.Any(t => t.FromOpened)))
            {
                continue;
            }

            string verb = sharing.Count == 2 ? "both log" : "all log";
            errors.Add($"Log path conflict: {JoinLabels(sharing.Select(t => t.Label))} {verb} to '{DisplayPath(openedDir, sharing[0].FullPath)}'.");
        }

        foreach (string outer in paths)
        {
            string prefix = outer.EndsWith(Path.DirectorySeparatorChar) ? outer : outer + Path.DirectorySeparatorChar;
            List<LogTarget> outerTargets = byPath[outer];
            foreach (string inner in paths)
            {
                if (inner.Length <= prefix.Length || !comparer.Equals(inner[..prefix.Length], prefix))
                {
                    continue;
                }

                List<LogTarget> innerTargets = byPath[inner];
                if (onlyInvolvingOpened && !outerTargets.Any(t => t.FromOpened) && !innerTargets.Any(t => t.FromOpened))
                {
                    continue;
                }

                errors.Add(
                    $"Log path conflict: the log of {outerTargets[0].Label} ('{DisplayPath(openedDir, outerTargets[0].FullPath)}') is the folder of the log of " +
                    $"{innerTargets[0].Label} ('{DisplayPath(openedDir, innerTargets[0].FullPath)}').");
            }
        }

        return errors;
    }

    // The key that groups targets naming one file: the identity of an existing regular file, prefixed with a NUL that no path contains
    // so it can never equal a path key; otherwise the path itself.
    private static string FileKey(string physicalPath)
    {
        string? identity = FileIdentity.TryGet(physicalPath);
        return identity is null ? physicalPath : "\0id:" + identity;
    }

    /// <summary>
    /// The log path with links resolved (see <see cref="PhysicalPath"/>), or the path itself when they cannot be (a loop, or a
    /// directory that cannot be searched). Resolved directories are cached in <paramref name="resolvedDirs"/>: many logs share
    /// one. The file name is resolved separately, since a log file may itself be a link.
    /// </summary>
    private static string ResolveLogPathLinks(string fullPath, Dictionary<string, string> resolvedDirs)
    {
        try
        {
            string? dir = Path.GetDirectoryName(fullPath);
            if (dir is null)
            {
                return fullPath;
            }

            if (!resolvedDirs.TryGetValue(dir, out string? physicalDir))
            {
                physicalDir = PhysicalPath.Resolve(dir);
                resolvedDirs[dir] = physicalDir;
            }

            return PhysicalPath.Resolve(Path.Join(physicalDir, Path.GetFileName(fullPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return fullPath;
        }
    }

    // A command's resolved log path, with its label (already suffixed for base files) and whether it comes from the opened file.
    private sealed record LogTarget(string FullPath, string Label, bool FromOpened);

    // Walks the items of a group depth-first, adding every command's resolved log path. Commands whose path cannot be resolved are skipped.
    private static void CollectLogTargets(
        GroupSpec parent,
        string parentLabel,
        IReadOnlyList<GroupSpec> ancestors,
        string logsDir,
        string suffix,
        bool fromOpened,
        List<LogTarget> targets)
    {
        List<GroupSpec> groupsIncludingParent = ancestors.Append(parent).ToList();
        for (int i = 0; i < parent.Items.Count; i++)
        {
            ItemSpec item = parent.Items[i];
            string label = $"{parentLabel} › {CmdrConfig.DescribeItem(item, i)}";
            switch (item)
            {
                case CommandSpec cmd:
                    try
                    {
                        string path = ConfigPaths.ResolveLogPath(groupsIncludingParent, cmd, logsDir);
                        targets.Add(new LogTarget(path, label + suffix, fromOpened));
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        // Not reported here; the command's own settings are checked elsewhere.
                    }

                    break;

                case GroupSpec child:
                    CollectLogTargets(child, label, groupsIncludingParent, logsDir, suffix, fromOpened, targets);
                    break;
            }
        }
    }

    private static string DisplayPath(string openedDir, string path) => Path.GetRelativePath(openedDir, path);

    // "A", "A and B", "A, B and C".
    private static string JoinLabels(IEnumerable<string> labels)
    {
        string[] items = labels.ToArray();
        return items.Length <= 1
            ? string.Join("", items)
            : $"{string.Join(", ", items[..^1])} and {items[^1]}";
    }

    // Parses an existing file. Shared by Load (the opened file) and LoadChain (base files).
    private static CmdrConfig Parse(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ConfigException($"Could not read configuration file '{path}': {ex.Message}", ex);
        }
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            bytes = bytes[3..];
        }
        CmdrConfig? config;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions))
            {
                RejectLegacyCommands(document.RootElement, path);
            }

            config = JsonSerializer.Deserialize(bytes, ConfigJsonContext.Default.CmdrConfig);
        }
        catch (ItemSourceJsonException ex)
        {
            (long itemLine, long itemPosition) = Locate(bytes, ex.SourceByteOffset);
            long relativeLine = ex.LineNumber ?? 0;
            long line = itemLine + relativeLine;
            long position = relativeLine == 0
                ? itemPosition + (ex.BytePositionInLine ?? 0)
                : (ex.BytePositionInLine ?? 0) + 1;
            string detail = ex.Message.Split(" Path: ", StringSplitOptions.None)[0];
            throw new ConfigException(
                $"Malformed JSON in '{path}' at line {line}, position {position}: {detail} (path: {ex.Path ?? "<unknown>"}).",
                ex);
        }
        catch (JsonException ex)
        {
            throw new ConfigException(
                $"Malformed JSON in '{path}' at line {(ex.LineNumber ?? 0) + 1}, position {(ex.BytePositionInLine ?? 0) + 1}: {ex.Message}",
                ex);
        }

        if (config is null)
        {
            throw new ConfigException($"Configuration file '{path}' contains a JSON null, expected an object.");
        }

        Normalize(config, path);
        return config;
    }

    private static (long Line, long Position) Locate(byte[] bytes, long byteOffset)
    {
        long line = 1;
        long position = 1;
        int end = (int)Math.Clamp(byteOffset, 0, bytes.LongLength);
        for (int i = 0; i < end; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                line++;
                position = 1;
            }
            else
            {
                position++;
            }
        }

        return (line, position);
    }

    /// <summary>
    /// Saves the configuration as indented UTF-8 JSON (no BOM, trailing newline).
    /// Creates the parent directory if needed and replaces the file atomically. An existing file keeps its Unix permissions.
    /// If <paramref name="path"/> is a symbolic link, its final target is replaced and the link is kept.
    /// </summary>
    /// <exception cref="IOException">The path is a dangling symbolic link, or the file cannot be written.</exception>
    public static void Save(CmdrConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(path);

        AtomicFile.Write(path, Serialize(config));
    }

    /// <summary>
    /// The bytes that <see cref="Save"/> writes for the configuration: indented UTF-8 JSON, no BOM, trailing newline. Two
    /// configurations with equal bytes are saved as the same file.
    /// </summary>
    public static byte[] Serialize(CmdrConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return JsonSerializer.SerializeToUtf8Bytes(config, ConfigJsonContext.Default.CmdrConfig);
    }

    // A group with "commands" is an old-format file (rejected even next to "items", which would silently drop it). Items are read by shape, so this is the only
    // place that can report the old property by name with its position. Top-level groups are read here too.
    private static void RejectLegacyCommands(JsonElement root, string configPath)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("groups", out JsonElement groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement group in groups.EnumerateArray())
        {
            RejectLegacyGroup(group, $"groups[{index}]", configPath);
            index++;
        }
    }

    private static void RejectLegacyGroup(JsonElement group, string jsonPath, string configPath)
    {
        if (group.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (group.TryGetProperty("commands", out _))
        {
            throw new ConfigException($"Configuration file '{configPath}': {jsonPath} uses 'commands'; use 'items' instead of 'commands'.");
        }

        if (group.TryGetProperty("program", out _))
        {
            throw new ConfigException($"Configuration file '{configPath}': {jsonPath} has 'program' (a command) but appears in 'groups'.");
        }

        if (!group.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement child in items.EnumerateArray())
        {
            // An object with "program" is a command; anything else is checked as a group.
            if (child.ValueKind == JsonValueKind.Object && !child.TryGetProperty("program", out _))
            {
                RejectLegacyGroup(child, $"{jsonPath}.items[{index}]", configPath);
            }

            index++;
        }
    }

    // JSON null is valid for nested values, which would otherwise leave non-nullable
    // properties null. Replace them with defaults; reject null list entries outright.
    private static void Normalize(CmdrConfig config, string configPath)
    {
        config.Groups ??= new List<GroupSpec>();

        for (int g = 0; g < config.Groups.Count; g++)
        {
            GroupSpec? group = config.Groups[g];
            if (group is null)
            {
                throw new ConfigException($"Configuration file '{configPath}': groups[{g}] must be an object, not null.");
            }

            NormalizeGroup(group, configPath, $"groups[{g}]", level: 1);
        }
    }

    // level is the nesting level of group (1 for a top-level group). A nested group deeper than CmdrConfig.MaxGroupDepth refuses the file.
    private static void NormalizeGroup(GroupSpec group, string configPath, string jsonPath, int level)
    {
        group.Name ??= "";
        group.Items ??= new List<ItemSpec>();

        for (int i = 0; i < group.Items.Count; i++)
        {
            ItemSpec? item = group.Items[i];
            string itemPath = $"{jsonPath}.items[{i}]";
            if (item is null)
            {
                throw new ConfigException($"Configuration file '{configPath}': {itemPath} must be an object, not null.");
            }

            item.Name ??= "";

            switch (item)
            {
                case GroupSpec child:
                    if (level + 1 > CmdrConfig.MaxGroupDepth)
                    {
                        throw new ConfigException(
                            $"Configuration file '{configPath}': {itemPath} is nested deeper than {CmdrConfig.MaxGroupDepth} group levels.");
                    }

                    NormalizeGroup(child, configPath, itemPath, level + 1);
                    break;

                case CommandSpec cmd:
                    NormalizeCommand(cmd, configPath, itemPath);
                    break;
            }
        }
    }

    private static void NormalizeCommand(CommandSpec cmd, string configPath, string jsonPath)
    {
        cmd.Program ??= "";
        cmd.Env ??= new Dictionary<string, string?>();
        cmd.Stop ??= new StopSpec();
        cmd.Log ??= new LogSpec();
        cmd.Args ??= new List<string>();
        // A JSON null for "ready" means no readiness patterns, the same as omitting it. A null pattern becomes blank,
        // which validation reports.
        if (cmd.Ready is not null)
        {
            cmd.Ready.Pattern ??= "";
        }

        for (int a = 0; a < cmd.Args.Count; a++)
        {
            if (cmd.Args[a] is null)
            {
                throw new ConfigException($"Configuration file '{configPath}': {jsonPath}.args[{a}] must be a string, not null.");
            }
        }
    }
}
