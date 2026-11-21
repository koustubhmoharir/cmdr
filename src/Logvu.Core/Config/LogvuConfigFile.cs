using Shared.Processes;

namespace Logvu.Core.Config;

/// <summary>
/// A logvu configuration file that has been read and validated, but whose logs directory is not resolved yet. Reading it runs nothing;
/// <see cref="ResolveAsync"/> runs the file's <c>envScript</c> (if it has one), expands <c>logsDirectory</c> and returns the
/// <see cref="LogvuConfig"/>. Two steps let the caller show the script's progress and cancel it.
/// </summary>
public sealed class LogvuConfigFile
{
    /// <summary>The largest number of stderr lines a script failure message shows: the last ones.</summary>
    private const int MaxStdErrLines = 10;

    private static readonly IReadOnlyDictionary<string, string> NoInputs = new Dictionary<string, string>();

    private readonly string _configDirectory;
    private readonly string _logsDirectoryTemplate;
    private readonly EnvScriptSpec? _envScript;
    private readonly Func<string, LogvuConfig> _finish;
    private readonly EnvScriptRunner _runner;

    /// <summary>Creates the file from the parts the loader validated. See <see cref="Read"/>.</summary>
    internal LogvuConfigFile(
        string configPath,
        string logsDirectoryTemplate,
        EnvScriptSpec? envScript,
        Func<string, LogvuConfig> finish,
        EnvScriptRunner runner)
    {
        ConfigPath = configPath;
        _configDirectory = Path.GetDirectoryName(configPath)!;
        _logsDirectoryTemplate = logsDirectoryTemplate;
        _envScript = envScript;
        _finish = finish;
        _runner = runner;
    }

    /// <summary>The full path of the configuration file.</summary>
    public string ConfigPath { get; }

    /// <summary>True when the file has an <c>envScript</c>, which <see cref="ResolveAsync"/> runs.</summary>
    public bool HasEnvScript => _envScript is not null;

    /// <summary>
    /// Reads, parses and validates the configuration file at <paramref name="configPath"/>, as <see cref="LogvuConfig.Load"/> does, but
    /// runs nothing. Finish it with <see cref="ResolveAsync"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="configPath"/> is <c>null</c> or empty.</exception>
    /// <exception cref="LogvuConfigException">The file cannot be read, is malformed, or a setting is missing, of the wrong type, unknown or invalid.</exception>
    public static LogvuConfigFile Read(string configPath) => Read(configPath, new EnvScriptRunner());

    /// <summary>As <see cref="Read(string)"/>, with the runner that will run the script (a test seam).</summary>
    internal static LogvuConfigFile Read(string configPath, EnvScriptRunner runner) => LogvuConfigLoader.Read(configPath, runner);

    /// <summary>
    /// Runs the <c>envScript</c>, if the file has one, then expands <c>logsDirectory</c> with the script's values on top of logvu's
    /// environment and resolves it against the file's folder. Without a script, it completes at once.
    /// </summary>
    /// <param name="cancellationToken">Stops the script: its process tree is killed, and the task ends cancelled.</param>
    /// <exception cref="LogvuConfigException">
    /// The script failed (the message has its error and the last lines of its stderr), a variable of <c>logsDirectory</c> is not set, or
    /// the expanded directory is not valid.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled while the script ran.</exception>
    public async Task<LogvuConfig> ResolveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?>? values = null;
        if (_envScript is { } script)
        {
            LaunchSpec launch = LaunchSpec.ForScript(
                script.Program,
                script.Args,
                script.Cwd,
                levels: [],
                levelCwd: _configDirectory,
                configDir: _configDirectory,
                inputs: NoInputs);

            EnvScriptResult result = await _runner.RunAsync(launch, script.Timeout, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new LogvuConfigException(FailureMessage(result));
            }

            values = result.Values;
        }

        return Finish(values);
    }

    /// <summary>
    /// Expands the logs directory with the script's values (a null value removes its variable) over logvu's environment, and builds
    /// the configuration. Pure apart from reading the environment.
    /// </summary>
    private LogvuConfig Finish(IReadOnlyDictionary<string, string?>? scriptValues)
    {
        // Windows variable names are case-insensitive, as they are in its environment block.
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var overlay = new Dictionary<string, string?>(comparer);
        if (scriptValues is not null)
        {
            foreach ((string name, string? value) in scriptValues)
            {
                overlay[name] = value;
            }
        }

        string? Lookup(string name) =>
            overlay.TryGetValue(name, out string? value) ? value : Environment.GetEnvironmentVariable(name);

        // An unset variable would silently leave a partial path (such as "/bin/..." for "${env:X}/bin/..."), so it is reported.
        foreach (string name in Placeholders.EnvNames(_logsDirectoryTemplate))
        {
            if (Lookup(name) is null)
            {
                throw Error("logsDirectory", $"variable '{name}' is not set");
            }
        }

        string expanded = Placeholders.Expand(
            _logsDirectoryTemplate,
            Lookup,
            NoInputs,
            cwd: _configDirectory,
            configDir: _configDirectory,
            Path.PathSeparator);

        if (string.IsNullOrWhiteSpace(expanded))
        {
            throw Error("logsDirectory", "must not be empty after expansion");
        }

        string logsDirectory;
        try
        {
            logsDirectory = Path.GetFullPath(Path.Combine(_configDirectory, expanded));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Error("logsDirectory", $"not a valid directory path: {ex.Message}");
        }

        return _finish(logsDirectory);
    }

    /// <summary>The failure message: the error, then the last lines of the script's stderr, indented.</summary>
    private string FailureMessage(EnvScriptResult result)
    {
        string message = $"Configuration file '{ConfigPath}': envScript: failed: {result.Error ?? "the script failed"}";
        string[] stdErrTail = result.StdErr.Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .TakeLast(MaxStdErrLines)
            .Select(line => "  " + line)
            .ToArray();
        return stdErrTail.Length == 0 ? message : message + "\n" + string.Join("\n", stdErrTail);
    }

    private LogvuConfigException Error(string jsonPath, string problem) =>
        new($"Configuration file '{ConfigPath}': {jsonPath}: {problem}");
}

/// <summary>The validated <c>envScript</c> setting of a configuration file.</summary>
/// <param name="Program">The executable of the script.</param>
/// <param name="Args">The arguments, with their placeholders as written.</param>
/// <param name="Cwd">The working directory as written, relative to the configuration file's folder; null for that folder.</param>
/// <param name="Timeout">How long the script may run before it is killed.</param>
internal sealed record EnvScriptSpec(string Program, IReadOnlyList<string> Args, string? Cwd, TimeSpan Timeout);
