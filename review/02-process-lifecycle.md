# 02 — Managed process lifecycle and threading review

Reviewed baseline: `a06d405e01095309617069e43c5d4be9200c0ebc` on Linux x64, Ubuntu 24.04, .NET SDK 10.0.107/runtime 10.0.7. Production code and repository tests were not modified. All probe source, binaries, build intermediates and test results are under `/tmp/cmdr-review02-7s0NLF`; this report is this reviewer's only repository write.

Five actionable findings: one P1 and four P2. These are verified execution paths; the error and stalled-log cases deliberately inject the relevant failure rather than claim an observed native API or filesystem outage. Cancellation/relaunch observations are cross-referenced to session findings 05-01/05-03 instead of counted again.

## Functionality and scope

`ManagedProcess` supervises successive runs of one command. Start prepares platform-specific launch settings, subscribes to leader exit before starting, attaches a process group, opens stdin and schedules separate stdout/stderr pumps. Immutable snapshots expose lifecycle state, timestamps, exit facts, leftover count and readiness. Graceful stop sends an interrupt or configured stdin text; force kill targets the group. Leader exit waits at most one second for output, clears readiness, records an exit marker, completes a per-run task and either tracks leftovers with a timer or detaches them. Restart performs stop/wait/start. Disposal releases tracking, the process/group and logging; Linux disposal intentionally does not kill descendants.

`ReadinessMatcher` evaluates completed plain-text stdout/stderr lines, chooses the final decisive verdict in each batch and gives ready precedence over busy on the same line. Each regex match has a 200 ms timeout. `LaunchSpec.From` resolves the effective directory chain and copies arguments, environment overlays and readiness settings. `PreparedLaunch` carries the platform start info plus interrupt follow-up input and any user notice.

Read fully: `ManagedProcess.cs`, `ReadinessMatcher.cs`, `LaunchSpec.cs`, `PreparedLaunch.cs`, `ProcessSnapshot.cs`, `ProcessState.cs`, `IProcessGroup.cs`, `IProcessPlatform.cs`, `ProcessPlatform.cs`, `UnsupportedProcessPlatform.cs`; `ManagedProcessTests.cs`, `ManagedProcessReadinessTests.cs`, `ManagedProcessInterruptInputTests.cs`, `LaunchSpecTests.cs`, `EnvExpansionProcessTests.cs`, `ProcessTestHelpers.cs`, `WindowsProcessPlatformTests.cs`, `WindowsLaunchPlanTests.cs`; related `ReadySpecTests.cs`, `SessionProcessTests.cs` and `CommandOutputTests.cs`. Read `CommandOutput.cs` and `OutputBuffer.cs` fully to establish locking, logging and callback contracts. Inspected parser bounds, configuration types, session call sites, design documentation and Linux/Windows group contracts as needed. Native implementation correctness is assigned to the other reviewer.

## Verified findings

### 02-01 — P1: A process-count query error terminates the supervisor host

**References:** `src/Cmdr.Core/Processes/ManagedProcess.cs:520`, `:523-526`, `:588-599`; throwing platform contract at `src/Cmdr.Core/Processes/WindowsProcessGroup.cs:118-134`.

**Trigger:** `IProcessGroup.CountLiveProcesses()` throws while handling leader exit or a leftover timer tick. The Windows implementation explicitly documents and throws `Win32Exception` when the accounting query fails. This is an operational error path, independent of whether the native query implementation itself is correct.

**Impact:** Both callbacks run on runtime worker threads and let the exception escape. The entire cmdr host terminates instead of recording a supervision error. On the exit path the exception also occurs before the exit marker and `_runExited.TrySetResult()`, so merely adding an outer catch without restructuring would leave exit waiters incomplete.

**Evidence:** A source-linked Linux subprocess used a delegating `IProcessPlatform` whose group throws `new Win32Exception(5, "probe CountLiveProcesses failure")`. Throwing on the first query produced exit status 134 with a stack through `ManagedProcess.OnExited`, line 520, and `Process.CompletionCallback`. Returning one leftover initially and throwing on the next query produced status 134 through `ManagedProcess.OnLeftoverTick`, line 598, and `TimerQueueTimer.Fire`. These are fault-injection reproductions of managed exception handling; no real Windows query failure was induced.

**Fix direction:** Contain expected platform/I/O failures at both callback boundaries. Finalize leader-exit state and complete its task independently of leftover enumeration. Preserve an explicit unknown/error supervision condition on query failure and retry/report it; do not silently turn a failed query into zero leftovers and permit another run. Keep notifications outside the lifecycle lock.

**Regression test:** Inject a group that fails its initial count, then one that fails a later timer count. Run crash-sensitive checks in subprocesses. Assert the host survives, leader waiting completes, the error is visible, and starting remains blocked until leftover ownership is safely resolved. Include recovery on a later successful query.

### 02-02 — P2: Old output pumps contaminate subsequent runs and can falsely mark them ready

**References:** `src/Cmdr.Core/Processes/ManagedProcess.cs:394-395`, `:406-428`, `:440-460`, `:490-494`, `:614-622`; shared parser reset at `src/Cmdr.Core/Output/CommandOutput.cs:79-94`.

**Trigger:** A leader exits while a detached descendant keeps a redirected pipe open. After the one-second drain timeout the supervisor allows another run. The previous pump remains alive and forwards subsequent chunks to the same `CommandOutput`, after its parsers and buffer have been reset for the new run.

**Impact:** Old stdout/stderr is presented and logged as new-run output, can join the new run's partial lines/style state, and can set its readiness to Ready or Starting. The matcher identity check does not establish output provenance: `OnProcessLinesCompleted` obtains the *current* matcher when an old pump delivers its chunk, so both matcher references pass the check.

**Evidence:** Run 1 executed `(sleep 3; echo OLD_READY; sleep 20) & echo old-leader; exit 0` with `Leftovers=Detach`. Its leader exited and its stdout pump remained `WaitingForActivation`. Run 2 executed `echo NEW_BOOT; sleep 30`, declaring `^OLD_READY$` as its ready pattern. Its output became `=== started ... === | NEW_BOOT | OLD_READY`, and its readiness became **Ready**, although run 2 never emitted the matching text. Both process groups were explicitly killed afterward. This reproduced twice.

**Fix direction:** Give each pump/output notification an immutable run identity and prevent stale chunks from touching the current run's parser, buffer, log segment or matcher. Coordinate validation with `BeginRun` so a separate check followed by an unlocked write cannot race a new start. Explicitly settle/cancel/release the prior run's streams and pumps; retain a bounded drain policy so inherited handles cannot block restart indefinitely. Closing a pipe can affect detached writers, so define that policy deliberately.

**Regression test:** Gate an old detached child until the replacement is running, then make it emit a readiness match on stdout and stderr. Assert no old text enters the new buffer/log segment and no readiness transition occurs. Include a partial line/ANSI sequence across the boundary and a pump paused between read and publication.

### 02-03 — P2: Disposal and run release do not dispose owned redirected streams or stop pumps

**References:** `src/Cmdr.Core/Processes/ManagedProcess.cs:267-285`, `:368-375`, `:394-395`, `:440-470`, `:614-622`.

**Trigger:** Dispose a supervisor whose Linux command or descendant is still alive, or release an old run after a bounded drain. The code disposes `Process`, clears stream/task fields and closes the log, but never explicitly closes the `StreamReader`/`StreamWriter` instances it obtained and has no pump cancellation token or disposed/run check before publication.

**Impact:** `Dispose` returns while the old pumps retain the supervisor, output buffer, subscribers and pipe handles, potentially for the surviving child's lifetime. They continue reading and updating output after disposal. Ordinary exited runs also defer pipe-handle reclamation to garbage collection instead of deterministically releasing them. This is distinct from 02-02: it occurs with one run and no restart, and violates cleanup even if publication is later guarded by a run ID. It does **not** mean Linux disposal should kill the child; that behavior is intentionally platform-specific.

**Evidence:** A command emitted `BEFORE`, slept two seconds, emitted `AFTER`, then slept again. After observing `BEFORE`, the probe disposed the supervisor. After 2.5 seconds its stdout task remained `WaitingForActivation` and the disposed supervisor's buffer contained `AFTER`. Separately, a probe retained the actual redirected stream objects across 40 short runs to prevent GC from hiding missing disposal. After supervisor disposal, **80 readers still reported `BaseStream.CanRead=true`, 40 writers `CanWrite=true`, and the descriptor count increased by 117** from the warmed initial run. Explicitly disposing those streams released them. A non-retaining control recovered descriptors after GC, so this is nondeterministic reclamation for exited runs, not a claim of an unconditional permanent leak in every short run.

**Fix direction:** Introduce explicit per-run ownership of the readers, writer, pumps and cancellation source. Capture streams before scheduling tasks, close/cancel them on release/disposal, observe task completion/failure, and prevent further output publication. Coordinate outstanding stdin writers during teardown. Avoid synchronously awaiting a pump under `_sync`, because its readiness callback needs that lock. If cleanup requires awaiting, expose an async cleanup path while keeping synchronous disposal safe and bounded.

**Regression test:** Dispose a live supervisor while reads are pending; require pumps to settle and its output version to stop changing without killing the Linux child. Retain actual stream references across completed runs and assert they are closed on release/disposal. Check failed-start/attach cleanup and repeated disposal too. Explicitly kill the test child afterward.

### 02-04 — P2: Concurrent stop requests write unsafely to the same StreamWriter

**References:** `src/Cmdr.Core/Processes/ManagedProcess.cs:130-165`, `:645-663`; concurrent callers in `src/Cmdr.Core/Session/Session.Processes.cs:82-85`, `:100-103`, `:333-335`.

**Trigger:** Two or more stop requests overlap for `StopMethod.Input`, or overlap while sending `InputAfterInterrupt`. Each request captures the same `_stdin` under `_sync`, then calls `StreamWriter.Write` outside it. Repeated stop requests are expressly supported, and session stop actions schedule independent worker tasks, making overlap reachable through ordinary repeated actions.

**Impact:** The shared writer's character buffer/encoder state is mutated concurrently. Stop text can be corrupted or fail to reach the child, and exceptions outside the two handled exception types escape `RequestStop`. The advertised thread-safe public API does not hold for this operation. Background session callers may lose these exceptions in unobserved fire-and-forget tasks.

**Evidence:** Twelve simultaneously released stop workers sent a 256 KiB configured string to a child that initially did not read stdin. One run produced `ArgumentOutOfRangeException` from `System.IO.StreamWriter.Flush(Boolean, Boolean)` through `ManagedProcess.WriteStopInput`, line 654. Another produced both that exception and `OverflowException`. A first run returned normally from every worker, illustrating why sequential/small-input tests miss the race. The test finally killed the child rather than waiting for a successful graceful stop.

**Fix direction:** Serialize stdin writes per run using a dedicated queue or async gate, independent of `_sync`. Coordinate that ownership with stream teardown and follow-up interrupt input. Do not put potentially blocking pipe writes back under the lifecycle lock, and do not treat broad exception swallowing as a substitute for serialization. Integrate cancellation/abandonment policy with 05-03.

**Regression test:** Gate a nonreading child and release multiple input-stop requests simultaneously, with enough input to force writes across multiple writer/pipe buffers. Then allow reading and verify complete, correctly ordered stop records with no unexpected exceptions. Include interrupt follow-up writes and concurrent disposal; ensure force kill remains available while input is blocked.

### 02-05 — P2: Force kill waits for log I/O before sending the kill and holds the lifecycle lock

**References:** `src/Cmdr.Core/Processes/ManagedProcess.cs:182-209` (especially `:203` before `:209`), `:151-165`, `:269-284`, `:501-526`; synchronous log writes/flushes at `src/Cmdr.Core/Output/CommandOutput.cs:104-112`, `:309-337`.

**Trigger:** An enabled log's write/flush stalls, for example on slow or unavailable storage. `ForceKill` writes its system line synchronously while holding `_sync` and only signals the group afterward. Alternatively, a pump can already hold the output lock in a stalled log write, causing `ForceKill` to wait for that lock before signaling.

**Impact:** The force-kill escape action cannot terminate the child until log I/O recovers. Snapshot reads, stop requests and disposal also block on the held lifecycle lock; the UI calls force kill synchronously. This is distinct from the blocked **stdin** dispatch/cancellation in 05-03: the force-kill path itself is obstructed by logging, even with no restart operation or stdin write in progress. Catching log I/O errors does not help when the operation has not returned.

**Evidence:** A deterministic probe substituted a gate-controlled writable stream underneath the existing log `StreamWriter`, then called `ForceKill` on a quiet live child. With the log write gated, it observed `ForceKill complete=False`, `snapshot read complete=False`, and `child alive=True`. Releasing the log gate immediately allowed force kill and leader waiting to complete. This reproduces the synchronization/order defect under injected I/O latency; no actual filesystem outage was induced.

**Fix direction:** Separate lifecycle transitions and signal delivery from log I/O. In particular, a best-effort diagnostic must not precede the kill in a way that can block it. Move output/log work out of `_sync` and give logging an explicit bounded backpressure/failure policy while preserving ordering. Avoid replacing blocked I/O with an unbounded queue and consequent memory growth.

**Regression test:** Inject a blocked log sink, call force kill, and assert the signal reaches the group and state reads remain responsive before unblocking the sink. Cover a pump already blocked in logging as well as the force-kill system line. Add corresponding stop/disposal checks and verify diagnostic ordering after the sink recovers.

## Cross-review findings, not counted again

- **05-03, cancellation/blocked stop dispatch:** Independently reproduced the same family in the public `ManagedProcess.RestartAsync` helper at `ManagedProcess.cs:232-257`. A 1 MiB input-stop string to a nonreading child caused the helper to block inside `RequestStop` **before returning its Task**; after one second a 200 ms cancellation deadline had expired and the call still had not returned. Force kill released the write, and the helper returned successfully with a replacement `Running` PID despite cancellation. The helper only supplies `ct` to `WaitForExitAsync`, and has no cancellation checkpoint before `Start`. This adds a core-API synchronous-prefix example to [05-03](05-session-orchestration.md); it is not a separate numbered finding. The session uses its own restart implementation rather than this helper, so fixing the helper alone will not fix F5.
- **05-01, restart after shutdown:** Pending restart launch authority belongs to the session/UI operation owner. See [05-01](05-session-orchestration.md). The managed force kill API's purpose is to kill the captured group; it does not itself promise to cancel future launch operations. Do not count that orchestration issue again as a process-tree kill failure.

## Strengths

- Snapshots are immutable and read under one lifecycle lock. Exit callbacks carry both run ID and process identity, preventing a stale exit callback from overwriting a replacement's state. Leftover timers also carry a run ID and are disposed when no longer needed.
- `TaskCompletionSource` uses `RunContinuationsAsynchronously`, avoiding inline restart/sequence continuations while `_sync` is held. Pump scheduling avoids synchronous reads taking the output notification lock under the lifecycle lock. The normal output-notification → lifecycle lock order is consistent; no unconditional lock inversion was found in that normal internal path.
- Signals and stdin writes are intentionally outside `_sync`, which permits force kill while a plain stdin write is blocked. The problems are missing writer serialization and independent synchronous logging inside lifecycle operations, rather than a reason to move pipe writes under `_sync`.
- Leader exit and remaining descendants are modeled separately. A bounded output drain avoids waiting forever for inherited handles. Detach is explicitly suppressed after an in-run force kill, while graceful stop still honors configured detach behavior.
- Readiness has explicit no-pattern/starting/ready states, timestamps, ANSI-stripped input, ready precedence, finite regex timeouts and last-decisive-line behavior. Matcher evaluation occurs outside `_sync` and output notifications are serialized across streams.
- Factory-created launch requests copy configuration collections and readiness data, preserving environment overlay order and directory context. Existing tests use gated process output for most readiness transitions and cover rapid exits, input and signal stops, leftovers, restart and platform-specific launch behavior.

## Maintainability, extensibility and coverage gaps

These are recommendations/risks, **not additional verified defects**:

- Most lifetime state is spread across mutable fields (`_process`, `_group`, `_stdin`, pump tasks, completion source, matcher and flags). A per-run resource owner with explicit publication, stop and cleanup phases would make stale-run isolation, disposal and future async stop support easier to audit. Keep platform launch policy behind the existing interfaces.
- Strengthen interface contracts: `IProcessPlatform.Attach` does not currently document the kill-on-attach-failure assumption made at `ManagedProcess.cs:356`; `IProcessGroup` should describe exceptions and concurrency with `Detach`/`Dispose`. A captured group can currently outlive the lifecycle lock before a signal, so implementations need a clear lease/idempotence policy. No claim of a real stale-PID signal was verified here.
- `LaunchSpec.From` provides copying, but the public positional constructor accepts caller-owned `IReadOnlyList` instances and mutable `ReadySpec`; the record is not deeply immutable in general. Clarify the factory-only guarantee or expose immutable representations before relying on arbitrary constructor-created specs across asynchronous operations. Existing factory-copy tests cover args/env but not the copied readiness object.
- `Changed` is synchronous and subscriber exceptions are uncontained. A throwing readiness subscriber can fault a pump (only I/O/disposed exceptions are caught); the `OnExited` comment that pump faults are already handled is therefore stronger than the implementation. No production subscriber that throws was established, so this is an error-contract/coverage recommendation rather than another crash finding. Define subscriber failure and reentrancy expectations, and observe pump faults.
- The 200 ms readiness timeout applies per pattern per line, not per chunk/run. Repeated pathological near-matches can spend that budget on every line and hold the output notification lock, delaying both pumps and backing up the child pipes. Memory remains bounded by streaming/ring-buffer/parser limits, but there is no aggregate matching budget or timeout diagnostic. Measure this workload before choosing a disable/budget policy; it is not evidence of an unconditional deadlock.
- `WaitForExitAsync` intentionally waits for exit bookkeeping/drain and completes on `Dispose`; it does not prove a Linux child has actually died after disposal. Document the disposal completion behavior for API consumers and test it. No production caller misuse was established.
- Add deterministic barrier-based tests for callback-vs-start/dispose, timer-vs-restart, signal-vs-detach and simultaneous stop requests. Current tests strongly exercise sequential real-process behavior but not these resource-ownership interleavings. A controllable platform/group and gated streams provide more stable coverage than relying only on sleeps.
- Existing detached-leftover tests use a quiet `sleep`, and restart tests emit no decisive late output. They therefore do not exercise 02-02. Existing input tests use one short request; no simultaneous writer or disposal race is covered. No tests force count-query exceptions, blocked logging or explicit stream-closure assertions.

## Tests and probes performed

No full suite was run; the coordinator owns that run. One invocation used the already-built test assembly, with results directed outside the repository:

```text
dotnet vstest /srv/devshare/projects/cmdr/tests/Cmdr.Core.Tests/bin/Debug/net10.0/Cmdr.Core.Tests.dll \
  '/TestCaseFilter:FullyQualifiedName~RapidShortRunsAlwaysEndExitedWithCodeZero|FullyQualifiedName~RestartStopsAndStartsANewRun|FullyQualifiedName~InputStopWritesToStdin|FullyQualifiedName~Readiness_ConcurrentStdOutAndStdErrEndReady|FullyQualifiedName~DetachedLeftoversAreNotTrackedAndDoNotBlockStart' \
  /ResultsDirectory:/tmp/cmdr-review02-7s0NLF/test-results
```

Result: **5 passed, 0 failed, 0 skipped**, reported duration 2 seconds. This confirms baseline behavior for rapid exits, normal restart, simple stdin stop, concurrent stdout/stderr readiness and quiet detached leftovers; it does not invalidate the missing cases above.

Custom harness: `/tmp/cmdr-review02-7s0NLF/Probe.csproj` compiles the repository's current `src/Cmdr.Core/**/*.cs` directly, excluding `bin`/`obj`; it does not reference or modify repository build output. It built successfully with zero warnings/errors. Its `Program.cs` contains the complete reproductions. Modes executed: `crossrun` (twice), `dispose`, `fds`, `streams`, `stopcancel` (twice), `concurrentstop` (three times), `query-exit`, `query-timer`, `log-block`. Run a mode with:

```text
ulimit -c 0
timeout 15s dotnet /tmp/cmdr-review02-7s0NLF/bin/Debug/net10.0/Probe.dll crossrun
```

The initial `crossrun` invocation demonstrated the defect but hit its outer cleanup timeout; the repeat added cleanup-stage diagnostics and waited for termination callbacks, reproduced the same contamination and exited successfully. Fatal-query probes were intentionally isolated subprocesses with core dumps disabled; both exited 134 as expected. Surviving test groups were explicitly killed, except the replacement from the first cancellation probe, which expired after its bounded 30-second sleep; the repeat explicitly killed the replacement too. Short-run resource probes exited normally. A final process check found no remaining probe hosts or their sleeping children. Temporary harness files are retained for coordinator inspection.

## Limitations

Execution was on Linux only. Windows launch/group tests were read statically; no Windows host was available, and no actual Windows job query, Ctrl+C helper or console-input operation was executed. The P1 callback failure was verified by injecting the exception that the Windows group explicitly permits, not by reproducing a Windows OS failure. Logging latency was injected at the stream boundary, not measured on network storage. Concurrency probes establish reachable defects, not their real-world frequency. Native process grouping, signal mechanics, command resolution/escaping and job attachment internals remain with the native reviewer. No production fixes were made.
