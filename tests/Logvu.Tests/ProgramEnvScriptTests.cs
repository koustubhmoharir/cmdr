using System.Globalization;
using System.Text.Json;
using Logvu;
using Logvu.Core.Config;

namespace Logvu.Tests;

/// <summary>
/// The startup resolution of a config whose <c>envScript</c> runs, through <see cref="Program.ResolveConfig"/>: the progress line, the
/// value the script gives, and the messages of a failure and of a cancel. Script cases are Linux only. Every wait is bounded.
/// </summary>
public sealed class ProgramEnvScriptTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "logvu-program-" + Guid.NewGuid().ToString("N"));

    public ProgramEnvScriptTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void A_config_without_a_script_resolves_at_once_and_prints_nothing()
    {
        string path = Write(Config(envScript: null, "Logs"));
        var stderr = new StringWriter();

        LogvuConfig? config = Program.ResolveConfig(LogvuConfigFile.Read(path), stderr, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(Path.Combine(_dir, "Logs"), config.LogsDirectory);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void A_config_without_a_script_that_does_not_expand_is_reported_and_returns_null()
    {
        string path = Write(Config(envScript: null, "${env:LOGVU_NO_SUCH_VARIABLE_" + Guid.NewGuid().ToString("N") + "}/Logs"));
        var stderr = new StringWriter();

        LogvuConfig? config = Program.ResolveConfig(LogvuConfigFile.Read(path), stderr, CancellationToken.None);

        Assert.Null(config);
        Assert.StartsWith("logvu: Configuration file '", stderr.ToString());
        Assert.Contains("logsDirectory: variable", stderr.ToString());
    }

    [Fact]
    public void A_script_prints_the_running_line_and_its_value_reaches_the_logs_directory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string name = "LOGVU_PROGRAM_TEST_" + Guid.NewGuid().ToString("N");
        string path = Write(Config(Script("echo '{\"" + name + "\":\"rel/Logs\"}'"), "${env:" + name + "}"));
        var stderr = new StringWriter();

        LogvuConfig? config = Program.ResolveConfig(LogvuConfigFile.Read(path), stderr, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal(Path.Combine(_dir, "rel", "Logs"), config.LogsDirectory);
        Assert.Equal($"logvu: running envScript of {path}…{Environment.NewLine}", stderr.ToString());
    }

    [Fact]
    public void A_failing_script_prints_its_failure_and_returns_null()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string path = Write(Config(Script("echo 'boom' >&2; exit 1"), "Logs"));
        var stderr = new StringWriter();

        LogvuConfig? config = Program.ResolveConfig(LogvuConfigFile.Read(path), stderr, CancellationToken.None);

        Assert.Null(config);
        Assert.StartsWith($"logvu: running envScript of {path}…", stderr.ToString());
        Assert.Contains($"logvu: Configuration file '{path}': envScript: failed: exited with code 1", stderr.ToString());
        Assert.EndsWith("\n  boom" + Environment.NewLine, stderr.ToString());
    }

    [Fact]
    public async Task A_cancelled_script_prints_cancelled_returns_null_and_leaves_no_process()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string pidFile = Path.Combine(_dir, "pids");
        // The shell writes its pid and the pid of a child that sleeps; a cancel must kill both.
        string script = "echo $$ > '" + pidFile + "'; sleep 30 & echo $! >> '" + pidFile + "'; wait";
        string path = Write(Config(Script(script), "Logs"));
        var stderr = new StringWriter();
        using var cancel = new CancellationTokenSource();

        Task<LogvuConfig?> starting = Task.Run(() => Program.ResolveConfig(LogvuConfigFile.Read(path), stderr, cancel.Token));
        WaitUntil(() => File.Exists(pidFile) && File.ReadAllLines(pidFile).Length == 2, "the script to start");
        int[] pids = File.ReadAllLines(pidFile).Select(line => int.Parse(line, CultureInfo.InvariantCulture)).ToArray();

        cancel.Cancel();

        Assert.Null(await starting.WaitAsync(Bound));
        Assert.EndsWith("logvu: cancelled" + Environment.NewLine, stderr.ToString());
        foreach (int pid in pids)
        {
            WaitUntil(() => !IsAlive(pid), $"process {pid} to be killed");
        }
    }

    private string Write(string text)
    {
        string path = Path.Combine(_dir, "logvu.json");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The <c>envScript</c> object for a <c>sh</c> script with the given code, or the text <c>null</c> for no script.</summary>
    private static string? Script(string code) =>
        JsonSerializer.Serialize(new { program = "sh", args = new[] { "-c", code } });

    /// <summary>A configuration with the given <c>envScript</c> object (or none) and <c>logsDirectory</c> text.</summary>
    private static string Config(string? envScript, string logsDirectory) =>
        "{\n"
        + (envScript is null ? string.Empty : "  \"envScript\": " + envScript + ",\n")
        + "  \"logsDirectory\": " + JsonSerializer.Serialize(logsDirectory)
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
