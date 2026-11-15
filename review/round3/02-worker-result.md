# Worker 02 handoff — lifecycle and Linux process platform

Reviewed HEAD: `a27a7ce65b3e3338a18596d700f82dcc5f982e7c` (`review-fixes`). Repository status was clean before and after review. This was a read-only review: no implementation changes, commits, branch changes, or new design phase. All probe source and build/test output are under `/tmp/cmdr-claude-run-review2/probe-02/`.

Result: **two P2 findings and one P3 finding**. No confirmed P1. A1 is the still-unfixed ManagedProcess-helper part of the previously reported cancellation family, rather than a regression in Session's replacement implementation. A2 demonstrates a timeout-policy/launch-contract failure using an injected delayed launcher; it does not demonstrate a leaked descendant with stock setsid. A3 demonstrates stale control diagnostics, not a recurrence of the fixed stdout/stderr/readiness contamination.

## Review performed and scope

Read README.md, docs/design.md, review/REPORT.md, review/02-process-lifecycle.md, review/03-platform-processes.md, review/COMPLETED-FIXES.md (including “Review of the fixes”), and review/TRACKER.md.

Read every requested production file completely: CtrlCHelper.cs, CtrlCHelperCommand.cs, ILaunchedProcess.cs, IProcessGroup.cs, IProcessPlatform.cs, LaunchSpec.cs, LinuxNative.cs, LinuxProcessGroup.cs, LinuxProcessPlatform.cs, ManagedProcess.cs, PreparedLaunch.cs, ProcessLaunched.cs, ProcessPlatform.cs, ProcessSnapshot.cs, ProcessState.cs, ReadinessMatcher.cs, UnsupportedProcessPlatform.cs.

Read every non-Windows test file in tests/Cmdr.Core.Tests/Processes completely: EnvExpansionProcessTests.cs, LaunchSpecTests.cs, ManagedProcessInterruptInputTests.cs, ManagedProcessTests.cs, ManagedProcessLeftoverCountFailureTests.cs, ManagedProcessRunPipesTests.cs, LaunchContractTests.cs, ProcessTestHelpers.cs, ManagedProcessReadinessTests.cs.

Followed the relevant CommandOutput publication/disposal and Session stop/restart/subscription paths, and EnvExpansion. WindowsProcessGroup and the exit/dispose portion of WindowsLaunchedProcess were consulted only to assess shared group/process contracts. No native Windows execution or Windows-specific defect claim is included.

Checked start/stop/kill/exit/restart/dispose transitions, waiter completion, lock ordering, previous-run resource release, output/readiness identities, stdin serialization, timer identities, count failure and recovery, pending detach cancellation, Linux stat parsing/signals, environment/cwd preparation, and callback failure boundaries. The previously fixed normal-path process-disposal lock inversion was not found again. Count failures remain contained and unknown counts block starting. Process chunks and readiness carry run identities; delayed control-error output does not (A3).

## A. Findings, most severe first

### A1 — P2: ManagedProcess.RestartAsync still blocks through cancellation and can launch after cancellation

- **Location at HEAD:** `src/Cmdr.Core/Processes/ManagedProcess.cs:241`, particularly `:253`, `:254`, `:266`.
- **Defect:** The public helper synchronously dispatches potentially blocking stop I/O and uses the cancellation token only for its exit wait, so cancellation neither bounds dispatch nor consistently prevents a replacement run.
- **Concrete failure scenario:** Start `/bin/sh -c 'echo probe02-blocked; sleep 30'` with input-stop text of 2 MiB and no stdin reader. Call RestartAsync on a dedicated thread, then cancel after the snapshot becomes Stopping. It remains blocked inside RequestStop. ForceKill releases the write; by the time WaitForExitAsync checks the token its underlying task can already be complete, so the helper starts a replacement and returns success. The idle variant is simpler: pass an already-cancelled token to RestartAsync on a NotStarted supervisor; it launches immediately without any token check.
- **Evidence:** The source-linked current-HEAD harness produced:

  ```text
  pre-cancelled restart returned successfully: state=Running, pid=15
  cancelled while input blocked: restartCompleted=False, state=Stopping
  restart result=success state=Running pid=26
  ```

  The blocked observation was made 300 ms after cancellation. Both replacement runs were explicitly force-killed and awaited. These are Core API reproductions; Session uses its own implementation, so they do not establish an F5/quit regression.
- **Confidence:** High.
- **Suggested fix:** Apply cancellation before any stop/start side effect; dispatch blocking stop work independently of the caller's cancellable wait and observe abandoned work; recheck cancellation immediately before authorizing Start. Capture the original run/completion rather than reacquiring whichever run happens to be current. Share the relevant restart rules with Session or retire the redundant public helper if maintainers choose that API change. Add both cancelled-idle and blocked-dispatch regressions.
- **Earlier-review relationship:** review/02-process-lifecycle.md already reproduced this helper in its cross-review discussion of 05-03, and review/REPORT.md explicitly required covering it. Current completed-fix entries describe Session fixes; this helper remains unchanged in the relevant behavior. This finding is reported once here, not as separate cancellation and blocking bugs.

### A2 — P2: Linux group-creation timeout is accepted as a successful launch

- **Location at HEAD:** `src/Cmdr.Core/Processes/LinuxProcessPlatform.cs:96`–`:106`, successful return at `:58`.
- **Defect:** WaitForOwnGroup falls through on its two-second deadline without reporting failure, and Launch returns a group keyed by the leader PID even though the leader may still belong to the inherited group.
- **Concrete failure scenario:** A launcher is delayed for more than two seconds before executing setsid(). The launch succeeds with a fabricated group identity, despite its documented requirement that the process be isolated before return. Group accounting can return zero while that leader is still alive; control relies on the ESRCH-to-positive-PID fallback rather than verified group control. Under ordinary scheduling the launcher establishes its group quickly, explaining why the immediate-stop regression passes.
- **Evidence:** The harness used the actual LinuxProcessPlatform.Launch with its prepared redirected streams, substituting a Python delay before exec of the real `/usr/bin/setsid`. It printed:

  ```text
  Launch returned after 2007ms: pid=15 pgid=1 ownGroup=False counted=0
  ```

  The delay was three seconds, and the process was killed before its eventual exec. This is fault injection at the pre-setsid scheduling boundary, not evidence that stock setsid regularly takes two seconds, nor evidence of a surviving descendant. The defect is the unconditional success after expiry and its incorrect group/accounting contract.
- **Confidence:** High for the timeout/contract defect; the frequency and descendant consequences on a normally scheduled stock launcher were not established.
- **Suggested fix:** Return an explicit established/exited/timed-out result, and fail and clean up on timeout rather than constructing a usable group. Distinguish unreadable stat data from confirmed disappearance as part of validating launch. Preserve the ordinary immediate-exit case. **Do not just add a throw and retain all cleanup placement:** Launch currently executes under ManagedProcess's lock; killing and disposing a Process there can recreate the reviewed Process exit-callback lock inversion. Failed-launch cleanup and exit-callback publication need a safe ordering outside that lock.

### A3 — P3: A late stop failure from an earlier run is appended to the replacement run

- **Location at HEAD:** `src/Cmdr.Core/Processes/ManagedProcess.cs:743`–`:758`; analogous untagged input failures at `:778`–`:784` and call sites at `:161`, `:169`.
- **Defect:** Control operations capture a run's group/stdin but publish eventual failure diagnostics through unqualified Output.WriteSystem, so the error is attributed to whichever run is current when the operation finishes.
- **Concrete failure scenario:** Run A starts a graceful interrupt that takes time to return. Force kill A, wait for its exit, and start B. If A's interrupt subsequently reports failure, B's output receives “stop failed” although B was never asked to stop. Windows's bounded helper wait gives such overlapping dispatches a natural opportunity; the reproduction injects the delay through the shared platform seam on Linux.
- **Evidence:** The harness delegated actual launches/kills/counts to Linux and gated only Interrupt, then threw Win32Exception after replacement launch. B's complete buffer was:

  ```text
  === started 2026-11-15 15:11:10 === | probe02-new | stop failed: probe02 old-run interrupt failure
  ```

  A's normal process output/start marker had correctly been cleared. B was then killed and awaited.
- **Confidence:** High.
- **Suggested fix:** Carry the captured run identity through SignalGroup/WriteStopInput and publish control diagnostics only if that identity remains current and the supervisor is not disposed. Make the identity check and system-line publication atomic with BeginRun, as already done for process chunks; alternatively route old-run failures to an explicitly separate diagnostic destination. Add gated signal and stdin-error boundary tests.

## B. Organization and pattern proposals, ranked

These are concrete follow-on improvements required by this review request, not a new design phase or implementation scope expansion.

### B1 — One restart authority and cancellation protocol

- **Priority:** 1.
- **Bug class prevented:** Divergent restart implementations fixing cancellation in one path while leaving the other unsafe; A1 and earlier 05-03/05-01 are actual examples.
- **Files affected:** ManagedProcess.cs; relevant restart pieces of Session.Processes.cs; process/session restart tests.
- **Sketch:** Factor the common cancellable stop-dispatch/wait/check logic into one internal operation whose run target and launch authority are explicit. Keep Session tree membership/revocation in Session. Decide whether the public helper remains supported; if it does, require the same cancellation checkpoints and observed background dispatch. Avoid a shared helper that merely takes a token but still calls blocking RequestStop in its synchronous prefix.
- **Cost / risk:** M; medium, because Session already has stronger operation ownership that must be preserved. The narrow cancellation checkpoints are S, but alone do not fix blocked dispatch.

### B2 — Complete the per-run resource owner

- **Priority:** 2.
- **Bug class prevented:** Mixing old control completions with current state/output, lifetime races with detached/disposed groups, and mismatched waiter/pipe/readiness identities. Actual examples: A3, past 02-02/02-03 and the Process.Dispose deadlock.
- **Files affected:** ManagedProcess.cs, ILaunchedProcess.cs, IProcessGroup.cs; run-pipe and lifecycle tests; a small CommandOutput API addition for run-qualified system diagnostics.
- **Sketch:** Expand RunPipes into a Run owner for identity, process/group, streams, matcher, exit completion, stop flags and operation tracking. Keep immutable current snapshot publication separate. Dispatches capture this owner rather than a selection of mutable supervisor fields. Mark a run retired, suppress its late publications, and release/dispose its resources outside the supervisor lock. Make group-use leases and detach/dispose ordering explicit without waiting under that lock.
- **Cost / risk:** M–L; medium to high because disposal and callbacks are sensitive. Iterate one ownership boundary at a time with gated tests; do not rewrite all platform logic together.

### B3 — Make group establishment and stat-read outcomes typed decisions

- **Priority:** 3.
- **Bug class prevented:** Converting an observation failure or elapsed deadline into successful isolation/accounting. Actual example: A2; past reviews also flagged /proc readability versus disappearance.
- **Files affected:** LinuxProcessPlatform.cs, LinuxProcessGroup.cs, ProcessLaunched.cs/IProcessPlatform.cs as needed for callback publication; LaunchContractTests.cs and focused Linux tests.
- **Sketch:** Extract pure parsing and a poll decision over `Found(state, pgid)`, `Gone`, `Unavailable(error)` plus deadline status. Inject the stat reader and time source in tests. Only Established or a confirmed already-exited leader may complete launch normally. Timeouts/errors enter a cleanup path that cannot dispose a callback-held Process under ManagedProcess's lock. Establishing the group should remove the normal need for positive-PID signal fallback; if fallback remains, confine it to the explicit pre-establishment owner.
- **Cost / risk:** M; medium due to launch/exit races. Pure decision coverage is S. This proposal does not claim to solve arbitrary daemon escape or all PID reuse with a racy stat precheck.

### B4 — Separate lifecycle decisions from platform I/O and resource release

- **Priority:** 4.
- **Bug class prevented:** Lifecycle locks held across external waits/enumeration/disposal and cleanup paths that reintroduce lock inversion. Actual precedents: fixed Process.Dispose deadlock and 02-05; A2's naive fix could restore the former.
- **Files affected:** ManagedProcess.cs, LinuxProcessPlatform.cs; shared platform contracts and fault-injection tests.
- **Sketch:** Under the lock, reserve a run/transition and capture required work; perform launch/count/detach/disposal outside it; publish only if the captured run/transition is still authoritative. Put transition rules in a small pure reducer or explicit state-specific methods so Running/Stopping/Exited-with-unknown-leftovers are exhaustively exercised. Preserve output-before-signal ordering with bounded output enqueue operations. Treat count failure and detach failure separately.
- **Cost / risk:** L; high if performed wholesale. Prioritize the specific launch-failure boundary first and retain current working fast paths until individually covered.

### B5 — Test fixtures must own and independently clean up every process and pipe

- **Priority:** 5.
- **Bug class prevented:** False-positive blocked-I/O tests, timing flakes, and cleanup relying on the very accounting under test. Current concrete examples are the test-quality items below and the earlier leaked INT-ignoring shell.
- **Files affected:** ProcessTestHelpers.cs, LaunchContractTests.cs, ManagedProcessTests.cs, ManagedProcessInterruptInputTests.cs, ManagedProcessRunPipesTests.cs, ManagedProcessLeftoverCountFailureTests.cs.
- **Sketch:** Add a disposable process fixture that captures the group immediately after launch, owns redirected streams explicitly, always attempts independent termination, waits for actual completion/group emptiness, and uses nested finally blocks so a cleanup assertion cannot skip remaining disposal. Provide gated streams/groups that report entry into the actual write/signal/count operation. Use gate files rather than child-duration sleeps for event observation. Fault injection of fatal callbacks should run in isolated subprocesses.
- **Cost / risk:** M; low to medium, mostly test infrastructure.

### B6 — Freeze launch data at one boundary

- **Priority:** 6.
- **Bug class prevented:** Caller mutation of IReadOnlyList-backed collections or ReadySpec during delayed launch/restart. This was identified as a risk in the earlier lifecycle review; no new production mutation failure is claimed here.
- **Files affected:** LaunchSpec.cs, PreparedLaunch.cs, ReadinessMatcher.cs; LaunchSpecTests.cs and launch call sites as required.
- **Sketch:** Represent the effective request with immutable collections and immutable readiness values, or defensively freeze all public-constructor inputs at the public launch boundary. Keep From's copied source configuration separate from the effective runtime request. Extend copy tests to readiness and stop settings; document exactly which constructor/factory guarantees apply.
- **Cost / risk:** S–M; low internally, medium if changing public constructors. Preserve actual Linux/Windows differences behind IProcessPlatform rather than adding OS checks to ManagedProcess.

## Test-quality issues

1. **Missing cancellation regressions:** ManagedProcessTests.cs:420–:439 only covers successful restart, with a ten-second token that does not cancel in normal execution. Session's cancellation tests cannot cover the independent helper. A1's source-linked probes fail the intended cancellation behavior while that happy-path test passes. Add idle/pre-cancelled, active/pre-cancelled, blocked-stop cancellation, and cancellation-before-relaunch cases.
2. **Blocked-write test can pass without a blocked write:** ManagedProcessInterruptInputTests.cs:99–:102 waits only for Stopping. That state is set at ManagedProcess.cs:142, before the call to WriteStopInput at :169. The worker can be descheduled there and force kill can happen before any stdin write begins. Add a write-entry/block gate and assert the dispatch remains pending before killing. Confidence high from ordering; no flake was manufactured.
3. **Start notification test depends on a duration:** ManagedProcessTests.cs:460 assumes a 300 ms child outlives Start's notification. If Start's thread is delayed after launch, exit can be recorded first; both callbacks can observe Exited because Changed carries the supervisor rather than an event snapshot. Use a child gate released after observing the Running notification. This is a test flake, not proof that every transient snapshot must be observable under the current event contract.
4. **LaunchContractTests leaks ownership of redirected streams to GC:** LaunchContractTests.cs:39–:43 disposes the group/process only, despite ILaunchedProcess.cs explicitly leaving stream disposal to its owner. Close stdin/stdout/stderr in finally as well, and kill/await on callback-test failure. Current `exit 3` is short-lived, so this is deferred descriptor reclamation, not evidence of a surviving long-lived process in a successful run.
5. **Cleanup is weaker than its comment:** ProcessTestHelpers.cs:86–:95 checks only HasLeftovers after ForceKill. While the leader is active, that flag is normally false, so cleanup can immediately dispose without waiting for leader exit or a post-exit count. SIGKILL normally handles the process group, but this helper cannot independently establish that cleanup succeeded; after detach it has no group authority at all. Detached tests currently add manual group cleanup, but repeated code and cleanup assertions can skip later disposal if they throw. Consolidate fixture ownership and actual exit waiting.
6. **Important platform-contract branches lack tests:** LaunchContractTests checks ordinary exit and failure before a process starts. It does not exercise timeout after start, dispose versus callback, or callback publication on failed setup. StopImmediatelyAfterStartIsNotLost exercises 20 ordinary scheduler interleavings, but cannot drive the two-second timeout branch; its passing result does not contradict A2. Readiness tests cover normal ANSI and partial-line behavior; deterministic split escape/chunk tests at the matcher/output seam would better protect against pump-boundary assumptions.

No new claim is made that the old INT-ignoring-shell leak still occurs on a successful existing test run. The scripts now wait for the child SIGINT disposition, and the detach tests explicitly kill the saved group. Native Windows tests were not run and their early-return passes are not counted as validation.

## Commands run and outcomes

### Read-only inspection

Used `cat`, `sed`, `nl -ba`, `rg --files`, `rg -n`, and `ls` for the request, documentation, complete in-scope source/tests, and the limited dependencies described above. Searched for applicable AGENTS.md files; none applied in the repository or its inspected ancestor paths. `git status --short`, `git diff --stat`, and `git rev-parse HEAD` confirmed a clean tree and the expected HEAD. Some initial batched reads exceeded the output budget; relevant missing portions were read again in smaller slices. Missing optional ancestor AGENTS.md paths made the corresponding ls command exit 2; there was no repository-access failure.

### Source-linked probes

Created Probe.csproj and Program.cs via shell heredocs in the permitted scratch directory. The project compiles current repository Core source directly, excluding repository bin/obj. It does not build or mutate the repository projects.

```sh
dotnet build /tmp/cmdr-claude-run-review2/probe-02/Probe.csproj --nologo -v:q
```

Succeeded: 0 warnings, 0 errors, 2.32 s.

Executed each mode once:

```sh
dotnet /tmp/cmdr-claude-run-review2/probe-02/bin/Debug/net10.0/Probe.dll cancel
dotnet /tmp/cmdr-claude-run-review2/probe-02/bin/Debug/net10.0/Probe.dll blocked-cancel
dotnet /tmp/cmdr-claude-run-review2/probe-02/bin/Debug/net10.0/Probe.dll group-timeout
dotnet /tmp/cmdr-claude-run-review2/probe-02/bin/Debug/net10.0/Probe.dll late-diagnostic
```

All probe hosts completed normally; outputs are quoted in A. They demonstrate wrong behavior rather than returning failing assertion codes. Each child/group was explicitly force-killed and its exit awaited; the direct-platform probe also explicitly disposed all streams.

After **each** probe/test run, checked for surviving probe/test processes with pgrep. The first broad `pgrep -fa 'probe02-|Probe.dll|cmdr-config-tests'` also matched the sandbox's command wrapper, so later checks anchored the actual executable names:

```sh
pgrep -fa '^(/usr/share/dotnet/dotnet |dotnet |/bin/sh -c |/usr/bin/python3 -c ).*(probe02|Probe.dll|cmdr-config-tests)'
```

No surviving matching workload process was reported by the anchored checks (pgrep exit 1 means no match). A final `pgrep -fa '^sleep '` also returned no matches. No unrelated processes were killed. Where a shell command combined a successful probe/test and a no-match pgrep, the overall shell exit was 1 because pgrep was last; this is not a probe/test failure.

### Focused existing tests

```sh
dotnet test tests/Cmdr.Core.Tests --no-build --no-restore \
  --filter 'FullyQualifiedName~RestartStopsAndStartsANewRun|FullyQualifiedName~StopImmediatelyAfterStartIsNotLost' \
  --results-directory /tmp/cmdr-claude-run-review2/probe-02/test-results \
  --logger 'trx;LogFileName=focused.trx'
```

**2 passed, 0 failed, 0 skipped**, reported duration 781 ms. These were the existing compiled test binaries, deliberately not rebuilt to preserve the repository's read-only constraint. The source-linked probes establish behavior of current source independently. Test results: `probe-02/test-results/focused.trx`. No full suite was run. No Windows tests were run.

## Open questions and design ambiguities

These are questions, not assumed requirements or additional findings:

- What is the intended supported future of the public ManagedProcess.RestartAsync helper? Session no longer uses it. Keeping it requires bringing its cancellation behavior in line with the already-fixed operation path; removing it would be an API decision.
- What is the subscriber failure contract for Changed and ProcessLinesCompleted? Changed invokes subscribers without containment (ManagedProcess.cs:790). A subscriber exception on an exit/timer thread can escape; on a readiness thread it can fault a pump. No throwing production subscriber was established, so this is not reported as a new P1. Decide and document the contract before choosing catch/continue, reporting, or propagation semantics.
- Is restricted `/proc` explicitly unsupported, or should unreadable live group members produce unknown accounting? TryReadStat currently treats disappearance and access/read errors alike. Likewise, numeric-only group ownership and positive-PID fallback retain PID-reuse risks, especially for a delayed captured control action. No PID-reuse or restricted-proc failure was induced, so neither is a separate bug finding. A stat-before-kill check alone would not give atomic identity protection.
- Docs still describe some superseded mechanisms: the design's Windows stdin-handle swap was replaced by native launch, and it says stdin is never closed although next-start/dispose close it. These stale explanations increase audit difficulty, but are not counted as cosmetic findings.

Known limitations were not reported again: Linux descendants deliberately creating a different group/session; background noninteractive jobs ignoring SIGINT; Linux Dispose intentionally leaving processes alive; setsid target-exec failure appearing as a nonzero process exit; Windows batch stdin-answer limitations; and already-recorded native Windows validation gaps.

## Remaining problems and recommended next step

All three A findings remain unfixed because the request prohibits repository modifications. Recommended next step is narrow regression-and-fix work for A1 and A3, followed by an isolated timeout/callback-cleanup change for A2. Preserve the working Session restart ownership and the Process.Dispose-outside-lock rule. Start with B1/B2 only where needed for those fixes; defer broader B4 restructuring. Resolve the explicit callback-failure and public-helper API questions before implementing behavior dependent on those answers. Native Windows verification remains with the other worker/user as recorded in TRACKER.md.

## Files changed in the repository

none
