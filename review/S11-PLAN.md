# S11 plan: per-run owner in ManagedProcess, Windows group leases, helper deadline

The deferred structural step from the third review ([ROUND3-REPORT.md](ROUND3-REPORT.md), roadmap S11; the worker's
original text is in [round3/08-worker-result.md](round3/08-worker-result.md), section S11). No open finding depends on
it. It turns the run-id checks added by S1 and S10 into a structure where stale code can't reach a newer run at all.
The third part of S11 (LaunchSpec copies its collections in the constructor and in `with`) is done; see COMPLETED-FIXES.md.

Written 2026-11-15 against `review-fixes` at 602fc2a. Line numbers will drift; the field and method names are the anchor.

## Why it is worth doing (and why it waited)

Every per-run fact in `ManagedProcess` is a separate field under `_sync`, reset one by one in `StartLocked` and checked
one by one by callers that ran later:

| Field(s) | Set in | Read later by |
|---|---|---|
| `_runId`, `_outputRun` | `StartLocked` | `ForceKill(int)`, `OnExited`, `OnLeftoverTick` (timer state), `OnProcessLinesCompleted` |
| `_process`, `_group`, `_pipes` | `StartLocked`, `ReleaseRunResourcesLocked`, `TryDetachLeftoversLocked` (`_group = null`) | `RequestStop`, `ForceKillCore`, `OnExited`, leftover count/detach |
| `_stopMethod`, `_stopInput`, `_inputAfterInterrupt`, `_leftoverMode` | `StartLocked` (copied from the spec / prepared launch) | `RequestStop`, `TryDetachLeftoversLocked` |
| `_stopOrKillRequested`, `_forceKillRequested`, `_leftoverDetachPending` | `StartLocked` reset; `RequestStop`, `ForceKillCore`, `OnExited`, tick | `OnExited`, detach decision, tick |
| `_runExited`, `_readiness`, `_leftoverTimer` | `StartLocked`, `OnExited`, `SetLeftoverCountLocked` | `WaitForExitAsync`, readiness matching, tick |

The bugs of rounds 2 and 3 in this class (05-R3-01 stale confirmation killing the replacement run, 02/A3 old stop failure
in the new run's output) were each fixed by adding one more identity check. A new field or a new delayed path can forget
it. Deferred because it is M–L and medium–high risk, and the checks that exist hold.

## Part 1: one `Run` object per run (do in small, shippable steps)

Target shape: a private `sealed class Run` created by `StartLocked`, holding everything in the table above except the
snapshot. `ManagedProcess` keeps `_current: Run?` plus the immutable `_snapshot`. Delayed work (exit callback, pumps,
timer, out-of-lock signal/stdin dispatch) captures the `Run`, and "is this still current?" becomes
`ReferenceEquals(run, _current) && !_disposed` (plus the state check where one exists today).

Steps, each its own commit with both suites green:

1. **Introduce `Run` holding identity only:** `RunId`, `OutputRun`, `Process`, and the exit TCS. Replace
   `IsCurrentRunLocked(runId, process)` and the timer's `int` state with the `Run` reference. Keep `internal int RunId` for
   Session (it reads `_current.RunId`); `ForceKill(int)` keeps its contract.
2. **Move the spec-derived settings** (`StopMethod`, `StopInput`, `InputAfterInterrupt`, `LeftoverMode`) into `Run` as
   readonly members set in its constructor. `RequestStop` copies `run` out of the lock instead of five locals.
3. **Move the flags** (`StopOrKillRequested`, `ForceKillRequested`, `LeftoverDetachPending`) and `Readiness`. They stay
   guarded by `_sync`; document that on the class. No lock of its own.
4. **Move resources** (`Group`, `Pipes`, `LeftoverTimer`) and replace `ReleasedRun` with `Run.Release()` returning what to
   dispose outside the lock. Detach sets `run.Group = null` exactly as now. `_livePipes` stays on `ManagedProcess`
   (it outlives runs on purpose: detached output drains after the run is replaced).
5. **Optional, only if steps 1–4 left it awkward:** the failed-start path builds no `Run` (today it writes
   `FailedToStart` into the snapshot and returns); keep that.

Explicitly out of scope: moving `CountLiveProcesses`/`Detach` I/O out of `_sync` (do not combine with this; separate
decision), and a single lifecycle state machine/reducer (L/high, deferred by the report).

Must keep (existing tests cover most; check each still exists and passes after every step):
- detached-run output keeps draining after replacement; output for an old run is dropped (`Output.BeginRun` generation);
- no kill on Linux dispose; Windows job kill-on-close on dispose;
- an unknown leftover count blocks restart (`? left`, Q10);
- bounded exit drain (`OutputDrainTimeout`), cancellation of `WaitForExitAsync`;
- process/group disposal outside `_sync` (lock order: session → sequence → process; output notify → process);
- `ForceKill(int)` does nothing for a stale run id; late stop failures are written to their own run (S10, 02/A3).

New tests worth adding while doing it (gate-based, per practice 2 of the report): fault each acquisition/transfer step in
`StartLocked` (Prepare throws, Launch throws after leader start, exit callback fires before Launch returns) and assert
exactly what is disposed; rapid exit; dispose while a stop input write is blocked.

## Part 2: Windows group-operation leases and Ctrl+C helper deadline (needs native Windows)

All in `src/Cmdr.Core/Processes/WindowsProcessGroup.cs`. Only verifiable on Windows; land it when it can be tested there.

**Leases.** `RequestStop`/`ForceKill` call `Interrupt`/`Kill` outside `_sync` on a group reference they copied. Meanwhile
a new `Start` or `Dispose` disposes that group (`ReleasedRun.Dispose` → `_job.Dispose()`), and detach disposes it under
the lock. Today:
- `Kill`, `Detach`, `CountLiveProcesses` catch `ObjectDisposedException` from the closed job handle: safe but implicit.
- `Interrupt` does not touch the job; it starts the helper against `_leaderPid` even if the group was disposed, and the
  pid may in principle have been reused by then. It can take up to 5 s, all outside the lock.

Plan: a small lease (`TryAcquire()`/`Release()` counter plus a disposed flag under a private lock). Operations acquire;
`Dispose` marks disposed, waits for no new acquirers, and closes the handle when the count reaches zero (the last
releaser closes it if Dispose came first, so Dispose never blocks for 5 s). `Interrupt` returns without signalling when
it can't acquire. Keep kill-on-close semantics: a disposed group's processes still die when the handle closes.
Linux (`LinuxProcessGroup`) needs no lease (signals go to a pgid, not a handle), but check whether its `Interrupt` after
dispose can hit a reused pgid; if so, the same disposed flag suffices.

**Helper deadline and drain** (`Interrupt`, the `WaitForExit(HelperTimeoutMilliseconds)` block):
- one deadline (monotonic, `Stopwatch`) covering start, wait, stderr drain and cleanup, instead of 5 s + 1 s;
- after a timeout or kill, the stderr read task is still observed (await with the remaining budget, then discard), so it
  isn't an unobserved task; the error text is kept when it arrives in time;
- a failed `Process.Start` and a timeout produce distinct messages (already do; keep).
Tests: the helper start/wait can be made injectable (internal seam taking a `Func<ProcessStartInfo, Process>`) so the
timeout and the drain are tested on Linux with a stub process; the real helper remains a native Windows acceptance check.

**Windows launch plan variants** (`WindowsLaunchPlan` in `WindowsCommandResolver.cs`): make direct and batch launches
explicit variants with their own inputs (batch: cmd.exe path, `/v:off`, the refused CR/LF and UNC cwd cases from Q6/Q7),
so no OS checks leak into Session. Tighten native allocation transfer and error capture in `WindowsProcessPlatform` only
where these changes touch it.

Decisions that govern part 2 (already made, ROUND3-REPORT.md section on questions): Q6 (refuse UNC batch cwd), Q7
(`/v:off`, refuse CR/LF batch args), Q10 (Ctrl+C or stop input still being sent at detach is dropped; children with their
own console are a documented limitation).

## Suggested order

Part 1 steps 1–2 first if more process-lifecycle work is planned; they are cheap and Linux-testable. Part 2 when a
Windows test session is available, after the existing [WINDOWS-TESTING.md](WINDOWS-TESTING.md) checklist passes.
