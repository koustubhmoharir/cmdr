namespace Shared.Processes.Tests;

public class EnvScriptRunnerTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Success_ReturnsEveryValue_WithNullAndEmptyStringsKept()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '%s' '{"A":"x","B":null,"C":""}'""", dir.Path));

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, result.Values!.Count);
        Assert.Equal("x", result.Values["A"]);
        Assert.True(result.Values.ContainsKey("B"));
        Assert.Null(result.Values["B"]);
        Assert.Equal("", result.Values["C"]);
    }

    [Fact]
    public async Task Whitespace_AndByteOrderMark_AreTolerated()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '\357\273\277 \n\t{"A":"1"}\r\n  \n'""", dir.Path));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("1", result.Values!["A"]);
    }

    [Fact]
    public async Task EnvLevels_AreVisibleToTheScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var levels = new[] { new EnvLevel(new Dictionary<string, string?> { ["X"] = "hello world" }, dir.Path) };

        EnvScriptResult result = await RunAsync(ShSpec("""printf '{"Y":"%s"}' "$X" """, dir.Path, [], levels: levels));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("hello world", result.Values!["Y"]);
    }

    [Fact]
    public async Task EnvAndCwdPlaceholders_InArgs_AreExpanded()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var levels = new[] { new EnvLevel(new Dictionary<string, string?> { ["X"] = "v1" }, dir.Path) };

        EnvScriptResult result = await RunAsync(
            ShSpec("""printf '{"A":"%s","B":"%s"}' "$1" "$2" """, dir.Path, ["${env:X}", "${cwd}"], levels: levels));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("v1", result.Values!["A"]);
        Assert.Equal(Path.GetFullPath(dir.Path), result.Values["B"]);
    }

    [Fact]
    public async Task InputPlaceholders_InArgs_AreSubstituted()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var inputs = new Dictionary<string, string> { ["name"] = "typed" };

        EnvScriptResult result = await RunAsync(
            ShSpec("""printf '{"A":"%s"}' "$1" """, dir.Path, ["${input:name}"], inputs: inputs));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("typed", result.Values!["A"]);
    }

    [Fact]
    public async Task ScriptCwd_ResolvesRelativeToLevelCwd()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string levelCwd = Path.Combine(dir.Path, "base");
        string expected = Path.GetFullPath(Path.Combine(levelCwd, "sub"));
        Directory.CreateDirectory(expected);

        EnvScriptResult result = await RunAsync(ShSpec("""printf '{"P":"%s"}' "$(pwd -P)" """, levelCwd, [], cwd: "sub"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(expected, result.Values!["P"]);
    }

    [Fact]
    public async Task ScriptCwd_DefaultsToLevelCwd()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string levelCwd = Path.GetFullPath(dir.Path);

        EnvScriptResult result = await RunAsync(Sh("""printf '{"P":"%s"}' "$(pwd -P)" """, levelCwd));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(levelCwd, result.Values!["P"]);
    }

    [Fact]
    public async Task NonZeroExit_ReportsTheCode_KeepsStderr_AndOmitsStdout()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '%s' '{"A":"secret-value"}'; printf 'boom detail\n' >&2; exit 3""", dir.Path));

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal("exited with code 3", result.Error);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("boom detail", result.StdErr);
        Assert.DoesNotContain("secret-value", result.Error);
    }

    [Fact]
    public async Task Timeout_ReturnsPromptly_AndKillsTheTree()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string pidFile = Path.Combine(dir.Path, "pids");

        EnvScriptResult result = await RunAsync(
            Sh("""echo $$ > "$1"; sleep 30 & echo $! >> "$1"; wait""", dir.Path, pidFile),
            TimeSpan.FromMilliseconds(300));

        Assert.False(result.Succeeded);
        Assert.Equal("timed out after 0.3 s", result.Error);
        Assert.Null(result.ExitCode);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(5), $"took {result.Elapsed}");
        int[] pids = ReadPids(pidFile);
        Assert.Equal(2, pids.Length);
        foreach (int pid in pids)
        {
            AssertGone(pid);
        }
    }

    [Fact]
    public async Task LeftoverChildHoldingStdout_IsKilled_AndTheResultIsPrompt()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string pidFile = Path.Combine(dir.Path, "child");

        EnvScriptResult result = await RunAsync(Sh("""sleep 30 & echo $! > "$1"; printf '%s' '{"A":"1"}'""", dir.Path, pidFile));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("1", result.Values!["A"]);
        Assert.True(result.Elapsed < TimeSpan.FromMilliseconds(1500), $"took {result.Elapsed}");
        AssertGone(ReadPids(pidFile)[0]);
    }

    [Fact]
    public async Task Cancellation_KillsTheTree_AndCancelsTheTask()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string pidFile = Path.Combine(dir.Path, "pid");
        LaunchSpec spec = Sh("""echo $$ > "$1"; sleep 30""", dir.Path, pidFile);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<OperationCanceledException>(() => new EnvScriptRunner().RunAsync(spec, LongTimeout, cts.Token));

        AssertGone(ReadPids(pidFile)[0]);
    }

    [Fact]
    public async Task AlreadyCancelled_ThrowsWithoutStartingTheScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string marker = Path.Combine(dir.Path, "started");
        LaunchSpec spec = Sh("""echo x > "$1"; printf '{}'""", dir.Path, marker);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new EnvScriptRunner().RunAsync(spec, LongTimeout, cts.Token));

        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task OutputLargerThanOneMiB_FailsAndKillsTheScript()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(
            Sh("""while :; do printf 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'; done""", dir.Path));

        Assert.False(result.Succeeded);
        Assert.Equal("output is larger than 1 MiB", result.Error);
        Assert.Null(result.ExitCode);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(5), $"took {result.Elapsed}");
    }

    [Fact]
    public async Task EmptyOutput_IsNotAnObject()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("exit 0", dir.Path));

        Assert.False(result.Succeeded);
        Assert.Equal("output is not a JSON object: empty output", result.Error);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task InvalidJson_ReportsLineAndPosition()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '{"A":\n  x}'""", dir.Path));

        Assert.False(result.Succeeded);
        Assert.Equal("output is not a JSON object: invalid JSON at line 2, position 3", result.Error);
    }

    [Fact]
    public async Task Array_IsNotAnObject()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '[1]'""", dir.Path));

        Assert.Equal("output is not a JSON object: found an array", result.Error);
    }

    [Fact]
    public async Task NumberValue_IsRejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '{"A":1}'""", dir.Path));

        Assert.False(result.Succeeded);
        Assert.Equal("value of 'A' is not a string or null", result.Error);
    }

    [Fact]
    public async Task InvalidName_IsRejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '{"A=B":"x"}'""", dir.Path));

        Assert.Equal("invalid variable name 'A=B'", result.Error);
    }

    [Fact]
    public async Task DuplicateName_IsRejected()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""printf '{"A":"1","A":"2"}'""", dir.Path));

        Assert.Equal("duplicate variable name 'A'", result.Error);
    }

    [Fact]
    public async Task MissingWorkingDirectory_ReportsCouldNotStart()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string missing = Path.Combine(dir.Path, "does-not-exist");

        EnvScriptResult result = await RunAsync(Sh("""printf '{}'""", missing));

        Assert.False(result.Succeeded);
        Assert.StartsWith("could not start: ", result.Error, StringComparison.Ordinal);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task MissingProgram_IsReportedAsSetsidExit_NotAsStartFailure()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Linux launches through setsid, which starts and then fails to exec the program, so the run is an exit, not a start failure.
        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Spec("cmdr-no-such-program-xyz", [], dir.Path));

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal($"exited with code {result.ExitCode}", result.Error);
    }

    [Fact]
    public async Task StdErrTail_IsLimitedTo16KiB()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""head -c 100000 /dev/zero | tr '\0' x >&2; printf 'END' >&2; exit 1""", dir.Path));

        Assert.Equal("exited with code 1", result.Error);
        Assert.Equal(EnvScriptRunner.MaxStdErrChars, result.StdErr.Length);
        Assert.EndsWith("END", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StdinIsAtEndOfFile()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();

        EnvScriptResult result = await RunAsync(Sh("""cat > /dev/null; printf '%s' '{"A":"1"}'""", dir.Path));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("1", result.Values!["A"]);
    }

    [Fact]
    public async Task Pwsh_ConvertToJsonOfAHashtable_SetsTheValues()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/pwsh")) return;

        using var dir = new TempDir();
        LaunchSpec spec = Spec(
            "/usr/bin/pwsh",
            ["-NoProfile", "-NonInteractive", "-Command", "@{ A = 'x'; B = $null } | ConvertTo-Json -Compress"],
            dir.Path);

        EnvScriptResult result = await RunAsync(spec, TimeSpan.FromSeconds(60));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("x", result.Values!["A"]);
        Assert.Null(result.Values["B"]);
    }

    [Fact]
    public async Task KillFailure_IsAFailure_EvenWhenTheScriptPrintedValues()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var runner = new EnvScriptRunner(new KillFailurePlatform());

        EnvScriptResult result = await runner.RunAsync(Sh("""printf '{"A":"1"}'""", dir.Path), LongTimeout, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("could not stop its processes: kill refused", result.Error);
        Assert.Null(result.Values);
    }

    /// <summary>The Linux platform, with a group whose kill fails.</summary>
    private sealed class KillFailurePlatform : IProcessPlatform
    {
        private readonly IProcessPlatform _inner = new LinuxProcessPlatform();

        public PreparedLaunch Prepare(LaunchSpec spec) => _inner.Prepare(spec);

        public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited)
        {
            LaunchedTree tree = _inner.Launch(launch, onExited);
            return tree with { Group = new Group(tree.Group) };
        }

        private sealed class Group(IProcessGroup inner) : IProcessGroup
        {
            public void Interrupt() => inner.Interrupt();

            public void Kill() => throw new System.ComponentModel.Win32Exception(1, "kill refused");

            public int CountLiveProcesses() => inner.CountLiveProcesses();

            public void Detach() => inner.Detach();

            // The real tree goes when the test ends: nothing of the script is left behind.
            public void Dispose()
            {
                inner.Kill();
                inner.Dispose();
            }
        }
    }

    private static Task<EnvScriptResult> RunAsync(LaunchSpec spec, TimeSpan? timeout = null) =>
        new EnvScriptRunner().RunAsync(spec, timeout ?? LongTimeout, CancellationToken.None);

    /// <summary>Builds the launch of <c>sh -c script</c>, with <paramref name="args"/> as its positional parameters ($1, ...).</summary>
    private static LaunchSpec Sh(string script, string levelCwd, params string[] args) => ShSpec(script, levelCwd, args);

    private static LaunchSpec ShSpec(
        string script,
        string levelCwd,
        string[] args,
        IReadOnlyList<EnvLevel>? levels = null,
        IReadOnlyDictionary<string, string>? inputs = null,
        string? cwd = null) =>
        Spec("sh", ["-c", script, "sh", .. args], levelCwd, cwd, levels, inputs);

    private static LaunchSpec Spec(
        string program,
        IReadOnlyList<string> args,
        string levelCwd,
        string? cwd = null,
        IReadOnlyList<EnvLevel>? levels = null,
        IReadOnlyDictionary<string, string>? inputs = null)
    {
        return LaunchSpec.ForScript(
            program,
            [.. args],
            cwd,
            levels ?? Array.Empty<EnvLevel>(),
            levelCwd,
            levelCwd,
            inputs ?? new Dictionary<string, string>());
    }

    private static int[] ReadPids(string path)
    {
        Assert.True(File.Exists(path), $"the script did not write {path}");
        return File.ReadAllLines(path).Select(line => int.Parse(line, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    private static void AssertGone(int pid) =>
        ProcessTestHelpers.WaitUntil(() => !IsAlive(pid), $"process {pid} to be killed", timeout: TimeSpan.FromSeconds(5));

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
