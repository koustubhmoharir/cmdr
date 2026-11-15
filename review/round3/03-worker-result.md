# Worker 03 handoff: Windows process platform static review

Reviewed HEAD `a27a7ce65b3e3338a18596d700f82dcc5f982e7c` on `review-fixes`, 2026-11-15. Repository status was clean before and after the work.

This was the requested **read-only review**, not an implementation phase. No production fixes or repository tests were edited. All probe projects, generated build/test artifacts, and probe output are under `/tmp/cmdr-claude-run-review2/probe-03/`. No Windows API was executed. Host tools: .NET SDK 10.0.107, PowerShell 7.6.2, Linux x64.

## 1. Findings (A), most severe first

No confirmed P1 finding. Four P2 findings (one in the test harness) and one P3 finding follow. These are distinct from the accepted limitations in `review/TRACKER.md`. Native Windows consequences are distinguished from actual probe results.

### W03-01 — P2 — NUL in an argument silently truncates the native command line

- **Location at HEAD:** `src/Cmdr.Core/Processes/WindowsCommandLine.cs:27`, `:49`, `:63`; native handoff at `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:295` and `:344`.
- **Defect:** The Windows command-line builder accepts embedded NULs and copies them into the UTF-16 buffer, so CreateProcessW receives only the prefix through the first NUL instead of rejecting an unrepresentable argument.
- **Concrete scenario:** Configure an existing `tool.exe` with args `["before", "x\u0000hidden", "after"]`. Config validation does not reject the argument. `Build` emits `tool.exe before x<NUL>hidden after`; the child receives `before`, `x`, and no `after` argument. A command can therefore execute successfully with missing options instead of failing to start. For a batch launch, the same truncation also removes the remainder of the raw `/c` command.
- **Evidence:** A source-linked probe called the actual `WindowsCommandLine.Build`, allocated the resulting UTF-16 string and read its native NUL-terminated interpretation:

  ```text
  NUL managed="C:\\bin\\tool.exe before x\u0000hidden after"
  NUL native="C:\\bin\\tool.exe before x"
  ```

  `AllocUnicode` deliberately preserves all embedded NULs because the same allocator serves environment blocks; that is appropriate for an environment block but not a command line. The native boundary is a writable NUL-terminated string, not a counted string. See Microsoft's [CreateProcessW contract](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw). No real child was launched on Windows.
- **Confidence:** High.
- **Suggested fix:** Reject NUL in file names, individual arguments and raw command-line text before allocating native resources, with an argument-index diagnostic. Keep the environment-block allocator's embedded-NUL support. Add pure regressions for NUL at the beginning/middle/end of an argument, subsequent arguments, and the batch path. Rejection is necessary because Win32 argv cannot represent NUL; silently deleting it is not lossless.

### W03-02 — P2 — A quoted `--%` still changes PowerShell's native argument passing

- **Location at HEAD:** `src/Cmdr/Tui/CommandLineFormatter.cs:70` and `:100` (the per-argument PowerShell formatting path).
- **Defect:** Formatting a literal native argument `--%` as `'--%'` does not protect it from PowerShell's native binder, which consumes the marker and changes the remaining arguments.
- **Concrete scenario:** Copy a direct native command with args `["--%", "a b", "%CMDR_PROBE%"]`, with `CMDR_PROBE=EXPANDED` in the shell. The intended first argument disappears; the second becomes two arguments; the last becomes `EXPANDED`. This affects the ordinary executable path, independently of the documented Legacy empty-string/double-quote limitations.
- **Evidence:** The probe used the actual formatter to invoke `/usr/bin/printf` through installed pwsh, once with `Standard` and once with `Legacy` native passing:

  ```text
  formatted: /usr/bin/printf '<%s followed by LF>' '--%' 'a b' '%CMDR_PROBE%'
  expected stdout: --%\na b\n%CMDR_PROBE%\n
  Standard actual stdout: a\nb\nEXPANDED\n
  Legacy actual stdout:   a\nb\nEXPANDED\n
  both exit codes: 0; both stderr: empty
  ```

  The actual exact JSON-escaped formatted string is in `probe-03/output.txt`; the printf format is `%s` plus an actual LF, so the display above abbreviates only that format token. Microsoft's [NativeCommandParameterBinder source](https://raw.githubusercontent.com/PowerShell/PowerShell/v7.4.0/src/System.Management.Automation/engine/NativeCommandParameterBinder.cs) checks the argument's string value for `--%` before checking its quoting, then expands environment variables and splits subsequent values. This is an inference for native Windows from the shared binder plus a real Linux pwsh reproduction; Windows PowerShell 5.1 was not available.
- **Confidence:** High for the demonstrated PowerShell 7 defect; 5.1 requires its own regression run.
- **Suggested fix:** For commands containing this exact argument, pass the native arguments as an array expression rather than separate string-valued binder parameters. A narrow probe of `& /usr/bin/printf @('%s\n','--%','a b','%CMDR_PROBE%')` preserved all three arguments. Retain the ordinary readable form for unaffected commands. Test the proposed form on Windows PowerShell 5.1 and PowerShell 7, including Legacy mode and the existing quoting cases. Merely adding more quotes around `--%` does not fix it.

### W03-03 — P2 — Batch launches from a UNC working directory run in the Windows directory

- **Location at HEAD:** `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:74`, `:309`; `src/Cmdr.Core/Processes/WindowsCommandResolver.cs:271` (batch plan, with raw arguments built at `:275`).
- **Defect:** A batch launch forwards a UNC cwd directly to cmd.exe without handling cmd's rejection of UNC current directories, so the batch and its children use a different cwd from the configured one.
- **Concrete scenario:** Open a project at `\\server\share\project`, use cwd there, and run `run.cmd` containing `@type relative.txt`, with the intended file in that project. Resolution correctly selects the absolute network batch path, but cmd.exe defaults its current directory to `C:\Windows` on an ordinary installation. The script reads `C:\Windows\relative.txt` or fails to find it. A package-manager shim similarly looks for project files in the wrong directory.
- **Evidence:** The actual pure resolver and launch plan produced:

  ```text
  resolved: \\server\share\project\run.CMD
  interpreter: C:\Windows\System32\cmd.exe
  arguments: /d /s /c "\\server\share\project\run.CMD ^"relative.txt^""
  ```

  `CreateInJob` passes `psi.WorkingDirectory` unchanged. There is no mapping, cwd prefix or explicit rejection in the batch plan. Microsoft's [explanation of cmd UNC current directories](https://devblogs.microsoft.com/oldnewthing/20070215-05/?p=28003) describes the restriction; a recent [firsthand Windows reproduction in the Microsoft Terminal repository](https://github.com/microsoft/terminal/issues/19514) shows the fallback on Windows 11. Microsoft's [pushd documentation](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/pushd) describes its temporary drive mapping for a network directory. **Static Windows finding, not reproduced here.** This is separate from the already-fixed forward-slash UNC *resolution* bug.
- **Confidence:** High for the default cmd behavior and missing handling; no claim about machines with a nondefault DisableUNCCheck registry setting.
- **Suggested fix:** Decide whether to support UNC batch cwd via a carefully owned temporary drive/cmd `pushd` wrapper, preserving exit status and cleanup, or fail explicitly before launch with a clear unsupported-cwd diagnostic. Do not report a successful launch with a silently changed cwd. Add a native network-share regression that verifies both the batch's cwd and a relative file read. The desired support policy is an open question below; no mapping implementation was assumed or made.

### W03-04 — P2 — The containment test helper kills unrelated PING processes

- **Location at HEAD:** `tests/Cmdr.Core.Tests/Processes/WindowsJobContainmentTests.cs:423`, `:543`, `:569`, `:734`.
- **Defect:** The helper adopts every machine-wide PING process started near the test's start and later kills it, including processes explicitly found outside the test's job.
- **Concrete scenario:** A developer starts a diagnostic `ping -t host` within two seconds before, or during, `EarlyChildIsInTheJobFromCreation`. `TrackPingsInJob` sees its name and recent StartTime, records it even with `inJob=false`, and `Probe.Dispose` calls `Kill()` on it. Disabling xUnit parallelism does not isolate the test from the user's processes or other applications.
- **Evidence:** `_since` is `DateTime.Now.AddSeconds(-2)`; selection uses only process name and StartTime; `_pings.Add` happens before the membership conditional; disposal kills every record, with no ownership check. This is direct source control flow. I did not execute these tests on Linux or deliberately kill an unrelated process.
- **Confidence:** High.
- **Suggested fix:** Have a purpose-built fixture write its exact child PID into a unique per-test file or pipe, then open and retain that process handle independently of job membership. Clean up only that child, including if it escapes the job. Use machine-wide enumeration for diagnostics only. A parent/child handshake can also make the early-child test deterministic. Do not solve this by cleaning up only job members, which would lose the independent escaped-child cleanup.

### W03-05 — P3 — Copied batch commands overwrite and delete an existing shell environment variable

- **Location at HEAD:** `src/Cmdr/Tui/CommandLineFormatter.cs:47`.
- **Defect:** The generated batch-copy snippet unconditionally replaces `CMDR_CMD` and then removes it rather than restoring the previous shell state.
- **Concrete scenario:** The user's PowerShell session has `$env:CMDR_CMD='original'`. Pasting a copied batch command with `a&b` makes that value unavailable to the batch and removes it from the user's session when the command completes. A configured environment inherited under that name also cannot retain its original value in the copied invocation.
- **Evidence:** The actual formatter's output was evaluated in pwsh with `function cmd { }` standing in for the Windows command. The function intentionally does nothing; only the surrounding session mutation is tested:

  ```text
  initial CMDR_CMD: original
  generated: $env:CMDR_CMD = 'C:\bin\run.cmd ^"a^&b^"'; cmd /d /c '%CMDR_CMD%'; Remove-Item Env:CMDR_CMD
  observed output after execution: AFTER=
  stderr: empty
  ```

  This is a real PowerShell state-mutation reproduction, not a cmd parsing or batch-execution test.
- **Confidence:** High.
- **Suggested fix:** Save both existence and value, restore in `finally`, and scope temporary PowerShell variables so they do not clobber caller variables. If preserving a preexisting `CMDR_CMD` for the child is required, use a temporary variable name chosen to avoid existing environment names. Add tests for present/absent variables and a terminating failure during invocation.

## 2. Organization and pattern proposals (B), ranked

These are bounded follow-up changes to the existing implementation, not a new design phase. Costs are relative: S small/local, M several files plus regressions, L a broader ownership change. Priorities indicate implementation order.

### B1 — Priority 1: An owned Windows test-process fixture with explicit child identity

- **Bug class prevented:** Killing unrelated processes (W03-04), leaked test pipes, and tests that measure supervisor bookkeeping instead of actual child liveness. Earlier 03-01 and its original tests demonstrate why independently held child identity matters.
- **Files affected:** `WindowsJobContainmentTests.cs`, `WindowsProcessPlatformTests.cs`, a small Windows test fixture/helper project and shared test ownership helper.
- **Sketch:** One `IAsyncDisposable` owner takes the launched tree, all three streams, drain tasks, independently opened child handles and a unique PID-report channel. A fixture reports its own child's PID before exiting. Cleanup kills only explicitly owned processes, closes stdin, bounds/observes reader completion, and closes the process/group even if an assertion fails. Machine-wide process listings remain diagnostic only. Use this owner in the detach test too.
- **Cost/risk:** M; low production risk, medium test migration risk. Do this before asking developers to run the native containment stress tests.

### B2 — Priority 1: Validated boundary types for argv and cmd command text

- **Bug class prevented:** Unrepresentable strings silently crossing Win32 boundaries (W03-01), accidental second quoting, raw-arguments/list conflicts, and assuming arbitrary data can always be encoded as cmd syntax. Past 06-05 and the current W03-02 show that string quoting alone does not establish shell semantics.
- **Files affected:** `WindowsCommandLine.cs`, `WindowsCmdEscaping.cs`, `WindowsCommandResolver.cs` (`WindowsLaunchPlan`), `WindowsProcessPlatform.cs`, `CommandLineFormatter.cs`, corresponding pure tests.
- **Sketch:** Replace the plan's nullable raw string plus simultaneously available argument list with a closed union of direct-native argv and batch invocation. Constructors validate NUL and required path properties. Distinguish a validated native command line from a double-NUL environment block so a shared allocator cannot imply shared validation. Keep native serialization and PowerShell source serialization separate, with explicit handling for binder markers. Preserve command argument indexes in errors.
- **Cost/risk:** M; medium because it crosses launch/copy boundaries. It can be staged with NUL validation first (S).

### B3 — Priority 2: One pure Windows launch policy, with explicit platform inputs

- **Bug class prevented:** Divergent execution versus copy resolution/escaping, cwd policy omissions (W03-03), platform-dependent pure-test behavior, and the past 03-02/03-03/03-04 path/fallback defects.
- **Files affected:** `WindowsCommandResolver.cs`, `WindowsBatchCommandLine.cs`, `WindowsProcessPlatform.cs`, `EnvExpansion.cs`, `CommandLineFormatter.cs`, `WindowsLaunchPlanTests.cs`, resolver/copy tests.
- **Sketch:** Pass an explicit Windows context (environment comparer, system directory, file predicate, absolute cwd) to one pure preparation function. Return a typed direct/batch decision, interpreter, stdin mode, post-interrupt answer and explicit cwd policy/result. Launch consumes it; copy uses the same resolved batch decision, then applies its shell-specific representation. Centralize fully-qualified/root-relative/drive-relative classification rather than having execution rely on `Path.IsPathFullyQualified` while planning uses a separate predicate. Avoid discovering the live filesystem inside the supposedly pure formatter; resolve in its caller and pass a decision.
- **Cost/risk:** M; medium. Preserve existing documented cwd-first, PATHEXT and drive-relative policies with table-driven tests rather than changing them during extraction.

### B4 — Priority 2: Typed native results and narrowly owned native allocations

- **Bug class prevented:** Raw-handle lifetime mistakes, overwriting last-error information during cleanup, and failure-path leaks. The prior client-pipe-copy and duplicated-exit-handle fixes are concrete evidence of this area's fragility.
- **Files affected:** `WindowsNative.cs`, `WindowsProcessPlatform.cs`, `WindowsProcessGroup.cs`, `WindowsLaunchedProcess.cs`, `WindowsSafeJobHandle.cs`.
- **Sketch:** Keep job/process parameters as SafeHandle at P/Invoke boundaries where feasible; wrap a process-creation result immediately in an owned aggregate for process/thread handles. Give attribute-list initialization and each allocation one disposable owner and explicit transfer. Native wrappers return `(success, error)` captured immediately inside the SafeHandle lease. `ListProcessIds` already captures error inside `UseJob`; `Kill`/`Detach` currently read it after `DangerousRelease`, which could itself close the job during concurrent disposal. Add a small native-call seam to fault each creation/duplication/attribute step and assert complete cleanup without needing a real Windows failure.
- **Cost/risk:** M; medium. Keep the existing duplicate exit-wait handle and nonblocking unregister behavior; do not undo their correct ownership fix.

### B5 — Priority 2: Explicit operation lifetime for helper dispatch

- **Bug class prevented:** Signals dispatched after a run is released, PID-only targeting, and incomplete helper cleanup on timeout/start/read failure; these belong to the same lifetime/concurrency class as the previous run-owner and wait-handle bugs.
- **Files affected:** `WindowsProcessGroup.cs`, `WindowsLaunchedProcess.cs`, `CtrlCHelperCommand.cs`, relevant `ManagedProcess` signal/release boundaries and native tests.
- **Sketch:** A group operation acquires a lease tied to the original leader's owned process identity before scheduling/starting the helper; group release revokes new dispatches. Do not hold a lifecycle lock while waiting for the helper. An owned helper runner drains stderr, applies one deadline, kills and reaps on timeout and reports a structured outcome. Inject that runner to test timeout/failure/disposal interleavings deterministically. Define whether an already-dispatched stop may complete after detach before changing semantics (see questions).
- **Cost/risk:** M; medium. This is a preventive ownership proposal; no PID-reuse incident was reproduced and it is not an additional confirmed finding.

### B6 — Priority 3: Visible platform exclusions and a semantic argument corpus

- **Bug class prevented:** False green Windows coverage and tests that only duplicate the escaping implementation. Past native-validation gaps remain open after several review rounds; W03-01/W03-02 pass the present suite.
- **Files affected:** All scoped Windows test classes, `CommandLineFormatterTests.cs`, Windows fixture from B1, shared test attributes/data.
- **Sketch:** Use explicit Windows-only facts/theories and visible optional-tool skips. Share a corpus of argument values across direct-exe, plain batch, node shim, PS7 and PS5.1 tests. Assert argv as length-prefixed/JSON values, plus cwd, exit code and environment, using a real argument-echo fixture. Keep source-string tests for precise encoding but require independent native semantic tests. Include exact `--%`, NUL rejection, CR/LF, tabs, Unicode, percent/bang values, empty strings, quotes and trailing slashes. Each host runs only cases it can establish and records what was excluded.
- **Cost/risk:** M; low production risk, medium Windows fixture/CI setup cost. The visibility-only change is S.

## 3. Test-quality issues

1. **Unsafe process ownership:** W03-04 above is the single finding for machine-wide adoption/killing; it is not counted again here. Independent PID reporting is required before broad native execution.
2. **Invisible exclusions:** All eight facts in `WindowsProcessPlatformTests` and all eight in `WindowsJobContainmentTests` use successful early returns on Linux. The class comments call these skipped, but xUnit records passes. `WindowsJobStructTests` similarly returns on non-64-bit hosts. `CommandLineFormatterTests.cs:174` and `:201` return when optional programs are absent. None is evidence of native Windows validity. The formatter native round trip specifically requires `/usr/bin/printf`, and `FindOnPath("pwsh")` never appends `.exe`, so its current discovery is unsuitable for Windows validation even when pwsh.exe is installed.
3. **Incomplete resource cleanup in tests:** `WindowsJobContainmentTests.cs:709` disposes group/process but never the tree's stdin writer. Readers are disposed only inside drain tasks; those tasks are not retained/awaited by the owner, and a failure before starting them leaves readers open until GC. `WindowsProcessPlatformTests.cs:154` disposes the process in the detached-group test but none of its streams; this is especially relevant because `ILaunchedProcess.Dispose` explicitly does not own streams. The group is detached and disposed in the try body, with no unconditional group disposal if detaching throws. Use B1 rather than relying on finalizers.
4. **The claimed formatter timeout does not bound execution:** `CommandLineFormatterTests.cs:226` calls synchronous `ReadToEnd()` before `WaitForExit(60_000)`. If pwsh hangs with stdout open, the timeout is never reached; if the assertion times out, disposing `Process` does not kill the child. Drain asynchronously with cancellation/deadline, then kill and reap in finally, and assert the exit code. Current helpers also ignore nonzero exit status.
5. **String expectations are not an independent cmd oracle:** `WindowsCmdEscapingTests` checks expected caret strings, `WindowsLaunchPlanTests` often compares a result to `BuildCmdArguments`, and the batch formatter test checks only the variable assignment. These are useful wiring tests but do not establish that a batch `%1`/`%*` forwarding layer or npm shim receives the intended argv. No current test executes the real CtrlCHelper signal flow end to end. The timeout/handle-inheritance test deliberately uses a stand-in helper, which cannot validate Ctrl+C delivery.
6. **Containment coverage is partly probabilistic:** `HandleListKeepsOneLaunchsPipesOutOfAnother` is sequential; the later held-inheritable-handle test supplies the useful independent check. Early-child runs repeat real scheduling rather than coordinating a child-creation barrier. Keep the useful held-handle and independently observed liveness assertions, but move them to deterministic fixture handshakes.
7. **Coverage gaps demonstrated this review:** The current direct-command alphabet covers spaces/backslashes/quotes/tabs but not NUL; the PowerShell native corpus omits literal `--%`; batch-copy tests omit preexisting `CMDR_CMD`; resolver UNC tests prove path selection but never effective cwd inside cmd. All 99 selected existing cases passed despite the reproduced failures above.

## 4. Work performed, commands and outcomes

### Read-only investigation

- Read the worker request; README and design; `review/REPORT.md`, `review/03-platform-processes.md`, `review/COMPLETED-FIXES.md` (including the second review), and `review/TRACKER.md`.
- Used `rg --files`/`rg -n`, `cat` and `sed` to read every scoped `Windows*.cs` production file, both Ctrl+C helper files, Windows formatter code and all scoped Windows test files. Also read Windows formatting cases in `DetailsCommandTests`.
- Read `IProcessPlatform`, `ILaunchedProcess`, `IProcessGroup`, `PreparedLaunch`, `LaunchSpec`, environment layering and the relevant `ManagedProcess` launch/signal/release contracts. Consulted config validation to check whether NUL was rejected earlier.
- Read-only git commands: `git status --short`, `git rev-parse --short HEAD`, `git rev-parse HEAD`, `git diff --stat`. HEAD matched the requested commit; status/diff output was empty. No branch changes, commits, stash or repository writes.
- Searched for applicable AGENTS.md files under `/srv/devshare/projects`; none applied to this repository. The instructions for other repositories were not used.
- Consulted Microsoft Win32 documentation for CreateProcessW, STARTUPINFOW, UpdateProcThreadAttribute, job process-ID lists and console control events; .NET 10 Process and registered-wait source; PowerShell native binder source; upstream cross-spawn escaping source; and Microsoft cmd/pushd/UNC references. Source links accompany substantive claims above. Some initial URLs returned cache misses/403; equivalent accessible documentation/source or search excerpts were used. No claim relies on the inaccessible page body.

### Isolated source-linked probes

Created `probe-03/Probe.csproj` and `Program.cs` by a Python script. The project links actual Core source and the actual `CommandLineFormatter.cs` and `GroupTree.cs`, with no production stubs or modified source. All output/intermediate paths are inside the probe directory.

- `dotnet run --project /tmp/cmdr-claude-run-review2/probe-03/Probe.csproj`
  - First attempt failed with CS0103 because `GroupTree.cs` was not yet included. Added that source link in the scratch project only.
  - Next run passed and produced the NUL/PowerShell/session-mutation results recorded above.
  - Added the pure UNC plan probe, made the probe's local compile list explicit to exclude nested test build outputs, then ran the same command redirected to `probe-03/output.txt`: exit 0, all probe observations recorded. The CRLF probe only establishes that raw CR/LF survives cmd encoding; it does **not** establish actual Windows execution behavior.
- A Python subprocess script invoked `dotnet --version` and `pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'`: 10.0.107 and 7.6.2.
- The same script tested the proposed PowerShell array-expression workaround against native printf: stdout exactly `--%\na b\n%CMDR_PROBE%\n`, stderr empty, exit 0. Saved in `probe-03/versions-and-fix-probe.txt`.

### Targeted tests only

Created `probe-03/tests/ReviewTests.csproj`, source-linking the same actual production files plus these existing test classes: `WindowsCommandLineTests`, `WindowsCmdEscapingTests`, `WindowsBatchCommandLineTests`, `CommandLineFormatterTests`. Used the repository's xUnit/test SDK versions. This avoids writing build/restore artifacts into the repository.

```text
dotnet test /tmp/cmdr-claude-run-review2/probe-03/tests/ReviewTests.csproj --filter 'FullyQualifiedName~WindowsCmdEscapingTests|FullyQualifiedName~WindowsCommandLineTests|FullyQualifiedName~WindowsBatchCommandLineTests|FullyQualifiedName~CommandLineFormatterTests' --logger 'console;verbosity=minimal'

Passed: 99; Failed: 0; Skipped: 0; Duration: 6 s
```

The two optional pwsh tests executed because `/usr/bin/pwsh` and `/usr/bin/printf` were present. Restore/build emitted NU1900 because NuGet's vulnerability cache location under the read-only home directory could not be written. Restore, compilation and tests still succeeded. No package-cache permissions were changed. No full suite or guarded Windows integration tests were run. This source-linked subset is evidence for those logic tests, not a complete production solution build or Windows validation.

### Static checks that did not produce new findings

Reviewed x64 struct field widths/alignment, job/handle attribute constants and buffers, writable UTF-16 command buffers, environment sorting/double terminators/null removal/empty values/hidden `=C:` entries, client pipe-copy cleanup, kill-on-close/detach, PID-list growth, conservative conhost classification, duplicate exit-wait ownership and callback/dispose gating. The current source addresses the previously reported early-child assignment gap and duplicate wait-handle issue. No additional confirmed defect was found in those mechanisms. Their native correctness still needs the already-tracked Windows run; passing Linux tests cannot establish it.

## 5. Open questions and design ambiguities

No ambiguous design was implemented. These need explicit decisions or Windows evidence, rather than assumptions:

1. **UNC batch cwd policy:** Should cmdr support a share via temporary mapping, or reject that batch cwd clearly? W03-03 establishes the wrong-directory outcome; it does not choose a new support policy.
2. **Multiline batch arguments:** README/editor behavior preserves multiline arguments in JSON, but the cmd escaping regex leaves CR/LF untouched. The source probe prints `C:\bin\run.cmd ^"line1\r\necho^ INJECTED^"`. What guarantee is intended for batch files? Native cmd and a real argv-echo fixture are required before claiming literal preservation or command injection. This is a validation concern, not a sixth confirmed finding. At minimum, define an explicit rejection policy for representations that cannot be made lossless.
3. **Registry-dependent cmd behavior:** The batch interpreter disables AutoRun but does not explicitly set `/v:off` or `/e:on`. Percent/bang arguments should be tested with delayed expansion enabled by registry and with an actual forwarding shim. Do not declare a new percent/bang bug solely from caret strings or Linux tests. General batch files can also perform their own expansion; distinguish cmdr's first parse from script-authored behavior.
4. **Stop completion after run release:** `ManagedProcess` deliberately dispatches signals outside its lock. `WindowsProcessGroup.Interrupt` can wait on `InheritableHandleLock` and launches by numeric leader PID without a group-disposal check, whereas kill/count operations lease the job. Define whether pending dispatch must be revoked on detach/dispose and how leader identity remains held through helper attachment. B5 addresses this ownership seam; no PID-reuse failure was reproduced.
5. **Ctrl+C coverage versus tree membership:** Console broadcasts only reach processes sharing that console, while the job can contain descendants that create a different console. Microsoft's [control-event contract](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent) makes this explicit. Is that an accepted graceful-stop limitation? Force kill remains job-wide. No change to this policy was assumed.
6. **Native acceptance:** PowerShell 5.1, PS7 Windows mode, real helper failure/timeout/delivery, console stdin, x64 native structure/attribute acceptance, conhost lifetime, nested jobs, and kill/detach behavior still need native verification. These overlap the existing tracker and are not reported as fresh defects merely because this machine is Linux.

## 6. Files changed in the repository

**none**

External files created/updated: this handoff and the source-linked probe project, program, output/version logs and targeted test project with their generated artifacts, all under the authorized handoff/probe paths.

## Recommended next step

Triage the five findings without reopening the design phase. First fix the test harness's process ownership before native stress runs, add narrow regressions for NUL, literal `--%` and environment restoration, and agree on the explicit UNC cwd outcome. Then run the existing tracker’s Windows checks plus the semantic argument/cwd cases using owned fixtures on native Windows, including PowerShell 5.1 and 7. Keep the passing 99-case Linux subset separate from native acceptance.
