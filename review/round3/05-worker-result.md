# Worker 05 result — session orchestration review

Reviewed branch `review-fixes`, HEAD `a27a7ce`, on Linux. Repository changes: **none**. This was the read-only review requested by 05-worker-request.md; no implementation or new design phase was undertaken. Four confirmed P2 defects remain. The ranked structural proposals below are deliverable B, not an implementation plan begun in the repository.

## Review coverage

Read every file in `src/Cmdr.Core/Session/*.cs` and `tests/Cmdr.Core.Tests/Session/*.cs` completely, including helpers. Read README.md, docs/design.md, review/REPORT.md, review/05-session-orchestration.md, review/COMPLETED-FIXES.md (including “Review of the fixes”), and review/TRACKER.md. Followed the necessary MainController action/event/restart callers, ManagedProcess launch/stop/exit/dispose contracts, LaunchSpec, and config loading, validation, export, path, environment, and input substitution boundaries.

Checked ownership/base-source restrictions; tree mutation locking, depth/cycle rules, export/dirty/save behavior; planned sequence membership/order, nested failures and sibling independence; selection/pruning/file order; input collection/substitution; expanded-state preservation; and status precedence/durations. No additional demonstrated defects were found in selection, StatusFormatter, base-source inheritance, or ordinary config export. Names can temporarily be invalid while editing and are rejected by validation/save, as the existing design and tests intend. Sequences wait for leader exit, including commands with ready patterns; ready state is display state and does not advance a sequence. Known tracker limitations are not counted as findings.

The important remaining failures are operation boundaries: registering versus launching, validating versus using mutable targets, and confirming versus acting on a particular process run.

## A. Findings, in priority order

### 05-R3-01 — P2: A stale pending-restart confirmation force-kills the replacement run

- **At HEAD:** `src/Cmdr.Core/Session/Session.Processes.cs:170` (especially line 180). Caller: `src/Cmdr/Tui/MainController.cs:814`, `:829`, `:839`.
- **Defect:** ForceKillForPendingRestart unconditionally kills the command's current process, even when the pending restart observed by the caller has already completed and that process is its replacement.
- **Concrete scenario:** A command is stopping for an F5 restart. The user presses F5 again and leaves “Restart already pending” open. While it is open, the old leader exits and the background restart starts the replacement. The user then chooses “Force kill now.” MainController passes the earlier list of CommandEntry references; ForceKillForPendingRestart kills the replacement, with no pending restart left to start it again. The intended action is to accelerate the old restart, but it instead stops the successfully restarted service.
- **Evidence:** Preserved probe mode `stale-force` queries the pending list, lets the old leader exit (via its supervisor's ForceKill, which deliberately does not revoke session restart ownership), awaits successful restart completion, and then submits that captured list. Output:

  ```text
  stale-force: old=14, replacement=24, pendingNow=0, replacementState=Exited, stopped=True
  cleanup: active=0
  ```

  Using an externally triggered old-leader exit avoids any need for terminal automation; the inspected controller has exactly this query/dialog/action gap. The probe does not claim to simulate a natural exit's exit code, which is irrelevant to the race.
- **Confidence:** High. Core behavior reproduced; TUI reachability established by caller inspection.
- **Suggested fix:** Return a pending-restart handle that identifies both the operation and the old process-run generation being waited on. On confirmation, kill only that still-current waited-for run. A fresh pending-set check alone is insufficient: another restart or another process start can race between checking and ForceKill. ManagedProcess needs a conditional kill by run identity, executed under its own lifecycle lock; do not call blocking process operations while holding Session._sync. Add a regression that leaves confirmation outstanding until replacement launch and verifies the replacement survives.

### 05-R3-02 — P2: Sequential restart loses caller cancellation before any process is launched

- **At HEAD:** `src/Cmdr.Core/Session/Session.Processes.cs:636`–`:640`, and the group equivalent at `:669`–`:678`; `src/Cmdr.Core/Session/SequenceRun.cs:29`–`:43`.
- **Defect:** Registering a sequential run removes the restart from tracking without transferring its cancellation token to that run, so cancellation in the registration-to-launch gap is ignored.
- **Concrete scenario:** Restart an idle sequential group with a live caller token. Register marks its command queued. Cancel the token before Launch schedules RunAsync (the queue EntryChanged notification is an exact gate for this boundary). RestartAsync returns normally and the command starts despite cancellation occurring before its process launch. Both the group and sibling-command restart overloads do this. Parallel restart cancellation has a before/after launch protocol; the sequential path bypasses it.
- **Evidence:** Probe hooks EntryChanged only to cancel when IsQueued is true. The supervisor is still NotStarted at that point. Outputs:

  ```text
  cancel-sequence: token=True, threw=False, state=Running, pid=19
  cleanup: active=0
  cancel-commands: token=True, threw=False, state=Running, pid=19
  cleanup: active=0
  ```

  The two PID values are from separate sandboxed invocations, not the same process.
- **Confidence:** High; reproduced for both overloads against unchanged HEAD source.
- **Suggested fix:** Make registration-to-launch an explicit ownership transfer. Retain restart cancellation until the run has accepted launch authority and crossed the defined start boundary; cancellation before then must cancel the run and cause RestartAsync to throw. Use a cancellation registration/launch gate owned by the run, disposed when no longer needed. Do not simply await the entire sequence from RestartAsync: its current contract returns after scheduling the replacement sequence. Add both overload regressions at the queued notification boundary. This is a remaining boundary of the earlier 05-03 fix, not a repeat of the fixed already-cancelled-token/blocked-stop cases. The TUI currently supplies no caller token, so this particular trigger is API-level.

### 05-R3-03 — P2: Parallel restart launches a target moved after batch validation

- **At HEAD:** `src/Cmdr.Core/Session/Session.Processes.cs:592`–`:599`; `:1113`–`:1126`; `:1157`. Contrast the membership check at `:1103`–`:1108`.
- **Defect:** Per-command restart checks and the final cleanup check inspect only token/revocation flags, so moving a later target after the initial membership validation does not invalidate its launch.
- **Concrete scenario:** Group g is parallel, containing idle a and b; destination is another editable group with potentially different cwd/env. Restart [a,b]. After a launches, the UI moves b to destination before the worker reaches b. UpdateCommand allows this because b is not in a SequenceRun. IsRevoked(op) remains false, StartEntry resolves b's new ancestry, and b launches in destination; RestartAsync reports success. The restart's documented “all commands must stay in the original group” rule is violated, and execution may use a different environment or relative executable than the user selected.
- **Evidence:** Probe `move-between-starts` uses a's Running notification to place the edit precisely between the starts, analogous to the existing boundary tests' notification hooks. Output:

  ```text
  move-between-starts: threw=False, bGroup=destination, bState=Running, pid=20
  cleanup: active=0
  ```

  The hook establishes the same interleaving that concurrent UI editing can produce; it is not a requirement that production event handlers perform edits. MainController explicitly runs RestartAsync on a background task while edits remain available.
- **Confidence:** High.
- **Suggested fix:** Apply the full stale-target predicate at every launch boundary and at batch completion, with cleanup of already-started targets on invalidation. Close the check/use gap by reserving launch targets, or by capturing an authorized immutable launch request plus a membership generation and handling invalidation across the actual start. Merely replacing IsRevoked with a check before StartEntry leaves a smaller check/use window. This extends the earlier moved-while-waiting fix to moved-while-starting; the existing test covers only the wait boundary.

### 05-R3-04 — P2: A registered sequence exposes a completed placeholder result before it runs

- **At HEAD:** `src/Cmdr.Core/Session/SequenceRun.cs:50`; publication in `src/Cmdr.Core/Session/Session.Processes.cs:819`, task assignment at `:834`, and restart wait at `:221`.
- **Defect:** SequenceRun.Result initially equals Task.FromResult(false), so a concurrent restart can mistake a registered-but-not-launched run for a completed one and fail to restart it.
- **Concrete scenario:** Activate a sequential group. Registration publishes ActiveSequence and IsQueued; before Launch assigns Result, another restart discovers that run, cancels it, and awaits the already-completed placeholder. It then immediately tries to register its replacement while the old ActiveSequence is still installed. It returns “A sequence is already running…” instead of waiting for the cancelled run to unregister and restarting. Once the original launch resumes, that run exits cancelled and nothing starts.
- **Evidence:** Probe `registration` gates the first queued EntryChanged notification on one thread, restarts the command on the other, then releases and joins activation:

  ```text
  registration: restartMessage=A sequence is already running in group 'seq'., state=NotStarted, sequenceActive=True
  cleanup: active=0
  ```

  No process was launched by this probe. A gate widens the scheduling window; a blocking subscriber is not required for the production race.
- **Confidence:** High; deterministic reproduction of the invalid published state.
- **Suggested fix:** Construct a readonly TaskCompletionSource<bool> (RunContinuationsAsynchronously) with the SequenceRun, expose its Task from construction, and settle it only after FinishSequence unregisters the run. Launch should schedule execution without replacing the public completion task. Ensure exceptions settle that same task too. Preserve the no-events-under-session-lock rule. Add a registration/launch overlap regression that verifies restart waits and eventually launches its replacement.

## B. Organization and pattern proposals, ranked

These proposals target actual bug classes, not naming or formatting. Costs: S = localized, M = several coordinated files, L = broad architectural change.

### 1. One launch-authority protocol for restarts and sequences

- **Priority:** First, correctness-critical.
- **Prevents:** Cancellation/lifetime gaps (past 05-01/05-03; current 05-R3-02) and mutable membership checks that are separated from use (past moved-while-waiting bugs; current 05-R3-03).
- **Files:** Session.Processes.cs, RestartOperation.cs, SequenceRun.cs, PlanNode.cs, Session.cs; restart boundary/ownership and concurrency tests.
- **Sketch:** Represent Reserved → Launching → Transferred/Completed or Revoked explicitly. A command launch holds an operation reservation and an authorized membership/version; edits invalidate or refuse that reservation according to the existing edit contract. Starting outside _sync must have a matching post-start reconciliation. Sequence registration transfers cancellation and launch authority explicitly instead of deleting the restart entry and forgetting its token. Centralize this protocol for command batches, root sequences, and nested branches.
- **Cost/risk:** M, medium-to-high risk because sequencing, edits, stop, and shutdown meet here. Implement as a narrowly scoped lifetime change with boundary regressions, not a broad UI rewrite.

### 2. Stable completion from construction; publish only fully initialized run state

- **Priority:** Second; small independent fix.
- **Prevents:** Current 05-R3-04 and future observers awaiting a placeholder or a replaced task.
- **Files:** SequenceRun.cs, Session.Processes.cs; SessionRestartBoundaryTests.cs.
- **Sketch:** Allocate one private completion source in the constructor, expose a get-only Result, register the already-valid owner, schedule work, unregister then complete/fault that source. Store the scheduler task separately only if needed for diagnostics. Express the invariant “registered implies its completion represents actual removal” in a test.
- **Cost/risk:** S, low-to-medium risk; completion ordering affects restart continuations and must be explicit.

### 3. Pending restart queries return operation/run handles, not reusable command identities

- **Priority:** Third, user-visible destructive race.
- **Prevents:** Current 05-R3-01, plus stale confirmation or delayed cleanup targeting a later run of the same CommandEntry. Past output bugs 02-02 already demonstrate why command identity alone cannot identify a process generation.
- **Files:** Session.Processes.cs, RestartOperation.cs, ManagedProcess.cs (small conditional-control seam), MainController.cs; boundary/controller tests.
- **Sketch:** Return a PendingRestartTarget with operation identity, command identity, and waited-for process generation. Confirm it with a conditional operation that can become a harmless no-op after completion/replacement. Apply the same generation principle when a delayed asynchronous control action must address one run. Keep process I/O outside the tree lock.
- **Cost/risk:** M, medium risk; it changes a Core/UI API and needs both a pure stale-handle test and one real-process integration test.

### 4. A shared pure action planner and reservation predicate

- **Priority:** Fourth.
- **Prevents:** Divergence among preflight, start, root/subrun registration, and busy checks (past 05-04, repaired by another guard; current repeated IsRevoked versus ThrowIfRevokedLocked split).
- **Files:** Session.Processes.cs, PlanNode.cs, SequenceRun.cs; input/tree-run/ownership tests.
- **Sketch:** Capture the necessary tree/process state under _sync. A pure planner returns canonical ordered targets, required inputs, refusal, and required reservations. Commit reservations through one function used by all entry points; execution handles I/O afterward. Preserve the existing policy that parallel siblings may proceed independently. Exhaustively table-test nested reservation and kill-first/preflight decisions without real children.
- **Cost/risk:** M, medium risk. Extract existing rules incrementally; do not invent a new execution policy.

### 5. An internal supervisor/scheduler seam and a process fixture that proves cleanup

- **Priority:** Fifth, supports all boundary fixes.
- **Prevents:** Missed transitions in current tests (all four findings), scheduler-dependent concurrency coverage, and silently leaked child processes (historical disposal test issue; current cleanup weaknesses below).
- **Files:** Session.cs AttachCommand, SequenceRun.cs/Session.Processes.cs scheduling calls; SessionTestHelpers.cs, SessionConcurrencyTests.cs, restart tests; a minimal ManagedProcess adapter seam.
- **Sketch:** Inject an internal supervisor factory and controllable scheduler that can pause at reservation, process start, exit, and registration removal. Keep several Linux/Windows real-process integration tests. A scoped real-process fixture records exact PIDs/run tasks before disposal, releases gates, waits for exit, kills only owned leftovers, and fails with diagnostics if cleanup cannot complete. Avoid launching arbitrary user callbacks under locks to achieve determinism.
- **Cost/risk:** M, low production-policy risk but moderate test migration cost. Keep this internal rather than creating a public plugin API.

### 6. Read snapshots/edit copies instead of exposing mutable config and live lists

- **Priority:** Sixth, prevents recurrence as callers grow.
- **Prevents:** The past 05-05 tree-reader/writer class, direct Spec mutation bypassing dirty/base-file rules, and callers retaining mutable live collection views. These are structural risks here, not additional claimed ordinary-TUI defects.
- **Files:** CommandEntry.cs, GroupEntry.cs, ItemEntry.cs, Session.cs, Selection.cs; MainController read/edit boundaries and config clone types as needed.
- **Sketch:** Keep mutable runtime entries private to Session; publish read-only snapshot collections captured under the tree lock and immutable command settings or explicit edit copies. Document which reads are owner-thread-only until converted. Use a tree version to reject stale action plans. Do not solve this by placing I/O or UI notifications under _sync. BuildConfig/Save currently run on the UI edit thread; no concurrent-save defect is claimed without a supported concurrent-save caller.
- **Cost/risk:** M–L, medium risk because current tests and UI retain entry identities/live lists. Introduce immutable settings and collection wrappers first, then versioned snapshots where asynchronous callers need them.

## Test-quality issues and missing regressions

1. **Boundary coverage misses execution shape.** SessionRestartBoundaryTests.CallerCancellationWhileStartingAParallelBatch_StartsNoMoreAndStopsWhatStarted tests parallel commands only. Ownership tests cover already-cancelled group tokens, but not cancellation after sequential registration. Add both sequential overloads from 05-R3-02. Likewise, CommandMovedAndItsGroupRemovedWhileWaiting covers waiting, not the gap between batch launches (05-R3-03).
2. **Pending-restart test proves only the still-pending case.** SessionRestartBoundaryTests.ForceKillForAPendingRestart_KeepsTheRestart_WhichStartsTheCommandAgain never retains the query result across restart completion. Add completed-operation, replacement-generation, and a newer overlapping-operation case (05-R3-01).
3. **Concurrency test does not guarantee overlap.** `tests/Cmdr.Core.Tests/Session/SessionConcurrencyTests.cs:34`–`:69` runs a bounded worker and edit loop with no start barrier or transition gate. Either loop can finish before the other gets meaningful execution. Missing inputs also intentionally avoid real launch transitions. It is useful stress coverage, but cannot establish the registration/start invariants. Add gated overlap tests and keep this as a supplemental stress test. Confidence: high, source-established test weakness.
4. **Cleanup can silently abandon live processes.** `tests/Cmdr.Core.Tests/Session/SessionTestHelpers.cs:43`–`:52` falls through after five seconds even if ActiveEntries is nonempty, then disposes supervisors; Linux disposal does not kill. It also does not join in-flight sequence/restart launches. The fixture should fail if owned children remain and retain enough process identity to clean them safely. No actual leftover was observed in this review's runs. Confidence: high for missing assertion/join, not a claim of an observed persistent leak.
5. **Dispose test has no exit handshake.** `tests/Cmdr.Core.Tests/Session/SessionRestartOwnershipTests.cs:140`–`:144` releases a shell after disposing its supervisor and immediately returns, without waiting on independent OS liveness. Its `&& [ -d ... ]` condition fixes the earlier persistent missing-release-file leak, so that old bug is not re-reported. The test can still leave the shell briefly alive across teardown and cannot prove cleanup. Capture PID/liveness before disposal and wait after release. Confidence: high.
6. **“All fields” round-trip test is incomplete.** SessionEditingTests.Load_ThenBuildConfig_RoundTripsAllFields does not set/assert nondefault Ready, Leftovers, or Stdin. The cloning code currently preserves them, so this is a regression-coverage gap rather than a production finding. Add nondefault values and assert built and reloaded config agree. Confidence: high.

Existing selection/status/tree/base/config tests generally exercise meaningful behavior. Platform early-return passes remain the already-recorded validation limitation; no Windows result is inferred from this Linux run.

## Commands run and outcomes

Read-only inspection used `pwd`, `rg --files`, `rg -n`, `ls`, `wc -l`, `cat`, `sed`, and `nl -ba` over the request, documentation, in-scope files, and the relevant callers/contracts. `git rev-parse --short HEAD` returned `a27a7ce`; initial and final `git status --short` and `git diff --stat` produced no changes. The initial AGENTS.md search under /srv/devshare/projects found none in cmdr.

All scratch source, builds, config data and test result directories were confined to `/tmp/cmdr-claude-run-review2/probe-05/`. Shell heredocs created probe05.csproj and Program.cs there; short Python scripts edited that probe only. The probe project directly includes the unchanged Core source, excluding repository bin/obj trees, and compiles it with .NET 10 and unsafe enabled. It has no ProjectReference that could rebuild repository outputs.

- `dotnet build /tmp/cmdr-claude-run-review2/probe-05/probe05.csproj --nologo` — four builds as probe modes were added; each succeeded, 0 warnings and 0 errors. All generated files remained under the probe directory.
- `dotnet /tmp/cmdr-claude-run-review2/probe-05/bin/Debug/net10.0/probe05.dll cancel-sequence` — exit 0; reproduced ignored cancellation; killed and awaited its owned child; active=0.
- Same executable, `move-between-starts` — exit 0; reproduced launch after move; cleanup active=0.
- Same executable, `stale-force` — exit 0; reproduced replacement killed from stale confirmation; cleanup active=0.
- Same executable, `registration` — exit 0; reproduced premature completed-result observation/refusal; no child launched; joined activation; cleanup active=0.
- Same executable, `cancel-commands` — exit 0; confirmed the sequential sibling-command overload also ignores cancellation; cleanup active=0.
- `dotnet vstest tests/Cmdr.Core.Tests/bin/Debug/net10.0/Cmdr.Core.Tests.dll '--TestCaseFilter:FullyQualifiedName~CallerCancellationWhileStartingAParallelBatch_StartsNoMoreAndStopsWhatStarted|FullyQualifiedName~ForceKillForAPendingRestart_KeepsTheRestart_WhichStartsTheCommandAgain' --ResultsDirectory:/tmp/cmdr-claude-run-review2/probe-05/test-results` — **2 passed, 0 failed, 0 skipped**, duration 316 ms. Used the existing repository assembly without building/writing repository artifacts. These are controls for the two suspected boundary gaps; the current-source probes establish the new failures independently of that assembly's provenance.
- After test/probe runs, checked for started processes with pgrep. Initial broad `pgrep -fa 'cmdr-config-tests|probe05|sleep 30'` matched the sandbox wrapper's own command text and was noisy. Follow-up anchored checks used `pgrep -fa '^dotnet /tmp/cmdr-claude-run-review2/probe-05/.*probe05.dll|^sleep 30$|^sh .*cmdr-config-tests'`; they returned no matches after cleanup. No unrelated processes were killed; probe children were killed through their own Session/ManagedProcess instances.

No full-suite test, repository build, commit, branch switch, stash, or repository file mutation occurred. No native Windows execution or terminal UI automation occurred. Probes deliberately control interleavings; they establish correctness failures, not incidence rates in normal use.

## Open questions and design ambiguities

- A group restart records original command identities but rebuilds its plan from the current subtree, whereas a command-list restart requires original direct-group membership. If an original command or subgroup moves outside a restarting group's subtree while it waits, is silently omitting it intentional, or should the whole restart cancel? The current docs specify original membership inclusion/exclusion for additions, but do not fully define relocation behavior for the group overload. I have not counted that policy choice as a defect or assumed an answer.
- Exactly when should caller cancellation stop governing a sequential restart: at registration, first process launch, or completion of the initial scheduling handoff? The documented promise rules out starting a process after cancellation before any launch (05-R3-02); the desired behavior after an already-started sequence continues needs an explicit boundary. This report does not propose cancelling the entire future sequence merely because a token cancels after RestartAsync has returned.
- Are public BuildConfig/BuildRows/AllGroups intended to support concurrent caller-side editing, or only the UI owner thread? Current documentation is narrower than “every public member is thread-safe.” I did not report their unguarded traversals as an ordinary TUI bug: background execution does not structurally edit the tree. Proposal 6 should make this contract explicit.

## Remaining problems and recommended next step

All four findings remain unfixed because the request is read-only. First add the four deterministic boundary regressions, then address the completion-publication bug and restart-to-sequence cancellation handoff in small patches; fix stale pending-restart control with operation/run identities; and close target membership authorization across parallel launches. Keep existing nested sibling-independence semantics and do not introduce blocking work under _sync. The ranked proposals describe how to prevent repeats without beginning a redesign. Re-run only the affected session tests during iteration, then use the coordinator's normal broader validation and already-tracked native Windows verification when changes are ready.

## Files changed in the repository

none

Outside-repository deliverables: this handoff and the preserved probe project/source/build/data/test-result directory under `/tmp/cmdr-claude-run-review2/probe-05/`.
