using System.Text.Json;

namespace Cmdr.Core.Config;

/// <summary>Loads and saves the cmdr configuration file.</summary>
public static class ConfigStore
{
    /// <summary>
    /// Loads the configuration. A missing file yields an empty configuration with defaults.
    /// Does not validate; call <see cref="CmdrConfig.Validate"/> separately.
    /// </summary>
    /// <exception cref="ConfigException">The file is malformed or contains a JSON null document.</exception>
    public static CmdrConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            return new CmdrConfig();
        }

        CmdrConfig? config;
        try
        {
            using FileStream stream = File.OpenRead(path);
            config = JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.CmdrConfig);
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

    // JSON null is valid for nested values, which would otherwise leave non-nullable
    // properties null. Replace them with defaults; reject null list entries outright.
    private static void Normalize(CmdrConfig config, string path)
    {
        config.Groups ??= new List<GroupSpec>();

        for (int g = 0; g < config.Groups.Count; g++)
        {
            GroupSpec? group = config.Groups[g];
            if (group is null)
            {
                throw new ConfigException($"Configuration file '{path}': groups[{g}] must be an object, not null.");
            }

            group.Name ??= "";
            group.Commands ??= new List<CommandSpec>();

            for (int c = 0; c < group.Commands.Count; c++)
            {
                CommandSpec? cmd = group.Commands[c];
                if (cmd is null)
                {
                    throw new ConfigException($"Configuration file '{path}': groups[{g}].commands[{c}] must be an object, not null.");
                }

                cmd.Name ??= "";
                cmd.Program ??= "";
                cmd.Env ??= new Dictionary<string, string?>();
                cmd.Stop ??= new StopSpec();
                cmd.Log ??= new LogSpec();
                cmd.Args ??= new List<string>();

                for (int a = 0; a < cmd.Args.Count; a++)
                {
                    if (cmd.Args[a] is null)
                    {
                        throw new ConfigException($"Configuration file '{path}': groups[{g}].commands[{c}].args[{a}] must be a string, not null.");
                    }
                }
            }
        }
    }
}
