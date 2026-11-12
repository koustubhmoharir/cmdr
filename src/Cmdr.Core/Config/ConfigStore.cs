using System.Text.Json;

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

        if (!File.Exists(path))
        {
            return new CmdrConfig();
        }

        return Parse(path);
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

        string fullPath = Path.GetFullPath(path);
        StringComparer comparer = PathComparer;
        var chain = new List<ConfigFile>();
        var visited = new List<string>();

        CmdrConfig config = Load(fullPath);
        chain.Add(new ConfigFile(fullPath, config, IsBase: false));
        visited.Add(fullPath);

        string current = fullPath;
        while (!string.IsNullOrWhiteSpace(config.Base))
        {
            string importer = current;
            string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(importer)!, config.Base));

            int cycleStart = visited.FindIndex(p => comparer.Equals(p, resolved));
            if (cycleStart >= 0)
            {
                IEnumerable<string> loop = visited.Skip(cycleStart).Append(resolved);
                throw new ConfigException($"Base files form a cycle: {string.Join(" → ", loop)}");
            }

            if (!File.Exists(resolved))
            {
                throw new ConfigException($"Base file '{resolved}' imported by '{importer}' does not exist.");
            }

            config = Parse(resolved);
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
    /// directory of the opened file (the last entry); errors of the opened file are not prefixed.
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

        return errors;
    }

    // Paths are compared case-insensitively where the file system usually is.
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // Parses an existing file. Shared by Load (the opened file) and LoadChain (base files).
    private static CmdrConfig Parse(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        CmdrConfig? config;
        try
        {
            using (JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions))
            {
                RejectLegacyCommands(document.RootElement, path);
            }

            config = JsonSerializer.Deserialize(bytes, ConfigJsonContext.Default.CmdrConfig);
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

    /// <summary>
    /// Saves the configuration as indented UTF-8 JSON (no BOM, trailing newline).
    /// Creates the parent directory if needed and replaces the file atomically.
    /// </summary>
    public static void Save(CmdrConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(config, ConfigJsonContext.Default.CmdrConfig);

        string tempPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
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

            NormalizeGroup(group, configPath, $"groups[{g}]");
        }
    }

    private static void NormalizeGroup(GroupSpec group, string configPath, string jsonPath)
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
                    NormalizeGroup(child, configPath, itemPath);
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

        for (int a = 0; a < cmd.Args.Count; a++)
        {
            if (cmd.Args[a] is null)
            {
                throw new ConfigException($"Configuration file '{configPath}': {jsonPath}.args[{a}] must be a string, not null.");
            }
        }
    }
}
