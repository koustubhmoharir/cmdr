## Verdicts

### 01 Config
- R01 Windows ReplaceFile 1176 deletes the surviving temp: PLAUSIBLE (documented API state; Windows-only, unverified). P2.
- R02 `..` after a missing component or a regular file redirects a save: CONFIRMED (code at PhysicalPath.cs:51 pops even when
  exists=false; worker probe reproduced). P2.
- R03 hard-link log aliases not detected: CONFIRMED, but niche. Downgraded to P3.
- R04 setuid / setgid+group-exec cleared by the write after the mode is set: CONFIRMED (probe; Linux clears these on a write by a
  non-root user). P3.
- R05 `cmdr ""` throws an unhandled ArgumentException: CONFIRMED (reproduced). P3.
- Test issues 2 (in-place tests pass under root without exercising in-place) and 4 (no timeout in process helpers): valid.
- Worker sandbox note: sudo is not available inside codex workers (no-new-privileges); dropped from later requests.

### 02 Process lifecycle / Linux
- A1 ManagedProcess.RestartAsync ignores cancellation during stop dispatch and before Start: CONFIRMED in code, but the method
  has no production caller (Session has its own restart). Downgraded to P3; recommend deleting the unused public helper.
- A2 WaitForOwnGroup falls through on its 2 s deadline and Launch reports success with a group id that isn't established:
  CONFIRMED (LinuxProcessPlatform.cs:96-106). Needs a >2 s stall before setsid; rare but possible under heavy load. P3.
- A3 a late stop failure from an earlier run is written into the next run's output: CONFIRMED (SignalGroup/WriteStopInput
  publish without a run identity). P3.
- Test issues 2 (blocked-write test may not block), 3 (300 ms timing), 4 (streams not disposed), 5 (cleanup helper weak): valid.
- Open question worth raising: Changed subscribers' exceptions are not contained.

### 03 Windows (static)
- W03-04 the containment test probe kills any PING process started near the test, inside the job or not: CONFIRMED
  (WindowsJobContainmentTests.cs TrackPingsInJob adds every recent PING; Dispose kills them). P2, and it should be fixed
  BEFORE the user runs the Windows checklist.
- W03-03 batch file with a UNC cwd: cmd.exe refuses UNC current directories and runs in C:\Windows: PLAUSIBLE (well-known
  cmd behaviour; no handling in the batch plan). P2; needs a policy decision (pushd wrapper vs. clear error).
- W03-01 NUL in an argument truncates the Windows command line: CONFIRMED in code; rare input. P3 (validate: reject NUL in args).
- W03-02 a literal `--%` argument breaks the PowerShell copy: CONFIRMED by pwsh probe. P3.
- W03-05 the batch copy snippet clobbers and removes a pre-existing $env:CMDR_CMD: CONFIRMED. P3.
- Test issues: early returns on Linux count as passes (known); formatter round-trip timeout doesn't bound ReadToEnd; streams
  not disposed in Windows tests. Valid.
- Open questions: multiline args to batch files (CR/LF passed through cmd escaping), delayed expansion (/v:off not forced).

### 04 Output
- 04-01 reusing a waiting writer after logging to another file uses that file's flush cursor, so A's replay is skipped:
  CONFIRMED (single global _lastLoggedPath/_nextUnloggedLine; ReuseWaitingWriterLocked switches path only). Needs a stalled
  writer plus a file switch. P3.
- 04-02 concurrent EnableLog calls exceed the one-running-one-waiting bound: CONFIRMED (checks before the unlocked open are
  not repeated after it). The TUI enables serially. P3.
- 04-03 replay permanently raises the writer's queue limit (_limit += replay, never reduced): CONFIRMED. P3.
- 04-04 forced line break splits a supplementary-plane continuation (ZWJ emoji part, second flag) when its high surrogate
  lands on the boundary: CONFIRMED (decision made per UTF-16 unit). P3.
- Pattern: the log writer state (global cursor + list searches) is the third round in a row with defects. B1 (per-destination
  state object) is the main structural recommendation for Output.
- Test issues: reset tests pass when the reset is a no-op; malformed-SGR tests assert little; CompletesWithin leaks threads;
  fixture deletes the temp dir before the writer closes. Valid.

### 05 Session
- 05-R3-01 "Force kill now" on a stale "Restart already pending" dialog kills the replacement run: CONFIRMED
  (ForceKillForPendingRestart kills whatever is current; MainController passes the list captured before the modal dialog).
  TUI-reachable, introduced by d34b2bc. P2. Fix: re-check under the session lock that each command is still waited for by a
  pending restart (and ideally target the waited-for run identity).
- 05-R3-02 cancellation between sequential registration and launch is ignored: CONFIRMED in code; API-only (TUI passes no
  token). P3.
- 05-R3-03 a command moved between parallel launches of one restart still launches in its new group: CONFIRMED in code; narrow
  window while editing during a restart. P3.
- 05-R3-04 SequenceRun.Result is a completed placeholder until Launch, so a concurrent restart can see the run as finished:
  CONFIRMED (SequenceRun.cs:50). Narrow window. P3. Fix: TaskCompletionSource from construction.
- Test issues: concurrency test has no overlap barrier; cleanup helper falls through with live processes; round-trip test
  misses Ready/Leftovers/Stdin. Valid.
- Open question: group restart when an original command moved out of the group's subtree while waiting.

### 06 TUI controller
- 06-R3-01 F5 dispatches Session.RestartAsync via Task.Run with no token; a Stop/quit that runs before the worker registers
  the operation can't revoke it: CONFIRMED in code (MainController.cs:778). Worker rated P1; the window is the thread-pool
  scheduling delay (milliseconds unless the pool is starved). P3, but the fix (register/reserve synchronously before
  dispatch) is cheap and fits the stale-modal pattern.
- 06-R3-02 single-line TextFields truncate a value with a newline (program, cwd, ready pattern, names, log path) when the
  dialog is accepted after an unrelated edit: CONFIRMED by probe. Same class as fixed 06-01/06-02 (args/env only). P3.
- 06-R3-03 activation recomputes its launch set after the input dialog, so a command that became startable while the dialog
  was open starts without its inputs ("no value for input"): CONFIRMED by probe. P3.
- Supplied 05-R3-01 confirmed again from the controller side.
- T1 MainControllerStatusTests passes with the production fix removed (mutation-tested): CONFIRMED, fix the test.
- T2 EnvTextTests:61 tautological assertion. Valid.
- Pattern: decisions taken before a modal dialog and applied after it (05-R3-01, 06-R3-03; 06-R3-01 is the async analogue).

### 07 TUI views (07r; the first run was killed accidentally, rerun with its leads)
- 07r-02 completing a selected stderr partial while stdout is still partial moves it one line back; the forward-only pin
  lookup loses the selection and the paused cursor then targets stdout: CONFIRMED by probe (PartialPins.cs:39). P3.
- 07r-01 copy validates the selection and then reads lines separately; a chunk arriving between them copies the other
  stream's text: CONFIRMED in code (OutputView.cs:742/747), narrow window. P3. Same root as round 1's snapshot concern.
- 07r-03 zero-width characters in the info pane advance Terminal.Gui's cursor but count 0 in cmdr's layout, shifting and
  clipping the row: CONFIRMED by driver-buffer probe. P3.
- 07r-04 POSIX copy leaves a program named `if`, `do` or `X=1` unquoted: CONFIRMED. P3.
- 07r-05 DetailsSelection.Extract on empty input throws; not reachable from the UI (guarded). P3 latent.
- Test issues: no backward-pin case; ColorMapTests "indexed background" uses RGB; harness factory cleanup. Valid.
