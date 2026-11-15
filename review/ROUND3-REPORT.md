# Third review — 2026-11-15

A full review of the code at `a27a7ce`, not only of recent changes. It also looks at how the code is organized, to find
what keeps letting bugs in.

It was done by eight codex workers (GPT 6.1 Sol), one at a time and read-only:
- One worker for each area: config, process lifecycle and Linux, Windows (from the code only), output, session, TUI
  controller, TUI views.
- A final worker that worked across areas.

The coordinator checked every finding against the code and set its final severity; the workers' ratings were often
higher. Evidence is in [round3/](round3/):
- Each worker's result, `NN-worker-result.md`.
- The coordinator's verdicts, [00-coordinator-verdicts.md](round3/00-coordinator-verdicts.md).

The probe programs the workers mention lived in a scratch directory outside the repository, and are gone.

**Result: 29 open findings: 5 P2 and 24 P3. There is no P1.** Most need precise timing, unusual input or a direct
API call. The five P2s:

| ID | Finding | Status |
|---|---|---|
| 05-R3-01 | "Force kill now" in a "Restart already pending" dialog that was left open kills the run that has since restarted (introduced by d34b2bc) | Confirmed |
| 01/R02 | A save through a symlink whose target has a missing folder or a regular file before `..` overwrites a different file | Confirmed (reproduced) |
| W03-04 | The Windows containment test kills any `ping.exe` started near the test, including the user's own. **Fix before running the Windows checklist.** | Confirmed (code) |
| 01/R01 | Windows: if `ReplaceFile` fails half-way (error 1176), cleanup deletes the temp file that holds the only surviving copy | Plausible (Microsoft docs; Windows only) |
| W03-03 | Windows: a batch file whose cwd is a UNC path runs in `C:\Windows`, because cmd.exe refuses UNC current directories | Plausible (known cmd behaviour); needs a decision (Q6) |

## All open findings

| ID | Sev | Finding | Roadmap |
|---|---|---|---|
| 05-R3-01 | P2 | Stale pending-restart confirmation kills the replacement run | S1 |
| 01/R02 | P2 | Missing or file component before `..` redirects a symlink save | S2 |
| 01/R01 | P2 | Windows ReplaceFile 1176: cleanup deletes the surviving temp file | S2 |
| W03-04 | P2 | Windows containment test kills unrelated PING processes | S0 |
| W03-03 | P2 | Windows batch with a UNC cwd runs in the Windows directory | S9 (Q6) |
| 01/R03 | P3 | Hard-link log aliases pass the duplicate-log check | S10 |
| 01/R04 | P3 | Saving clears setuid, and setgid with group-execute (the last write clears them) | S2 |
| 01/R05 | P3 | `cmdr ""` crashes with an unhandled ArgumentException | S10 |
| 02/A1 | P3 | `ManagedProcess.RestartAsync` ignores cancellation; it has no production caller | S10 (Q8: delete it) |
| 02/A2 | P3 | Linux: a launch that takes over 2 s to get its own process group is reported as successful anyway | S10 |
| 02/A3 | P3 | A late "stop failed" from an earlier run is written into the next run's output | S10 |
| W03-01 | P3 | A NUL in an argument silently cuts the Windows command line short | S10 |
| W03-02 | P3 | A literal `--%` argument breaks the PowerShell copy | S9 |
| W03-05 | P3 | The batch copy snippet overwrites and removes an existing `$env:CMDR_CMD` | S9 |
| 04-01 | P3 | Reusing a waiting log writer applies another file's flush position, so its replay is skipped | S5 |
| 04-02 | P3 | Concurrent EnableLog calls exceed the one-running-one-waiting writer bound | S5 |
| 04-03 | P3 | A replay permanently raises the log writer's queue limit | S5 |
| 04-04 | P3 | A forced line break splits an emoji ZWJ sequence or flag when its high surrogate lands on the boundary | S8 |
| 05-R3-02 | P3 | Cancellation between a sequential restart's registration and its launch is ignored (API only) | S4 |
| 05-R3-03 | P3 | A command moved to another group between the parallel starts of one restart launches in its new group | S4 |
| 05-R3-04 | P3 | `SequenceRun.Result` is a completed placeholder until launch, so a concurrent restart sees it as finished | S3 |
| 06-R3-01 | P3 | F5 queues the restart before registering it, so a Stop or quit landing first can't revoke it | S3 |
| 06-R3-02 | P3 | Single-line edit fields cut off a value at its first newline when the dialog is accepted after an unrelated edit | S7 |
| 06-R3-03 | P3 | Activation works out its targets again after the input dialog, and starts newly startable commands without their inputs | S4 (Q1) |
| 07r-01 | P3 | Copy checks the selection, then reads the lines separately, so output arriving between them can be copied instead | S6 |
| 07r-02 | P3 | Completing a selected stderr partial moves it one line back, and the forward-only pin lookup loses the selection | S6 |
| 07r-03 | P3 | A zero-width character in the info pane shifts the rest of the row and clips its last character | S8 |
| 07r-04 | P3 | The POSIX copy leaves a program named `if`, `do` or `X=1` unquoted | S9 |
| 07r-05 | P3 | `DetailsSelection.Extract` throws on empty input; the UI guards against it | S10 |

The test-quality issues are listed in each worker's result. Notable ones:
- The status-message test still passes with its fix removed (checked by removing the fix).
- The in-place save tests pass under root without ever writing in place.
- The session cleanup helper gives up silently while processes are still alive.
- The concurrency test has no barrier to make the overlap actually happen.
- Some ANSI reset tests would pass if the reset did nothing.
- An "indexed background" color test actually uses an RGB color.
- `EnvTextTests:61` asserts something that is always true.

## Why the bugs keep coming: seven recurring classes

From [08-worker-result.md](round3/08-worker-result.md), which maps the findings of all three rounds to these classes.

1. **Authority checked before a delay, not carried through it.**
   - What happens: a decision is checked, then a lock is released, an await or modal dialog runs, or work is queued, and
     the old decision is acted on afterwards.
   - Examples: 05-R3-01 to 04, 06-R3-01, 06-R3-03, 04-02; round 1 05-01/03/04/05; round 2's restart boundary fixes.
2. **Identity, progress and cleanup kept on the reusable container instead of the lifetime they belong to.**
   - What happens: things that belong to one run, operation or log destination are kept in shared fields.
   - Examples: ManagedProcess's per-run fields beside a partial `RunPipes`; CommandOutput's single global log cursor;
     pending restarts identified by `CommandEntry`; pins that assume a line only moves forward.
   - Findings: 02/A3, 04-01 to 03, 05-R3-01, 07r-01/02, W03-04; round 2's Dispose deadlock, pin identity and
     waiting-writer fixes.
3. **Representation boundaries that lose information or confuse units.**
   - Units mixed up: UTF-16 units vs. scalars vs. graphemes vs. columns, and program token vs. argument token.
   - Codecs that don't survive the real boundary: editor fields that lose values passing through the real control;
     quoting that the real shell reads differently.
   - Findings: 06-R3-02, 04-04, 07r-03/04, W03-01/02/05; round 1 04-xx and 06-01/02/05.
4. **Filesystem names, file identity and commit outcomes conflated.**
   - One resolver serves both "existing file to save over" and "path that may not exist yet".
   - A bool stands in for the outcome of a replacement.
   - Comparing path strings stands in for file identity.
   - Findings: 01/R01 to R04, W03-03; round 2's symlink and UNC fixes.
5. **Rules and failure meanings duplicated, or left implicit.**
   - Preflight, hints and execution each compute their own target set. There are two restart implementations.
   - A completed task means "not launched yet"; an expired deadline means success.
   - Findings: 02/A1/A2, 05-R3-02 to 04, 06-R3-03, 07r-05, 01/R05.
6. **Bounds applied at the wrong stage.** Example: replay raises the live queue limit. Findings: 04-02/03; round 1
   02-05, 04-10, 04-11.
7. **Tests that show agreement or eventual completion, not the branch or invariant they claim.**
   - Sleeps used instead of gates that force the interleaving.
   - Cleanup that polls the very object under test.
   - Harnesses that copy the production logic instead of calling it.
   - Early returns that count as passes.
   - This is why repairs pass their tests yet miss nearby states.

The overall architecture is sound:
- Core doesn't depend on the UI.
- Platform code sits behind interfaces.
- Snapshots and lines are immutable.
- Session already has restart ownership and a tree lock.

The defects sit where a guarantee stops at a component boundary. The fix is to carry the guarantees across those
boundaries, not to rewrite.

## Roadmap

"Fix now" steps are small and independent. "Structural" steps should be planned deliberately and delivered as separate
patches. Full detail, with files, dependencies and how to verify each step: [08-worker-result.md](round3/08-worker-result.md), section 3.

| Step | Kind | What | Fixes | Cost |
|---|---|---|---|---|
| S0 | Fix now, first | Windows test fixture that reports and kills only its own child (not processes picked by name and start time); owned test-process helpers as tests are touched | W03-04 | S–M |
| S1 | Fix now | "Force kill now" acts on a pending-restart handle (operation + waited-for run) and kills only that run if it is still current. A re-check in Session alone is an S stopgap that narrows, but doesn't close, the race | 05-R3-01 | M |
| S2 | Fix now | (a) strict resolution for an existing save target, separate from prospective log paths; (b) classified replacement outcome that keeps a surviving temp; (c) restore the final mode after the last write | 01/R02, R01, R04 | M each |
| S3 | Fix now | (a) `SequenceRun` completion from a TaskCompletionSource created with it; (b) register the restart synchronously before F5 queues it | 05-R3-04, 06-R3-01 | S, S–M |
| S4 | Structural | One action authorization carried from planning through the input dialog to every launch: cancellation handoff to sequences, membership re-checked at each launch, then one action decision shared by preflight and execution (later, hints) | 05-R3-02/03, 06-R3-03 | M–L |
| S5 | Fix now | Per-destination log state (flush position, running and waiting writer, open reservation); separate replay and live counts; re-check after open | 04-01/02/03 | M |
| S6 | Structural | Backward pin lookup first (S); then one atomic publication of completed line + replaced partial, and one coherent read per copy, later per frame | 07r-02, 07r-01 | S, then M–L |
| S7 | Fix now | Field drafts that keep a value the real control can't hold (CR/LF), for every editor field | 06-R3-02 | S–M |
| S8 | Fix now | Complete the scalar before forced-break decisions; draw the info pane by measured grapheme positions | 04-04, 07r-03 | M, S–M |
| S9 | Fix now | Always quote the POSIX program; handle `--%` by meaning, not quoting; save and restore `CMDR_CMD`; decide the UNC batch cwd policy (Q6) | 07r-04, W03-02/05, W03-03 | S–M |
| S10 | Fix now | Standalone: run-qualified stop diagnostics; delete `ManagedProcess.RestartAsync`; typed Linux group-establishment result; reject NUL args; empty CLI argument; empty extract; hard-link identity for log conflicts | 02/A1–A3, W03-01, 01/R05, 07r-05, 01/R03 | S each (R03, A2: M) |
| S11 | Structural, later | Finish the per-run owner in ManagedProcess, then group-operation leases and helper deadlines on Windows, only as far as S1/S3/S4 need | none exclusively | M–L |

Order: S0 before any Windows testing. Then S1, S2 and S3 alongside each other. The other fix-now steps can ship
independently. S4 and S6's second half are the main structural investments, together with S5's destination owner.

Deliberately not recommended:
- An immutable config/DTO rewrite.
- A wholesale lifecycle state reducer.
- A universal text-layout or shell-quoting layer.
- A virtual filesystem.
- A repository-wide analyzer project.

None of these is justified by the bugs found.

## Practices that would catch these earlier

1. **Document the threading and ownership model** in docs/design.md: a short table saying what is UI-only, what the
   session lock guards, what belongs to a run or a log destination, and which identity must cross each await, modal
   dialog and background dispatch. Correct the stale stdin-swap explanation at the same time.
2. **Race tests use transition gates, not sleeps.** Both sides must be shown to have reached the transition before
   either is released. A "blocked write" test asserts that the write actually started. Use fake time for deadlines.
3. **Independent oracles.**
   - Inode for replace vs. in-place.
   - OS liveness for child processes.
   - The argv the child actually received, for shell claims.
   - Driver cells for layout.
   - Explicit expected text and styles for the parser.
4. **Mutation-check critical regressions.** Remove the guard locally and confirm the test fails. This would have caught
   the status-message test.
5. **Fixtures own construction, work and teardown.** Teardown fails with diagnostics instead of quietly giving up, and
   never kills processes by name.
6. **Visible skips instead of early returns**, a capability check before ownership tests, and a shared corpus of nasty
   inputs:
   - CR/LF, ZWJ emoji, `--%`, NUL, empty strings.
   - Shell keywords as names.
   - `%` and `!` in arguments.
7. **Postconditions after the last stage.**
   - File mode after the final write.
   - Progress only after a flush.
   - Completion only after unregistering.
   - Queue bounds after a replay drains.

## Open questions for the user

The roadmap avoids guessing these; the steps that depend on them wait for an answer.

- **Q1 — decided:** Enter starts exactly the commands that were startable when it was pressed. (06-R3-03: freeze the
  prepared start set, and re-check ownership and state before launch.)
- **Q2 — decided:** consistent with command-list restarts: a group restart whose original command moved out of the
  group meanwhile is cancelled.
- **Q3 — decided:** a caller's cancellation token governs a sequential restart until its first command has started;
  after that, stopping it is Stop/Kill's job. (05-R3-02)
- **Q4 — decided:** logging A, then B, then A again writes to A only the lines it missed; a log file never gets a line
  twice. (S5)
- **Q5 — decided:** editors keep trimming, but only fields that were edited; an untouched field keeps its exact value. (S7)
- **Q6 — decided:** refuse a UNC working folder for batch files, with a clear error. (W03-03)
- **Q7 — decided:** a batch-file argument containing CR or LF is refused at start with a clear error, and cmd.exe is
  always launched with `/v:off`, so registry-enabled delayed expansion can't change how arguments are read.
- **Q8 — decided:** delete the unused `ManagedProcess.RestartAsync`. (02/A1)
- **Q9 — decided:** an exception from a process-change subscriber is contained and reported as a `cmdr error: ...` line in
  that command's output.
- **Q10 — decided:** a `/proc` entry that can't be read counts as an unknown leftover (`? left`), not as gone; a Ctrl+C or
  stop input still being sent when the command is detached is dropped; Windows children with their own console are a
  documented limitation (force kill still covers the whole job).
- **Q11 — decided:** saving guarantees crash atomicity (never a half-written file), not power-loss durability. Kept on
  Linux: mode, owner and group; POSIX ACLs and xattrs are not preserved (documented). On Windows the file's ACL is kept
  (File.Replace, and the in-place fallback); this is part of the pending Windows verification.
