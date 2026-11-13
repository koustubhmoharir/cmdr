# 03 — Platform process integration review

Reviewed commit: `a06d405e01095309617069e43c5d4be9200c0ebc` (2026-11-13).

**Result: four P2 findings; no confirmed P1 finding. Windows review is static.** Two Windows resolver errors were reproduced by executing the actual pure implementation on Linux; two additional findings follow from source control flow and Windows API contracts. No Windows process, console, job, or handle behavior was executed here. The job-assignment issue is already explicitly documented in the source; it is identified below as a known correctness gap, not a newly discovered or unexplained race. Linux runtime probes found no defect in the exercised behavior.

Only this report was written in the repository. Production code was not changed. The full suite was not rerun. The coordinator reports 618 Core and 572 UI tests passing on Linux; the eight `WindowsProcessPlatformTests` return early on Linux and therefore their reported passes do **not** validate Windows integration.

## Scope and behavior

Read all of `LinuxNative.cs`, `LinuxProcessPlatform.cs`, `LinuxProcessGroup.cs`, `WindowsNative.cs`, `WindowsJobStructs.cs`, `WindowsSafeJobHandle.cs`, `WindowsProcessPlatform.cs`, `WindowsProcessGroup.cs`, `WindowsCommandResolver.cs`, `WindowsCmdEscaping.cs`, `CtrlCHelper.cs`, `CtrlCHelperCommand.cs`, `ProcessPlatform.cs`, `UnsupportedProcessPlatform.cs`, `IProcessPlatform.cs`, `IProcessGroup.cs`, `PreparedLaunch.cs`, and `LaunchSpec.cs` under `src/Cmdr.Core/Processes/`.

Read the process test files in full, including all five pure Windows test classes, `WindowsProcessPlatformTests`, `ProcessTestHelpers`, `EnvExpansionProcessTests`, `LaunchSpecTests`, `ManagedProcessInterruptInputTests`, `ManagedProcessTests`, and `ManagedProcessReadinessTests`. Read `EnvExpansion.cs`, `StdinMode.cs`, `EnvLevelsTests`, `EnvExpansionTests`, the CLI helper dispatch in `src/Cmdr/Program.cs`, project settings, and the relevant README launch/stop/leftover/platform contracts. `ManagedProcess` was consulted only for the launch, attachment, signal, stdin, and resource ownership boundaries; its own state machine is separately reviewed.

Linux builds an argument-list launch through `setsid --`, with redirected UTF-8 streams and layered environment values. Attachment waits for the leader's own process group, and group control sends SIGINT/SIGKILL to the negative group ID, falling back to the leader if the group is absent. Live process counts scan `/proc/*/stat` and omit zombies/dead processes. Dispose and detach do not signal on Linux.

Windows resolves commands using the effective child environment's PATH/PATHEXT, selects direct execution or an escaped `cmd.exe /d /s /c` batch launch, and supports pipe or console stdin. Console stdin temporarily replaces the parent's standard-input handle during startup. Attachment creates a kill-on-close job and assigns the already running leader. Counts and force kills operate on the job. Graceful interruption launches a separate copy of cmdr in helper mode, attaches that helper to the leader's console, and broadcasts Ctrl+C there. Detach clears the job's kill-on-close flag.

`ProcessPlatform` selects Linux or Windows once; other operating systems receive `UnsupportedProcessPlatform`, whose three operations fail explicitly.

## Findings

### 03-01 — P2: Early Windows children can permanently escape job supervision

**Status:** Verified implementation gap by static inspection and documented job inheritance semantics; **Windows static, not reproduced on Windows**. Already acknowledged at `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:15–17`.

**Locations:** `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:135–138`, `:154–155`, `:185–198`, and `:232–245`. Observable containment operations are `src/Cmdr.Core/Processes/WindowsProcessGroup.cs:78–85`, `:121–134`, and `:146`.

**Trigger:** A command creates a child after `Process.Start()` starts executing it but before `Attach()` assigns the leader to the job. A fast wrapper that creates a worker and immediately exits is the clearest case. This does not require a child deliberately breaking away from a job.

**Impact:** The early child is absent from job accounting, survives job termination and kill-on-close, and can be left running after cmdr exits. If the leader exits before assignment, `AssignLeader` treats access-denied plus `HasExited` as success and returns an empty job even though a child may remain. Leftover count can therefore report zero and permit another run while an old worker still owns resources. The README's Windows containment and leftover guarantees do not hold for this launch ordering.

**Evidence:** Both startup paths call ordinary `process.Start()` with no suspension or creation-time job assignment. Only a subsequent call creates the job and assigns the leader. Adding a parent to a job does not retroactively add its existing children: the documented automatic association applies to children created **after** association. See Microsoft's [job object semantics](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects). The source explicitly describes the same escape window. `KillUntracked` is only called on an attachment exception, so the accepted fast-exit path does not even attempt that fallback.

**Fix direction:** Make containment part of launch: create/configure the job first and either use supported creation-time job assignment or start suspended, assign successfully, then resume. On any intermediate failure, terminate the suspended process and close all process/thread/job/pipe handles before reporting failure. Merely enumerating children after startup still leaves a creation/exit race.

**Regression test:** On native Windows, launch a small fixture that immediately creates a bounded-duration child, records its PID outside stdout buffering, and exits. Retain an independent handle to the child. Test both a short-lived leader and a leader that stays alive. Assert membership/counting and actual child termination after force kill and supervisor disposal. Use a launch seam/barrier to force the pre-assignment ordering rather than hoping repeated runs encounter it. Always clean up the recorded child independently of the job.

**Existing coverage gap:** `tests/Cmdr.Core.Tests/Processes/WindowsProcessPlatformTests.cs:72–90` checks supervisor state and leftover count, not independent child liveness; an unassigned child can evade both assertions. `:95–118` expects an immediate background child to be tracked but does not control the race. On Linux neither test executes its body.

### 03-02 — P2: Relative Windows PATH entries are checked and executed in different directories

**Status:** Verified pure resolver/launch-plan defect, reproduced on Linux; native Windows impact assessed statically.

**Locations:** `src/Cmdr.Core/Processes/WindowsCommandResolver.cs:58–63` and `:76–92`; caller at `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:70–75`; batch handoff at `src/Cmdr.Core/Processes/WindowsCommandResolver.cs:172–178`.

**Trigger:** A child environment has a relative PATH entry, such as `tools` or `.\node_modules\.bin`, and cmdr's current directory differs from the command's working directory.

**Impact:** Child-local tools can fail to resolve. More seriously, a batch file can be selected using the file system under cmdr's current directory and then executed as a relative path by `cmd.exe` under the child's directory, so the checked file and the executed file differ. If the two directories contain different `tools\run.cmd` files, the plan can execute a different script than the resolver inspected. This is a launch correctness problem; this report does not claim an untrusted-config security boundary.

**Evidence:** `SearchDirectories` yields the relative entry unchanged. `Resolve` passes `tools\run.CMD` directly to `File.Exists`, which uses the current process directory, and returns that relative path. `PlanLaunch` embeds it unchanged in the batch command, whose working directory is the child's. The resolver's documented full-path result at `WindowsCommandResolver.cs:31` is violated.

Actual source probe:

```text
Resolve("run", @"C:\child", "tools", ".CMD", existsOnlyAtChildAbsolutePath)
  candidates: C:\child\run.CMD, tools\run.CMD
  result: null (expected candidate C:\child\tools\run.CMD was never checked)

exists("tools\\run.CMD") = true
  result: tools\run.CMD
  plan: /d /s /c "tools\run.CMD"
```

**Fix direction:** Normalize each search directory to an absolute Windows path using an explicit base before calling the file-exists predicate, and return that absolute candidate to the launcher. The child working directory is the natural base for effective child PATH entries. Even if parent-relative PATH semantics are intentionally chosen, the selected path must be made absolute so the check and launch refer to the same file.

**Regression test:** Add pure cases for `tools` and `.\node_modules\.bin` with different supervisor and child directories, requiring absolute candidates/results. On Windows, put distinguishable `.cmd` fixtures in both directories and assert which script runs; also test only the child fixture existing. Include a direct `.exe` fixture to prevent batch and executable resolution from diverging.

**Existing coverage gap:** `tests/Cmdr.Core.Tests/Processes/WindowsCommandResolverTests.cs:17–42` and `:74–90` cover working-directory preference and absolute/quoted/empty PATH entries, but every nonempty PATH search directory in those tests is absolute.

### 03-03 — P2: Failed Windows resolution can launch a different executable through native fallback

**Status:** Verified fallback control flow, probed using the actual pure implementation; resulting executable lookup is supported by the Windows/.NET API contracts. **Windows execution remains static.**

**Locations:** `src/Cmdr.Core/Processes/WindowsCommandResolver.cs:157–161` and `:181–185`; `src/Cmdr.Core/Processes/WindowsProcessPlatform.cs:70–83`.

**Trigger:** The explicit resolver returns null for a configured relative executable, but that path exists relative to cmdr's own directory. Example: command working directory `C:\child`, program `.\tools\tool.exe`, no `C:\child\tools\tool.exe`, but `C:\launcher\tools\tool.exe` exists and cmdr runs in `C:\launcher`. A bare name absent from the effective child search path can similarly fall through to the native parent/system search.

**Impact:** A command that should fail resolution can run the wrong executable. Effective child working-directory/PATH resolution is bypassed at the last step. This finding remains even after all search directories are correctly normalized, because a genuine miss is enough to trigger it.

**Evidence:** The probe checked only these missing candidates:

```text
C:\child\.\tools\tool.exe
C:\child\.\tools\tool.exe.EXE
```

It then produced `WindowsLaunchPlan.FileName = .\tools\tool.exe`. The comment assumes preserving the unresolved name makes `Process.Start` report failure, but `WorkingDirectory` does not control executable lookup when `UseShellExecute=false`. Native lookup can resolve that relative executable in the parent's directory. See [.NET WorkingDirectory semantics](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-10.0) and [CreateProcessW executable lookup](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw). The .NET 10 [Windows process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs) passes the executable command and child working directory separately to native process creation.

**Fix direction:** Make an unresolved configured command an explicit resolution failure, or preserve an absolute intended candidate for an OS error without falling back to another lookup policy. If system-directory/native search is desired for bare names, model that deliberately in the resolver and still produce an absolute result. Preserve direct launches of explicitly named executable files when defining this policy; do not accidentally make their validity depend entirely on PATHEXT membership.

**Regression test:** Add a plan test that a resolver miss cannot yield an unresolved relative executable. Replace the current expectation in `tests/Cmdr.Core.Tests/Processes/WindowsLaunchPlanTests.cs:59–71` (the test currently preserves the faulty assumption). On native Windows, create two directories, place a harmless marker executable only under the supervisor's relative `tools` directory, launch with the child directory, and require a start failure with no marker. Add a bare-name fixture present only in the parent's PATH and an effective child PATH that excludes it.

### 03-04 — P2: Drive-rooted Windows command paths are appended to the working directory

**Status:** Verified pure path-classification defect, reproduced on Linux; Windows launch consequences assessed statically.

**Locations:** `src/Cmdr.Core/Processes/WindowsCommandResolver.cs:52–55`, `:134–143`, and `:145–154`.

**Trigger:** A valid drive-rooted Windows program path, such as `\tools\run.cmd` or `/tools/run.cmd`, is configured with working directory `C:\child`. These paths start at the drive root, not inside `C:\child`.

**Impact:** The resolver misses an existing batch file under `C:\tools`, or searches a different path under `C:\child`. The missed batch file then takes the direct-process fallback instead of the batch interpreter path, so it cannot be launched correctly. If a matching child-directory file exists, the wrong script can be selected instead.

**Evidence:** `IsPathLike` recognizes the separator but `IsRooted` only recognizes a double-backslash UNC prefix or a drive-letter absolute path. Consequently the actual probe for `\tools\run` against `C:\child` attempted `C:\child\\tools\run.CMD`, never `C:\tools\run.CMD`, and returned null despite the latter existing in the supplied filesystem. Drive-relative syntax such as `C:tools\run.cmd` is also not handled by this classifier; its intended base should be defined or rejected explicitly rather than treated as an ordinary relative name.

**Fix direction:** Separate “fully qualified” from “rooted” path classification. Normalize root-relative paths against the drive root using Windows path semantics before extension probing. Cover or explicitly reject drive-relative and forward-slash UNC forms with a clear error. Keep the normalization logic testable on Linux, or inject a Windows path-normalization function rather than extending ad hoc string concatenation.

**Regression test:** Pure resolver tests should resolve `\tools\run.cmd` and `/tools/run.cmd` to the same root-level fixture with both root and child-directory lookalikes present. Assert the resulting launch plan selects the interpreter and the absolute root-level batch path. Native Windows should execute a root-relative batch fixture successfully. Existing path tests at `tests/Cmdr.Core.Tests/Processes/WindowsCommandResolverTests.cs:92–130` cover ordinary relative paths, drive-qualified paths, and backslash UNC paths, but omit these rooted forms.

## Strengths and reviewed ownership details

- The platform boundary is small and coherent. Pure Windows resolution, escaping, helper-command construction, and layout checks are executable on Linux, and all 48 focused tests passed.
- Linux uses `ArgumentList` throughout and places `--` before the target program. Spaces, quotes, empty arguments, shell metacharacters, line breaks, tabs, trailing backslashes, and Unicode survived the independent literal-argument probe. There is no implicit shell parsing on this path.
- Environment overlays are applied before Windows resolution and before Linux target execution. Layer snapshots and null removals have focused coverage. The probe confirmed cwd/config-directory/path-separator expansion reached a real child and UTF-8 stdin had no BOM.
- Linux validates positive group IDs, handles ESRCH without reporting an already gone tree as a signal failure, waits for initial group creation, and parses `/proc` command names using the final closing parenthesis. The unusual-name and immediate-interrupt probes passed.
- Windows owns jobs through `SafeJobHandle`; active job operations hold `DangerousAddRef` across the native call, preventing a concurrent Dispose from closing the handle mid-call. Native declarations use explicit BOOL-sized integers, pointer-sized fields, and `SetLastError`. The reviewed x64 structure layout tests pass.
- Job-creation/configuration/assignment failure paths dispose the job and attempt to kill the untracked process tree. The console-stdin swap restores the saved handle in `finally`, and restoration failure attempts child cleanup before throwing. These are useful safeguards, although Windows failure paths were not runtime validated.
- The Ctrl+C helper keeps console attachment out of the TUI process, captures stderr before detachment, ignores its own generated Ctrl+C, and returns differentiated error codes. Helper command construction handles apphost and `dotnet <assembly>` launches without assembling quoted shell strings. The parent drains helper stderr asynchronously and bounds its wait.
- Detach semantics and the different platform Dispose behaviors are explicitly documented. Unsupported platforms fail directly rather than silently using a less capable supervisor.

## Documented limitations and unverified risks — not additional defects

**Linux group scope:** Supervision covers the process group established by `setsid`, not arbitrary descendants that deliberately create another group/session. Such descendants escaping signals/counting is a limitation of this implementation model, not a new defect in this report. Noninteractive shell background jobs ignoring SIGINT are also expected; `ManagedProcessTests.CtrlCReachesChildrenAndBackgroundChildIgnoresIt` covers the intended need for a later force kill. Linux processes continuing after supervisor disposal and the target failing through `setsid` with a nonzero exit rather than `FailedToStart` are existing intentional behaviors.

**Windows batch stop:** The source already explains that an stdin-reading child can consume the batch termination answer and that console-stdin batches cannot be answered through a pipe (`WindowsProcessPlatform.cs:97–114`). Treat these as known limitations. The hard-coded English `Y` deserves a native non-English Windows test; this review did not establish a locale-related production failure.

**Console creation:** No finding asserts that `CreateNoWindow` necessarily leaves an unattachable process. Microsoft's creation-flag wording is insufficient to infer that; [primary console experiments and their accompanying test suite](https://github.com/rprichard/win32-console-docs#common-semantics) describe a new console without a visible window. The source also records prior user confirmation of console stdin. End-to-end Ctrl+C and null-stdin validation on supported Windows hosts is still missing from this review. An initial concern about this was withdrawn, not retained as a speculative bug.

**Linux identity/readability:** `LinuxProcessGroup.cs:85–105` retains a numeric ID and has a leader fallback, while `/proc` reads conflate disappearance and inaccessibility (`:122–128`). PID reuse, inaccessible `/proc`, and failure to establish the group within the two-second wait need explicit assumptions and targeted fault tests. No PID-reuse misdelivery or restricted-`/proc` failure was reproduced; these are hardening risks, not counted findings. Avoid tests that signal arbitrary host PIDs.

**Process-wide stdin mutation:** The Windows lock protects this platform's console-stdin swaps; it cannot coordinate unrelated process launchers or native stdin readers. All current pipe-mode command streams are redirected, and the helper does not read stdin, so no concrete in-repository conflicting launch was identified. Prefer explicit per-child standard handles in a future native launcher, especially if fixing 03-01.

## Maintainability, extensibility, and missing coverage

The main maintainability problem is launch atomicity: `Prepare` / `Start` / `Attach` assumes a running process can safely exist before platform ownership is established. A Windows launcher capable of creating the job, child handles, and suspended process as one owned operation would address 03-01 and eliminate the global stdin swap. Define which layer owns every partially created resource and returns the fully supervised process/group; do not leave that contract implicit in comments. Linux can continue to use its simpler execution path.

Path normalization should have one explicit Windows policy. The resolver's present separator/drive/extension string rules are readable but incomplete, and the null fallback switches policies unexpectedly. Pure tests should assert filesystem candidates and full resulting plans, including failure behavior, rather than only returned filenames.

Highest-value additional coverage:

- **Native Windows interruption:** An actual cmdr helper executable, a child with an observable Ctrl+C handler, pipe and console stdin, two simultaneous command consoles, no event delivered to cmdr or the other command, and child exit before helper attachment. Existing helper-command tests test only command construction. Launching the test runner itself in helper mode would not test cmdr's CLI dispatch; inject a built cmdr helper command or use a dedicated fixture.
- **Native Windows argument round trips:** A fixture that reports argv, launched directly and through `.cmd`/`.bat` forwarding scripts. Include empty arguments, spaces/tabs/newlines, quotes, runs of backslashes, `%NAME%`, `!NAME!`, carets, parentheses, ampersands, pipes, and Unicode. Exercise node_modules shims and ordinary/global shims separately. `WindowsCmdEscapingTests` checks hand-derived strings, not what real `cmd.exe` and the final executable receive; no additional escaping defect is claimed without that evidence.
- **Windows failure cleanup:** Inject failures at job creation, job configuration, assignment, stdin replacement/restoration, helper launch/nonzero exit/timeout, and detach. Check actual child liveness and job/handle release independently. Add concurrent query/kill/dispose tests for the SafeHandle lifetime guarantee.
- **Linux boundary cases:** Preserve the successful probes as focused regressions where useful; add controlled zombie exclusion, child-environment PATH selecting a target, missing `setsid`, and restricted `/proc`/group-creation-timeout behavior in isolation. Prefer a test seam or disposable namespace for failures instead of changing the shared host.
- **Test reporting/layout:** Return-early platform tests should be visibly skipped, and the Windows suite must actually run on Windows. Check interop field offsets as well as sizes for any architectures the product promises; current tests assert sizes and conditionally bypass two on 32-bit. Missing tracked CI, SDK pinning, and lockfiles are coordinator-owned repository recommendations, not additional platform defects here.

Linux counting scans all `/proc` entries for each group. This is simple and correct in the tested environment, but a shared sampling strategy may help if many tracked groups/processes are supported. Measure before changing it; no performance defect was established.

## Tests performed and reproducibility

Host: Ubuntu 24.04 x64, .NET SDK 10.0.107, .NET runtime 10.0.7. No `wine`/`wine64` or native Windows runtime was available. **All Windows OS integration review is static.**

Focused tests used a copy of existing test binaries outside the repository:

```bash
dotnet vstest /tmp/cmdr-review03-c9nygasl/tests/Cmdr.Core.Tests.dll \
  --TestCaseFilter:'FullyQualifiedName~WindowsCommandResolverTests|FullyQualifiedName~WindowsLaunchPlanTests|FullyQualifiedName~WindowsCmdEscapingTests|FullyQualifiedName~WindowsCtrlCHelperCommandTests|FullyQualifiedName~WindowsJobStructTests' \
  --ResultsDirectory:/tmp/cmdr-review03-c9nygasl/results
```

Result: **48 passed, 0 failed, 0 skipped**. These are pure tests, not Windows integration tests. No full-suite duplication occurred.

An independent console harness compiled the current Core source files directly, with all generated output under `/tmp/cmdr-review03-c9nygasl`, and exercised platform APIs without going through `ManagedProcess`. Its source is `/tmp/cmdr-review03-c9nygasl/Program.cs`, project `/tmp/cmdr-review03-c9nygasl/probe.csproj`, and captured output `/tmp/cmdr-review03-c9nygasl/probe.log`. Run:

```bash
dotnet run --project /tmp/cmdr-review03-c9nygasl/probe.csproj --verbosity quiet
```

The recorded checks were:

| Probe | Result |
| --- | --- |
| Real Linux Python child: literal argument vector including empty/quoted/Unicode/metacharacter/newline/tab/backslash values | Exact argument-array assertion passed; exit 0 |
| UTF-8 stdin with `é雪\n` | Exact round-trip assertion passed; no BOM |
| Effective working directory and `${cwd}`, `${configDir}`, `${pathsep}` environment | Child reported the expected temporary directory and `:` separator |
| Immediate Linux attachment followed by SIGINT | 10/10 children exited within the two-second bound per harness invocation |
| Linux command name `a ) b ( c)` set via `prctl` | Group ID equaled leader PID; live count 1, then 0 after kill/reap |
| Windows relative PATH candidate and batch plan | Reproduced 03-02 |
| Windows drive-rooted command candidate | Reproduced 03-04 |
| Windows unresolved relative/bare-name plan | Confirmed original unresolved FileName is retained in 03-03; native wrong-executable consequence not executed |
| Unsupported platform Prepare/Start/Attach | All three threw `PlatformNotSupportedException`; Linux current-platform selection correct |

Probe children were bounded-duration processes and were killed/reaped in cleanup when needed. Temporary working directories were removed. No production source, test source, project file, or repository build artifact was written by this review. External references were consulted narrowly for central Windows job inheritance, native executable lookup, and the ambiguous console-creation assumption; the review otherwise uses repository source and tests.
