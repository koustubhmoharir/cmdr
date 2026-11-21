using System.Globalization;
using System.Text.Json;
using Logvu.Core.Config;

namespace Logvu.Core.Tests.Config;

/// <summary>
/// Resolution of a configuration whose <c>envScript</c> runs a <c>sh</c> script (Linux only). Every wait is bounded: the scripts are
/// killed by their timeout, or by the cancellation the tests send.
/// </summary>
public sealed class LogvuConfigEnvScriptTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Script_values_reach_logs_directory_and_load_resolves_them()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(
            Script(["-c", """echo '{"LOGVU_TEST_V":"rel/Logs"}'"""]),
            "${env:LOGVU_TEST_V}"));

        LogvuConfig config = LogvuConfig.Load(path);

        Assert.Equal(dir.Full("rel/Logs"), config.LogsDirectory);
    }

    [Fact]
    public async Task Script_value_overrides_the_process_environment()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_OVERRIDE_" + Guid.NewGuid().ToString("N");
        string path = dir.Write("logvu.json", Config(
            Script(["-c", $"echo '{{\"{name}\":\"from-script\"}}'"]),
            $"${{env:{name}}}"));

        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "from-process");

            Assert.Equal(dir.Full("from-script"), (await ResolveAsync(path)).LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public async Task Null_script_value_removes_the_variable_and_is_reported_as_unset()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_REMOVED_" + Guid.NewGuid().ToString("N");
        string path = dir.Write("logvu.json", Config(
            Script(["-c", $"echo '{{\"{name}\":null}}'"]),
            $"${{env:{name}}}/Logs"));

        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "from-process");

            LogvuConfigException ex = await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path));

            Assert.Equal($"Configuration file '{Path.GetFullPath(path)}': logsDirectory: variable '{name}' is not set", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public async Task Script_arguments_take_environment_placeholders_from_logvu()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string name = "LOGVU_TEST_ARG_" + Guid.NewGuid().ToString("N");
        string path = dir.Write("logvu.json", Config(
            Script(["-c", """printf '{"A":"%s"}' "$1" """, "sh", $"${{env:{name}}}"]),
            "${env:A}"));

        string? previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "from-env");

            Assert.Equal(dir.Full("from-env"), (await ResolveAsync(path)).LogsDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Fact]
    public async Task Script_values_are_used_as_they_are_and_not_expanded_again()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(
            // "$${" is an escape in the arguments, so the script prints the text ${env:HOME}, which must not be expanded again.
            Script(["-c", """echo '{"A":"$${env:HOME}"}'"""]),
            "${env:A}"));

        Assert.Equal(dir.Full("${env:HOME}"), (await ResolveAsync(path)).LogsDirectory);
    }

    [Fact]
    public async Task Failing_script_reports_its_error_and_the_last_stderr_lines()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(
            Script(["-c", "echo 'boom one' >&2; echo 'boom two' >&2; exit 1"]),
            "Logs"));

        LogvuConfigException ex = await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path));

        Assert.Equal(
            $"Configuration file '{Path.GetFullPath(path)}': envScript: failed: exited with code 1\n  boom one\n  boom two",
            ex.Message);
    }

    [Fact]
    public async Task Stderr_of_a_failing_script_is_limited_to_its_last_ten_lines()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(
            Script(["-c", "i=1; while [ $i -le 12 ]; do echo \"line $i\" >&2; i=$((i+1)); done; exit 1"]),
            "Logs"));

        string message = (await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path))).Message;

        Assert.DoesNotContain("  line 2\n", message);
        Assert.Contains("\n  line 3\n", message);
        Assert.EndsWith("\n  line 12", message);
    }

    [Fact]
    public async Task Output_that_is_not_a_json_object_fails_the_script()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(Script(["-c", "echo 'not json'"]), "Logs"));

        LogvuConfigException ex = await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': envScript: failed: output is not a JSON object", ex.Message);
    }

    [Fact]
    public async Task Script_that_runs_past_its_timeout_fails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(Script(["-c", "sleep 30"], timeoutSeconds: 1), "Logs"));

        LogvuConfigException ex = await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path));

        Assert.StartsWith($"Configuration file '{Path.GetFullPath(path)}': envScript: failed: timed out after 1 s", ex.Message);
    }

    [Fact]
    public async Task Script_whose_working_directory_is_missing_fails_to_start()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string path = dir.Write("logvu.json", Config(Script(["-c", "echo '{}'"], cwd: "missing"), "Logs"));

        LogvuConfigException ex = await Assert.ThrowsAsync<LogvuConfigException>(() => ResolveAsync(path));

        Assert.Contains("envScript: failed: could not start", ex.Message);
    }

    [Fact]
    public async Task Script_cwd_is_relative_to_the_config_folder_and_seen_by_the_script()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        Directory.CreateDirectory(dir.Full("scripts"));
        string path = dir.Write("logvu.json", Config(
            Script(["-c", """echo "{\"A\":\"$(basename "$(pwd)")\"}" """], cwd: "scripts"),
            "${env:A}/Logs"));

        Assert.Equal(dir.Full("scripts/Logs"), (await ResolveAsync(path)).LogsDirectory);
    }

    [Fact]
    public async Task Cwd_and_config_dir_placeholders_in_script_arguments_are_expanded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        Directory.CreateDirectory(dir.Full("scripts"));
        string cwdPath = dir.Full("scripts");
        string cwdConfig = dir.Write("Config/logvu.json", Config(
            Script(["-c", """echo "{\"A\":\"${cwd}\"}" """], cwd: "../scripts"),
            "${env:A}"));
        string configDirPath = dir.Write("Other/logvu.json", Config(
            Script(["-c", """echo "{\"A\":\"${configDir}\"}" """]),
            "${env:A}/Logs"));

        Assert.Equal(cwdPath, (await ResolveAsync(cwdConfig)).LogsDirectory);
        Assert.Equal(dir.Full("Other/Logs"), (await ResolveAsync(configDirPath)).LogsDirectory);
    }

    [Fact]
    public async Task Cancelling_the_resolution_kills_the_script_tree()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string pidFile = dir.Full("pids");
        string script = "echo $$ > '" + pidFile + "'; sleep 30 & echo $! >> '" + pidFile + "'; wait";
        string path = dir.Write("logvu.json", Config(Script(["-c", script]), "Logs"));

        using var cancel = new CancellationTokenSource();
        Task<LogvuConfig> resolving = LogvuConfigFile.Read(path).ResolveAsync(cancel.Token);

        WaitUntil(() => File.Exists(pidFile) && File.ReadAllLines(pidFile).Length == 2, "the script to start");
        int[] pids = File.ReadAllLines(pidFile).Select(line => int.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving).WaitAsync(Bound);
        foreach (int pid in pids)
        {
            WaitUntil(() => !IsAlive(pid), $"process {pid} to be killed");
        }
    }

    /// <summary>A configuration file resolution, bounded by <see cref="Bound"/>.</summary>
    private static async Task<LogvuConfig> ResolveAsync(string path)
    {
        using var cancel = new CancellationTokenSource(Bound);
        return await LogvuConfigFile.Read(path).ResolveAsync(cancel.Token).ConfigureAwait(false);
    }

    /// <summary>The <c>envScript</c> object for a <c>sh</c> script with the given arguments.</summary>
    private static string Script(string[] args, string? cwd = null, int? timeoutSeconds = null) =>
        JsonSerializer.Serialize(new { program = "sh", args, cwd, timeoutSeconds });

    /// <summary>A configuration with the given <c>envScript</c> object and <c>logsDirectory</c> text.</summary>
    private static string Config(string envScript, string logsDirectory) =>
        "{\n  \"envScript\": " + envScript
        + ",\n  \"logsDirectory\": " + JsonSerializer.Serialize(logsDirectory)
        + ",\n  \"filePrefix\": \"SheetKraft\",\n  \"fileNamePattern\": \"^SheetKraft(?:[0-9]+)?[.]log$\"\n}";

    private static void WaitUntil(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {description}.");
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>True while the process exists and is not a zombie (its state is read from /proc).</summary>
    private static bool IsAlive(int pid)
    {
        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (IOException)
        {
            return false;
        }

        int close = stat.LastIndexOf(')');
        return close < 0 || stat[close + 2] is not ('Z' or 'X');
    }
}
