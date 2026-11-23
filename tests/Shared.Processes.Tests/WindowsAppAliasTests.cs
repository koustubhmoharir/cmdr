using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit.Abstractions;
using static Shared.Processes.Tests.ProcessTestHelpers;

namespace Shared.Processes.Tests;

/// <summary>
/// Windows only: app execution alias detection, and a diagnostic matrix that starts the Microsoft Store pwsh alias with each of the four
/// create strategies. The matrix is written to the test output and included in the failure message, so one run on Windows is enough to
/// diagnose a failure. On other systems, and where the alias is not installed, these tests return early.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("Windows processes")]
public class WindowsAppAliasTests
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(20); // pwsh is slow to start
    private static readonly TimeSpan OutputTimeout = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    public WindowsAppAliasTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void OrdinaryFileIsNotAnAppExecLink()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        string path = dir.File("plain.exe");
        File.WriteAllText(path, "not a program");

        Assert.False(WindowsAppAlias.IsAppExecLink(path), $"an ordinary file is reported as an app execution alias: {path}");
    }

    [Fact]
    public void MissingPathIsNotAnAppExecLink()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        string path = dir.File("missing.exe");

        Assert.False(WindowsAppAlias.IsAppExecLink(path), $"a missing path is reported as an app execution alias: {path}");
    }

    [Fact]
    public void StorePwshAliasIsAnAppExecLink()
    {
        if (!OperatingSystem.IsWindows()) return;

        string alias = StorePwshAlias();
        if (!File.Exists(alias)) return;

        Assert.True(WindowsAppAlias.IsAppExecLink(alias), $"the pwsh app execution alias is not detected: {alias}");
    }

    [Fact]
    public async Task StorePwshAliasLaunchesWithEachStrategyAndAutomatically()
    {
        if (!OperatingSystem.IsWindows()) return;

        string alias = StorePwshAlias();
        if (!File.Exists(alias)) return;

        var platform = new WindowsProcessPlatform();
        using var dir = new TempDir();
        var matrix = new StringBuilder();
        matrix.AppendLine($"alias: {alias} (IsAppExecLink={WindowsAppAlias.IsAppExecLink(alias)})");

        var strategies = new (string Name, WindowsCreateStrategy Strategy)[]
        {
            ("Default", WindowsCreateStrategy.Default),
            ("PassApplicationName only", new WindowsCreateStrategy(PassApplicationName: true, JobListAttribute: false)),
            ("JobListAttribute only", new WindowsCreateStrategy(PassApplicationName: false, JobListAttribute: true)),
            ("AppAlias", WindowsCreateStrategy.AppAlias),
        };

        foreach ((string name, WindowsCreateStrategy strategy) in strategies)
        {
            Attempt attempt = await AttemptAsync(
                $"{name} (PassApplicationName={strategy.PassApplicationName}, JobListAttribute={strategy.JobListAttribute})",
                onExited => platform.Launch(platform.Prepare(AliasSpec(alias, dir.Path)), onExited, strategy));
            matrix.AppendLine(attempt.Describe());
        }

        Attempt automatic = await AttemptAsync(
            "public Launch (strategy picked automatically)",
            onExited => platform.Launch(platform.Prepare(AliasSpec(alias, dir.Path)), onExited));
        matrix.AppendLine(automatic.Describe());

        string text = matrix.ToString();
        _output.WriteLine(text);

        Assert.True(
            automatic.Started && automatic.ExitCode == 0 && automatic.Stdout.Contains("alias-ok", StringComparison.Ordinal)
                && automatic.InJob == "true",
            "The public Launch did not run the pwsh alias in its job to a successful exit with \"alias-ok\" on stdout. Matrix:\n" + text);
    }

    private static LaunchSpec AliasSpec(string alias, string workingDirectory) =>
        Exec(
            alias,
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Write-Output alias-ok" },
            workingDirectory);

    /// <summary>The Store pwsh alias for the current user, which may not be installed.</summary>
    private static string StorePwshAlias() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "WindowsApps",
        "pwsh.exe");

    /// <summary>
    /// Starts one launch with <paramref name="start"/>, reads its output to the end, waits for its exit, records whether it was in its job,
    /// then kills and disposes the group. A failed start is recorded with its exception type, message and Win32 error code.
    /// </summary>
    private static async Task<Attempt> AttemptAsync(string label, Func<Action<ILaunchedProcess>, LaunchedTree> start)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LaunchedTree tree;
        try
        {
            tree = start(_ => exited.TrySetResult());
        }
        catch (Exception ex)
        {
            return new Attempt(label, $"not started: {Describe(ex)}", false, null, "", "", "not applicable");
        }

        try
        {
            string inJob = InJobText(tree.Process.Id, tree.Group);
            Task<string> stdoutTask = tree.Process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = tree.Process.StandardError.ReadToEndAsync();

            bool finished = await Within(exited.Task, ExitTimeout);
            if (finished)
            {
                await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(OutputTimeout));
            }

            string outcome = finished ? "started" : $"started, did not exit within {ExitTimeout.TotalSeconds} s";
            return new Attempt(
                label,
                outcome,
                true,
                finished ? tree.Process.ExitCode : null,
                OutputText(stdoutTask),
                OutputText(stderrTask),
                inJob);
        }
        finally
        {
            try
            {
                tree.Group.Kill();
            }
            catch (Exception)
            {
                // Closing the job below still kills what is left (kill on close).
            }

            tree.Group.Dispose();
            tree.Process.Dispose();
        }
    }

    /// <summary>
    /// Whether the started process is in the group's job, checked right after the launch. A process that has already exited is
    /// reported as such, because its membership can no longer be read.
    /// </summary>
    private static string InJobText(int pid, IProcessGroup group)
    {
        if (group is not WindowsProcessGroup windowsGroup)
        {
            return "not applicable";
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return "exited before check";
            }

            int ok = WindowsNative.IsProcessInJob(process.Handle, windowsGroup.Job.DangerousGetHandle(), out int result);
            if (ok == 0)
            {
                return $"IsProcessInJob failed (Win32 error {Marshal.GetLastPInvokeError()})";
            }

            return result != 0 ? "true" : "false";
        }
        catch (ArgumentException)
        {
            return "exited before check";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"error({ex.Message})";
        }
    }

    private static string OutputText(Task<string> read)
    {
        if (read.IsCompletedSuccessfully)
        {
            return read.Result;
        }

        return read.IsFaulted ? $"[read failed: {read.Exception?.GetBaseException().Message}]" : "[not read to EOF]";
    }

    private static string Describe(Exception ex) => ex is Win32Exception win32
        ? $"{ex.GetType().Name} (Win32 error {win32.NativeErrorCode}): {ex.Message}"
        : $"{ex.GetType().Name}: {ex.Message}";

    private static async Task<bool> Within(Task task, TimeSpan timeout) => await Task.WhenAny(task, Task.Delay(timeout)) == task;

    private static string Quote(string text) =>
        "\"" + text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    /// <summary>One launch's outcome, as one line of the matrix.</summary>
    private sealed record Attempt(
        string Label,
        string Outcome,
        bool Started,
        int? ExitCode,
        string Stdout,
        string Stderr,
        string InJob)
    {
        public string Describe() =>
            $"{Label}: {Outcome}; in job: {InJob}; exit code: {(ExitCode is int code ? code.ToString(CultureInfo.InvariantCulture) : "n/a")}; " +
            $"stdout: {Quote(Stdout)}; stderr: {Quote(Stderr)}";
    }
}
