# Worker 06 handoff — TUI controller, dialogs and flows

Reviewed HEAD: `a27a7ce65b3e3338a18596d700f82dcc5f982e7c`, branch `review-fixes`, on Linux. This was a read-only review. No implementation or new design phase was started. Three additional production defects were confirmed with executable probes; the pending-restart dialog defect supplied in the request is recorded separately below, once. A regression-test weakness was also demonstrated by mutation testing.

## 1. Findings (A), severity order

### 06-R3-01 — P1: A queued controller restart can escape Stop/Quit cancellation before Session registers it

- **At HEAD:** `src/Cmdr/Tui/MainController.cs:778` (dispatch), `:783` (registration happens later); `src/Cmdr/Tui/QuitFlow.cs:40` (no-active-process exit path). Relevant API: `src/Cmdr.Core/Session/Session.Processes.cs:215`, `:298`, `:485`.
- **Defect:** F5 gives an untracked thread-pool task authority to restart without a caller cancellation token, so StopAll/ForceKillAll can finish before that task registers a restart and launches commands.
- **Concrete scenario:** Confirm F5 on a currently idle command, then quit before the worker reaches `Session.RestartAsync`. Quit sees no active processes, calls StopAll, and allows exit. The worker subsequently registers a new operation and starts the command. The same ordering can defeat an explicit Stop before worker registration. During real application exit there is an interval before Session.Dispose; on Linux a process started in that interval is not killed by disposal. A delayed thread pool increases the window. Session's existing revocation fixes correctly handle registered operations but cannot find this one yet.
- **Evidence:** Actual MainController `RestartTargets`, actual Confirm modal, and actual QuitFlow were executed. A probe held Session's existing `_sync` monitor on the UI thread across dispatch and quit, making worker entry wait until after quit; UI calls can re-enter the monitor. This is a deterministic scheduling gate, not a modification to production code or cancellation state. Output in `probe-06/output5.txt` and `output7.txt`:
  ```text
  pending after F5=0
  QuitFlow allowed=True
  started after quit=True
  ```
  The started command was `/bin/sh -c "echo started > '<probe directory>/restart-marker'"`, so the file establishes a real launch after quit. The probe waited for the leader and cleaned up. It tests the interval before Session.Dispose, not a launch after disposal.
- **Confidence:** high.
- **Suggested fix:** Own/reserve the controller operation synchronously before dispatch. Pass a token that Stop, successful quit, and controller disposal can revoke even before Session entry. Track task completion. Preserve Session's existing operation revocation as the second boundary. Do not simply call the whole current async restart on the UI thread: it can perform work synchronously before its first incomplete await. Add a gated controller/quit regression, including force quit, ordinary Stop, and repeated F5 before worker registration.

### 06-R3-02 — P2: Accepting unchanged single-line editor fields silently truncates valid config strings

- **At HEAD:** `src/Cmdr/Tui/CommandDialog.cs:105`, `:106`, `:107`, `:132`, `:153`, `:154` (initial values into TextField); `src/Cmdr/Tui/GroupDialog.cs:88`, `:98`; command field factory `CommandDialog.cs:212` and group field factory `GroupDialog.cs:159`.
- **Defect:** The lossless guards protect args/env only, while Terminal.Gui TextField truncates other original strings at a newline before the form reads them back.
- **Concrete scenario:** On Linux, load a command whose working directory or executable filename contains LF, open F2, change an unrelated field, and accept: `first\nsecond` becomes `first`. A valid multiline regex also changes on accepting unchanged fields, on either platform; for example a free-spacing pattern `(?x)ready\n|done` loses the `|done` branch. Command/group names and log paths use the same single-line control boundary. Linux permits newline characters in filenames, and these config strings are not rejected by config validation. This finding is about truncation, independent of the intentionally tested whitespace trimming policy.
- **Evidence:** The real CommandDialog was constructed with initial program/cwd/readiness strings containing LF and CRLF, and its actual TryCommit invoked with a capturing callback. Output in `probe-06/output7.txt`:
  ```text
  singleline original="first\nsecond" saved program="first" cwd="first" ready={"Pattern":"first","Busy":"first"}
  singleline original="first\r\nsecond" saved program="first" cwd="first" ready={"Pattern":"first\r","Busy":"first\r"}
  ```
  The group path uses the same TextField factory and read/build pattern; that extension is source-established, not a separately executed group probe. Internal CR/tab args/env were also probed and retained correctly; no new args/env defect is asserted.
- **Confidence:** high.
- **Suggested fix:** Extend representability/preservation to every field. Preserve an unrepresentable original value with a read-only explanation, or use an explicit reversible representation. Test actual controls and compare full specs/settings after an unrelated edit, rather than only testing Form.Build on strings that have never passed through TextField.

### 06-R3-03 — P2: Activation recomputes its launch set after the input modal without gathering newly required inputs

- **At HEAD:** `src/Cmdr/Tui/MainController.cs:649`, `:658`, `:665`; relevant preflight/launch API: `src/Cmdr.Core/Session/Session.Processes.cs:350`, `:380`, `:16`, `:54`.
- **Defect:** Inputs are collected for the pre-modal startable commands, but activation after the modal can include commands that became startable while the dialog ran.
- **Concrete scenario:** A parallel group has running command A using `${input:b}` and idle B using `${input:a}`. Enter on the group prompts only for `a`. A exits while the user is entering it. On accepting, activation includes A as well as B: A fails with `no value for input 'b'`, while B starts. In a sequential plan the same stale preflight can instead cause a step failure and cancel later work. No opportunity to provide `b` was offered. This also applies to marked sibling-command activation.
- **Evidence:** Actual controller Activate and InputDialog were run. A was `/bin/sleep ${input:b}`, initially launched with `b=30`; B was `/bin/echo ${input:a}`. Inside the modal timer the probe ended A, waited for it to become startable, filled the only prompted field, and accepted. Ending A by force kill was a deterministic substitute for natural exit; the relevant post-modal state is exited/startable. Output in `probe-06/output7.txt`:
  ```text
  preflight requests=a
  during input a.CanStart=True
  after input a state=Exited b state=Exited
  a output=...|failed to start: no value for input 'b'
  ```
  B was launched successfully; the error belongs to the newly included A.
- **Confidence:** high.
- **Suggested fix:** Before any launch, revalidate the prepared action and its input requirements. If the current plan needs additional values, gather them first or invalidate/retry the action without side effects. Alternatively freeze the explicitly prepared start set and execute only that set, with ownership/state checks. Which set should win is a product question below; silently launching a plan with missing inputs is wrong under either policy. Cover required-over-optional input merging when a newly startable command shares a name.

### Supplied issue, confirmed by source — P2: Pending-restart dialog can kill the replacement run

- **At HEAD:** `src/Cmdr/Tui/MainController.cs:814`, `:829`, `:839`; `src/Cmdr.Core/Session/Session.Processes.cs:170`.
- **Defect:** “Force kill now” applies to the captured CommandEntry list without verifying that the restart/run shown by the modal is still pending.
- **Concrete scenario:** Open the second-F5 pending-restart dialog. The old run exits and its restart completes while the modal remains open. Select Force kill now: `ForceKillForPendingRestart` unconditionally calls each entry's current `Process.ForceKill`, killing the successfully restarted replacement with no restart left to launch it again.
- **Evidence:** Complete call-path inspection. The API does not itself inspect `_restarts` or old run identities; the controller does not re-query after the modal. No separate runtime probe was run for this already-supplied finding.
- **Confidence:** high.
- **Suggested fix:** Bind the decision to a restart operation and the run(s) it was waiting for. An atomic Session operation should escalate only those still-pending waits. Re-querying pending entries after the modal narrows the bug but is not a complete run-identity guarantee if a restart advances between query and kill.

## 2. Organization and pattern proposals (B), ranked

These are concrete review recommendations, not an implementation/design phase. Costs are relative: S = localized change, M = several coordinated components, L = broad contract change.

1. **Synchronously owned UI operations, with a launch-authority boundary.**
   - **Prevents:** queued work escaping stop/quit (06-R3-01), and regressions in historical 05-01/05-03 shutdown cancellation. It also makes rapid repeated F5 observable before worker registration.
   - **Files:** MainController, QuitFlow, Session.Processes, RestartOperation; controller integration tests.
   - **Sketch:** create a controller operation handle/token before queueing; reserve targets and track completion. Stop/quit revokes both queued UI handles and registered Session operations. Worker entry requires live authority. Dispose closes the dispatcher and prevents late UI notifications from touching closed views. Keep background waits/I/O outside UI and Session locks.
   - **Cost/risk:** M / medium, because cancellation must preserve the current graceful-stop and cancelled-quit behavior.
   - **Priority:** first; patch the dispatch gap before broader cleanup.

2. **Treat modal decisions as versioned intents, not captured mutable entries.**
   - **Prevents:** supplied pending-restart replacement kill, stale input preflight (06-R3-03), and the same state/identity confusion behind historical restart-boundary fixes (9b3ae25).
   - **Files:** MainController activation/restart/stop flows, QuitFlow, Session.Processes, RestartOperation, InputDialog call boundary.
   - **Sketch:** prepare an intent describing targets, desired action, input requests, and operation/run identities. A pure policy decides whether a current snapshot still satisfies it. After modal return, Session validates/applies it atomically or returns a typed “changed/retry” result before side effects. Keep force-kill authorization tied to the run being confirmed. Test transitions using gates at modal entry/exit.
   - **Cost/risk:** M–L / medium; scope the first changes to activation inputs and restart escalation.
   - **Priority:** second, alongside the P2 fixes.

3. **One explicit editor codec/preservation contract for every field.**
   - **Prevents:** 06-R3-02 and recurring historical 06-01/06-02 no-op-edit data loss.
   - **Files:** CommandDialog, GroupDialog, CommandForm/GroupForm, ArgsText, EnvText; MultilineFieldDialogTests and form tests.
   - **Sketch:** each field owns its original value, representable display value, and edited/kept state. Converting through the actual control must either round-trip or retain the original. Build the committed spec from that owned draft. Share preservation rules between command/group editors; don't rely on ad hoc nullable `Kept*` fields that cover only the last discovered cases.
   - **Cost/risk:** M / low-to-medium; localized editor behavior but persistence needs exact-value regressions.
   - **Priority:** third.

4. **A single action-decision model for hints, preflight and execution.**
   - **Prevents:** duplicated rules drifting among EnterHint, HintLeft, IsStoppable, input preflight and Session.Activate. The current stale-input defect is a demonstrated consequence of treating preflight as a separate approximation; ancestor sequence reservations are also richer than HintLeft's direct-group boolean. The latter is a review risk, not an additional verified finding here.
   - **Files:** MainController, EnterHint, Session.Processes, tests/EnterHintTests and new policy tests.
   - **Sketch:** expose a pure decision result such as NoAction / Start(plan, requests) / Kill(run identities) / Refused(reason). Status hints consume the same result; dialogs only gather data; execution validates the decision's revision. Exhaustively test snapshot transitions and ancestor-run blocking without a terminal or subprocess.
   - **Cost/risk:** M / medium; retain the public Session action methods as wrappers initially.
   - **Priority:** fourth; implement as part of proposal 2 where possible, avoiding competing rule sources.

5. **A deterministic UI harness and failure-safe temporary ownership.**
   - **Prevents:** regressions hidden by uninitialized test state (the demonstrated status-test weakness), modal/default-button coverage gaps, and helper cleanup leaks on setup failure.
   - **Files:** MainControllerStatusTests, ConfirmTests, InputDialogLayoutTests, MultilineFieldDialogTests, SessionFixture, TempChain; a shared test harness.
   - **Sketch:** provide an ANSI application fixture with explicit “first layout/stable hints/modal opened” steps, controllable clock, bounded run termination, and owned view disposal. Offer typed test access rather than repeated private-field reflection. TempSession.Open/TempChain.Open should clean directories if save/load throws before returning, and cleanup should run in finally if Session.Dispose throws. Any helper that starts commands should own stop/kill/wait cleanup: Session.Dispose alone does not kill on Linux. Current listed helper consumers mostly do not start commands, so this is prevention rather than a claim of a present process leak.
   - **Cost/risk:** S–M / low.
   - **Priority:** strengthen the status regression now (S), then add the minimal harness needed for findings 1–3.

## 3. Test-quality issues

### T1 — High confidence: the status-message regression passes with its production fix removed

`tests/Cmdr.Tests/MainControllerStatusTests.cs:17–26` calls Build, immediately shows a message, then runs the first fast tick. Build cached hints before rows/focus were established (`MainController.cs:96` versus RebuildRows later). The first tick therefore computes different hints for the newly focused group. That difference restores the display even if ShowMessage never invalidates `_shownHints`, so the test does not establish its claimed **unchanged hints** condition.

Executable mutation evidence, entirely under `probe-06/tests/mutant/`:

- Remove only `_shownHints = null;` from a scratch copy of HEAD MainController: the unmodified repository test still **passes, 1/1** (`mutant-output.txt`).
- Add one preliminary OnFastTick after Build in a scratch copy of the test, establishing the actual focused hints before ShowMessage: that test **fails against the mutant**, with `Actual: "Temporary status"` (`strengthened-mutant-output.txt`).
- Run the strengthened test against unchanged HEAD: **passes, 1/1** (`strengthened-baseline-output.txt`).

Fix the test by establishing and recording the pre-message hints, asserting they are unchanged by the scenario, then checking exact restoration and styling after expiry. No production status defect is alleged at HEAD.

### T2 — High confidence: one environment assertion is tautological

`tests/Cmdr.Tests/EnvTextTests.cs:61` asserts that `A` is absent from keys already filtered with `k != "A"`. That passes for every dictionary. Assert the exact successfully parsed overlay (`A=1`) and that the malformed line did not add anything.

### T3 — Coverage weaknesses, distinct from confirmed production findings

- ConfirmTests verifies index arithmetic, not an actual dialog's initial focus, Enter default, Esc result, or hotkey dispatch. The installed Terminal.Gui 2.5.0 XML docs do independently specify last-button-default behavior, so no contradictory default-button finding is asserted.
- MultilineFieldDialogTests use actual controls and useful exact-value assertions, but only args/env preservation and direct TryCommit; single-line values, cancel/default-key routing, and save/reload are absent. The 39 targeted form/editor tests passed alongside the new truncation probe.
- No existing QuitFlow/controller operation-lifetime tests cover the worker-registration gap, real nested-modal state changes, repeated quit/F5, or a run replacement while confirmation remains open. Core restart tests cannot establish ownership before the controller calls Core.
- `InputFormTests.Each_empty_required_input_gets_its_own_error` supplies only one empty required value. Add a case with two empty values and assert both diagnostics.
- InputDialogLayoutTests use a real 100 ms timer rather than an explicit layout-ready event. These runs were not used to claim a timing failure; a lifecycle-driven, bounded harness would be more deterministic. NumberTypingTests correctly inject timestamps, and MainControllerStatusTests deliberately moves expiry into the past rather than sleeping.
- SessionFixture/TempChain create their directories before all fallible setup completes, without failure cleanup. Dispose deletes only after Session.Dispose returns. Their best-effort IOException catch can also hide leftover directories. No leaked child process was observed in this review.

## 4. Work performed, commands and outcomes

### Read-only investigation

- Read README.md, docs/design.md, review/REPORT.md, review/06-ui-application.md, review/COMPLETED-FIXES.md (including Review of the fixes), and review/TRACKER.md. Known Windows verification and narrow-input-field limitations were not re-reported.
- Read every requested production file completely: MainController, QuitFlow, TuiHost, MainWindow, FocusCycle, StatusLine, StatusColors, Confirm, CommandDialog, CommandForm (also contains GroupForm), GroupDialog, GroupOptions, InputDialog, InputForm, ArgsText, EnvText, EnterHint, NumberTyping, CommandListSource, CommandListView, GroupTree, TreeRows, GroupSummary, RowLayout, RecentPicker, RecentRows.
- Read the named matching tests completely, including all RowLayout variants, plus GroupOptionsTests, GroupSummaryTests, and StatusColorsTests; read SessionFixture and TempChain. Followed Session ownership, activation/input, restart, stop, disposal, and Selection contracts only as needed for UI call sites. Views and CommandLineFormatter were not reviewed for findings.
- Used `cat`, `sed`, `rg`, `rg --files`, `nl -ba`, `ls`, and read-only git. No AGENTS.md was found, including a hidden-file search. Starting and finishing `git status --short` and `git diff --stat` were empty; HEAD remained `a27a7ce65b3e3338a18596d700f82dcc5f982e7c`.
- Checked installed Terminal.Gui 2.5.0 package XML docs for modal/session behavior, RequestStop, keyboard dispatch and MessageBox defaults. Real-control probes established TextField's newline handling; it was not guessed from helper code.

### Scratch probes and targeted tests

All probe sources/projects, mutated source copies, logs and final test outputs are under `/tmp/cmdr-claude-run-review2/probe-06/`. Production source was compiled by source inclusion into scratch projects, with the installed Terminal.Gui 2.5.0 dependency, avoiding repository build/obj writes. No full suite was run.

- `dotnet run --project .../probe-06/probe06.csproj` and later `dotnet run --no-restore --project .../probe-06/probe06.csproj`: final combined probe **exit 0** (`output7.txt`). Confirms launch-after-quit, single-line truncation and stale input collection. Initial CR/tab args/env checks passed and NUL-name preview did not throw; those suspicions were rejected.
- Narrow harness iteration: an initial single-thread-pool gate prevented modal progress and was interrupted (130); replaced it with a Session monitor scheduling gate. One attempt failed due to ambiguous reflection of Dialog.Result (134), corrected to the typed property. Another waited on the unsaved-changes dialog because probe setup made the Session dirty and was interrupted (130); saving the scratch config fixed setup. One attempt had a probe-only `OutputLine.Text` compile error (1), corrected to PlainText. These are harness failures, not application findings. Intermediate logs are retained as output2–output6.txt.
- `dotnet test .../probe-06/tests/probe06tests.csproj --filter 'FullyQualifiedName~CommandFormTests|FullyQualifiedName~GroupFormTests|FullyQualifiedName~MultilineFieldDialogTests' -p:NuGetAudit=false`: **39 passed, 0 failed** (`tests-output.txt`). A final rerun uses `TMPDIR=.../probe-06/tmp`, `--no-restore`, the same filter and unchanged source; **39 passed, 0 failed**, recorded in `tests-final-output.txt`.
- `dotnet test .../probe-06/tests/mutant/mutant.csproj --filter 'FullyQualifiedName~MainControllerStatusTests' -p:NuGetAudit=false`: unmodified test against cache-invalidation mutant **1 passed**.
- `dotnet test .../probe-06/tests/mutant/strengthened.csproj --filter 'FullyQualifiedName~MainControllerStatusTests' -p:NuGetAudit=false`: strengthened test against mutant **1 failed, as intended**.
- `dotnet test .../probe-06/tests/mutant/baseline/baseline.csproj --filter 'FullyQualifiedName~MainControllerStatusTests' -p:NuGetAudit=false`: strengthened test against HEAD **1 passed**.
- Early restores/runs emitted NU1900 because NuGet vulnerability-cache writes under the read-only home directory were denied; compilation/runtime probes still succeeded. Later tests disabled NuGetAudit. Initial test invocations used existing helpers' default temporary location; those helpers disposed their directories. The final rerun explicitly confines their temporary files through TMPDIR under the probe directory.
- After completed/failed/interrupted runs checked for probe/test children with `pgrep -fa`, ultimately using the anchored filter `^(/[^ ]*/)?cmdr-config-tests|^(/[^ ]*/)?probe06|^dotnet (run --.*probe-06|/tmp/cmdr-claude-run-review2/probe-06/)|^testhost` to exclude shell/sandbox command-text matches. No remaining matching children were found. Only the probe's own interactive executions were interrupted; no unrelated process was killed. Probe commands used force-kill/wait cleanup for their own managed children.

## 5. Open questions, remaining limitations, next step

- **Activation semantics after a modal:** should the operation execute exactly the set prepared when the user pressed Enter, or recompute current startable commands? Either can work if explicit and validated; the review does not choose a new product design. Required inputs must be complete before any side effect.
- **Minimum terminal dimensions:** command/group editors retain fixed 78×20 / 70×16 layouts, and quit-wait is fixed 56×7. The prior review explicitly left minimum-size behavior as a product decision; no new small-screen defect is counted. Decide the supported minimum before changing those layouts. Input-dialog clipped labels/one-cell fields are already recorded limitations.
- **Whitespace normalization:** form tests intentionally trim program/cwd/name/log-path values. Linux can have whitespace-significant paths. Whether to preserve those exact values is an existing policy ambiguity, not added to the confirmed newline-truncation finding.
- Native Windows behavior and physical-terminal/clipboard behavior remain unverified, as recorded in TRACKER; no Linux early-return Windows test is represented as Windows evidence.
- **Recommended next step:** narrowly fix/test 06-R3-01 first with a controller-owned scheduling gate regression; then fix the supplied escalation race and activation-input revalidation, followed by complete editor preservation. Strengthen the status regression using the demonstrated one-tick setup change. Apply proposals incrementally at those boundaries, without starting another broad design phase.

## 6. Files changed in the repository

**none**.
