# 05 — Session orchestration review

Completed independent review of baseline `a06d405e01095309617069e43c5d4be9200c0ebc` on 2026-11-13. **Six verified findings: one P1, four P2, one P3.** No production/test source changes or proposed patches. This report is this reviewer's only repository write; probe projects, generated output and test results were placed under `/tmp`.

## Findings at a glance

| ID | Severity | Finding | Boundary / reachability |
| --- | --- | --- | --- |
| 05-01 | P1 | Pending restart relaunches after ForceKillAll | **Session lifecycle + actual TUI F5/quit path** |
| 05-02 | P2 | Command-list actions accept another session's entries | **API-only ownership**; no ordinary TUI trigger found |
| 05-03 | P2 | Restart cancellation misses blocking stop dispatch and final launch | **Session/process contract**; token-specific defect is API-level |
| 05-04 | P2 | New command starts a competing run in a queued child group | Session run reservation; **actual TUI add-command/Enter path** |
| 05-05 | P2 | Background restart races tree edits and throws during enumeration | **Session + actual TUI threading/ownership** |
| 05-06 | P3 | Command-list sequence runs caller order and duplicates | **API-only ordering contract**; Selection already canonicalizes |

Cross-boundary labels identify consolidation overlaps. In particular, 05-01 and 05-05 concern session orchestration and its actual controller caller; 05-03 does not assert that ManagedProcess/native implementations violate their contracts. Native process implementations were not independently audited here.

## Scope, functionality and behavior checked

Read all twelve `src/Cmdr.Core/Session/*.cs` files and all eleven files in `tests/Cmdr.Core.Tests/Session/` fully: the session partials, SequenceRun, PlanNode, entries/settings/sources, Selection/targets/rows and StatusFormatter; editing, tree editing, tree execution, base-file, input, expanded-state, process, selection and status tests, including helpers. Also read the relevant process launch/state/control contracts, LaunchSpec and its tests, path resolution and its tests, input substitution, the output/log interface, config-chain loading/validation/save boundaries, and the readiness tests. Inspected MainController's dispatch/event/edit/restart paths and QuitFlow; read related UI tree, group choices/forms, collapse and read-only tests to establish actual callers and expected ownership. UI rendering and native/ManagedProcess internals remain the other reviewers' scope; references to them below establish the session's integration contract.

The session loads base sources first and the opened file last. Each group owns ordered mixed children, and each command owns one reusable ManagedProcess. Source metadata selects the correct buffer limit, config directory and log directory. Base items are read-only through the editing API. Config export deep-copies only the opened file's groups, and saving validates that file before atomic persistence through ConfigStore.

Group activation plans startable descendants, skipping already running commands and dropping empty groups. A sequential group owns one registered SequenceRun: command steps wait for leader exit; nested sequential groups are sub-runs; nested parallel groups wait for all successes and fail as soon as one branch fails. Parallel siblings deliberately keep running after another branch fails. A top-level parallel activation itself has no owning run. Kill-first Enter behavior, graceful Stop, force kill, restart and quit are distinct actions. Stopped exits do not count as success even with exit code zero; leader success can advance a sequence while descendants remain, consistent with the documented leader-based semantics.

**Readiness/dependencies:** this baseline has readiness display and matching, not a dependency graph or wait-until-ready scheduling API. SequenceRun waits for leader exit (`SequenceRun.cs:272`), and parallel branches do likewise (`Session.Processes.cs:686`); readiness does not satisfy a step. A focused control probe confirmed a Ready but running leader left its successor NotStarted/queued, and release/exit then ran the successor successfully. This agrees with README's sequences/readiness sections and is not reported as a missing-feature defect.

Tree editing checks ownership, base-file editability, cycles, active processes for removal, and run membership for protected moves/mode changes. Execution plans freeze membership/order; settings are read for each later launch, so edits apply on next start as documented. LaunchSpec copies arguments/environment/readiness and resolves cwd/env through the complete ancestor chain. Input values are copied for the action and shared down sub-runs; missing required inputs fail preparation without launching or enabling its configured log.

Selection is confined to direct sibling commands, returns marked targets in file order, and intentionally treats a group header as the full subtree. Prune removes moved/removed marks, and MainController calls it when rebuilding rows. Expanded is initial collapse configuration, not live collapse state. Status formatting correctly gives Running/Stopping precedence over queued state and preserves stopped-versus-failed and leftover warnings. No separate defect was found in these selection, formatting, source inheritance or serialization paths.

## Verified findings

### 05-01 — P1: Pending restart can relaunch commands after force-kill shutdown

**Cross-boundary: session lifecycle / UI action ownership.**

- **References:** `src/Cmdr.Core/Session/Session.Processes.cs:140-165`, `:196-207`, `:329-346`; caller `src/Cmdr/Tui/MainController.cs:769-787`; quit handling `src/Cmdr/Tui/QuitFlow.cs:48-54`, `:79-94`.
- **Trigger:** Start a long-running command that ignores SIGINT; begin F5/restart, leaving restart waiting for leader exit; invoke `ForceKillAll` (including Quit → Force kill).
- **Impact:** Shutdown kills the old process but wakes the pending restart, which launches a replacement. Quit can keep waiting on the new command; repeated shutdown operations race pending launches. This is separate from native process-tree termination: the replacement is a new session launch.
- **Evidence:** Linux probe against current source observed old PID `2938939`, then PID `2938942`, `State=Running`, and `ActiveEntries.Count=1` after `ForceKillAll` and restart completion. Restart tasks are not registered in `ActiveRuns`; cancellation of sequences does not invalidate the continuation at the final start. The UI explicitly runs restart as an untracked background task.
- **Fix direction:** Track restart operations and their launch authority. Stop/quit/disposal should invalidate pending launch authority before stopping processes. Check the operation/session generation immediately before starting, atomically with shutdown. Preserve intentional new actions after shutdown is cancelled without reviving the old operation.
- **Regression test:** Gate a command after installing its SIGINT handler, start restart, wait for Stopping, force-kill all, await restart settlement, and assert no new PID/start marker and no active command. Cover both restart overloads, StopAll plus eventual exit, and disposal.

### 05-02 — P2: Command-list actions do not enforce session ownership

**API-only ownership issue:** no ordinary TUI path passing another session's commands was found. **Cross-boundary for consolidation: public session API / lifecycle ownership.**

- **References:** `src/Cmdr.Core/Session/Session.Processes.cs:865-887` (`GroupOf`), called at `:18`, `:79`, `:112`, `:132`, `:228`, `:289`; `:310-323` (`ToggleLog`). Contrast `src/Cmdr.Core/Session/Session.cs:555-570`.
- **Trigger:** Pass entries from session B to a command-list action on session A. Targets need only share a group and not be removed; the receiving session is never checked. ToggleLog similarly accepts foreign or removed entries.
- **Impact:** A can start/stop/kill/log B's commands and register a sequence on B's group, while A's tree-based active-entry/run enumeration cannot see or stop that work. Event routing and resource ownership split between sessions. Foreign group overloads and editing correctly reject ownership violations, making the discrepancy surprising for embedders.
- **Evidence:** Probe: `A.Activate(B.Groups[0].Commands)` returned null, started B's first process and queued its second; `A.ActiveEntries.Count=0`, `B.ActiveEntries.Count=1`; `A.StopAll()` left the first Running and the second queued. B was explicitly cleaned up afterward.
- **Fix direction:** Make target validation instance-based and call `EnsureOwned` on every target before any mutation. Apply the same ownership/removal check to ToggleLog. Validate the complete collection before acting so mixed owned/foreign lists cannot partially execute.
- **Regression test:** For every command-list action and input-query overload, pass foreign, removed, and mixed targets; assert ArgumentException with no process, log, queue or event side effects. Include ToggleLog and verify the legitimate owner can still act.

### 05-03 — P2: Restart cancellation does not cover blocking stop dispatch or reliably prevent relaunch

**Cross-boundary: session orchestration / ManagedProcess stop contract.** The cancellation-token defect is API-level: the current TUI supplies the default token. Its pending restart is nevertheless relevant to 05-01. ManagedProcess/native implementation is outside this review; the finding concerns how session awaits a stop operation that may block.

- **References:** `src/Cmdr.Core/Session/Session.Processes.cs:142-165`, `:197-207`; stop contract/call implementation context `src/Cmdr.Core/Processes/ManagedProcess.cs:117-120`, `:154-165`.
- **Trigger:** Configure `stop.method=input` with input larger than the pipe buffer and a process that does not read stdin. Start RestartAsync, wait until stop dispatch blocks, then cancel its token.
- **Impact:** Restart remains pending while the write blocks. Once force kill releases the write, the cancelled operation can still launch a replacement because completed awaits do not establish a cancellation checkpoint. Even a token already cancelled can restart an idle command/group.
- **Evidence:** A 2 MiB stop input to `sleep 30` yielded `tokenCancelled=True`, `restartCompleted=False`, `State=Stopping` 300 ms after cancellation. Force-killing let the restart complete normally. A repeated probe explicitly observed a replacement Running with a different PID despite the cancelled token. A separate already-cancelled idle group restart returned null and launched PID `2938968` (already Exited by observation), rather than throwing cancellation.
- **Fix direction:** Apply cancellable waiting to stop-dispatch tasks (without pretending cancellation can abort the underlying pipe write), and explicitly check cancellation before side effects and immediately before launch. Observe outstanding dispatch task failures after the caller abandons the wait. Coordinate with 05-01's launch invalidation.
- **Regression test:** Block stop input with a nonreading child; cancel restart and require prompt OperationCanceledException without force kill; then clean up the child and assert no replacement launches. Cover already-cancelled idle/running targets and both overloads.

### 05-04 — P2: A newly added command can start an overlapping run in a queued child group

**Actual TUI trigger:** add a command to a child group while its outer sequence is running an earlier step, then Enter on the new command.

- **References:** `src/Cmdr.Core/Session/Session.Processes.cs:417-434`, `:544-552`, `:558-565`; `src/Cmdr.Core/Session/SequenceRun.cs:220-228`; tree edit permitted at `src/Cmdr.Core/Session/Session.cs:213-221`; actual add-command UI `src/Cmdr/Tui/MainController.cs:930-953` and group choices `src/Cmdr/Tui/CommandDialog.cs:77`.
- **Trigger:** Outer sequential group has an active first command and a pending sequential child group. Add a new command to the child; activate just that new command before the outer run reaches the child.
- **Impact:** The new child run starts despite the group already being a pending step of the outer run. When the outer sequence reaches it, TryStart fails, clears its planned queue and aborts the remaining outer steps. An independent action silently disrupts an accepted sequence plan.
- **Evidence:** Gated Linux probe returned no refusal for the extra command; after releasing the first step, `innerRunning=True`, original child command `NotStarted`, outer after-command `NotStarted`; original child's output: `A sequence is already running in group 'inner'.` The start guard checks only the child's own ActiveSequence and the submitted commands. The new command was absent from the outer plan, so it is not busy; the child group itself is reserved by that plan.
- **Fix direction:** Before accepting a new run, check whether its group is contained as a current/pending step of another active run, even when the submitted commands were added later. Keep start-refusal and input-preflight checks consistent. Do not broadly reject all parallel siblings, which intentionally run independently.
- **Regression test:** Gate an outer first step, add/start a command in its queued sequential child, require immediate refusal and no extra process; release the gate and require the original child and outer remainder to succeed. Also cover moving an idle command into the queued group.

### 05-05 — P2: Background restart traverses the editable tree without synchronization

**Cross-boundary: actual TUI threading / session tree ownership.** This is not an assumption that arbitrary callers may edit concurrently: MainController explicitly puts restart on a worker while normal editing remains available on the UI thread.

- **References:** `src/Cmdr.Core/Session/Session.cs:12-14`, `:108-120`, `:180-207`, `:505-529`; `src/Cmdr.Core/Session/Session.Processes.cs:140-142`, `:777-779`, `:812-829`; traversal `src/Cmdr.Core/Session/GroupEntry.cs:88-99`; caller `src/Cmdr/Tui/MainController.cs:769-787`, `:975-985`.
- **Trigger:** F5/restart runs in the worker and enumerates ActiveRuns/AllEntries/PlanChildren while the UI adds, removes or reparents an unrelated idle group. The session lock does not guard the edits or these full traversals. Declaring process actions UI-thread-only does not match the current caller.
- **Impact:** Restart can fail with `InvalidOperationException: Collection was modified; enumeration operation may not execute.` The UI catches the exception and displays Restart failed; the requested restart does not complete. The same mismatch also leaves check/register and check/remove sequences without one ownership protocol; this probe establishes enumeration failure, not every possible race outcome.
- **Evidence:** A bounded source-based probe used 300 idle groups, one restart worker (1,000 attempts) and one serial editing loop (1,220 add/remove pairs). 995 attempts failed with that exception from `List<T>.Enumerator.MoveNext`. Repeating the preserved probe below yielded 978 failures in 1,000 attempts and 1,496 edit pairs, with stack `GroupEntry.Walk:90 → ActiveRuns:779 → IsBusyCommand:849 → StartCommands:401 → RestartAsync:165`. Counts depend on scheduling. The command deliberately required an omitted input, so this probe started no subprocesses. The edit stream was single-threaded, matching UI ownership; concurrency came solely from the restart worker.
- **Fix direction:** Choose one threading model: marshal all tree inspection/planning/registration and edits onto the UI/session-owner thread, leaving only signal I/O and waits on workers; or consistently synchronize tree access and publish immutable plans/snapshots. Merely adding a lock around ActiveRuns is insufficient if edits do not use it. Avoid holding the tree lock while raising events, sending input or awaiting exit.
- **Regression test:** Coordinate a background restart's planning with an unrelated group edit and require no enumeration exception or partial registration. Exercise the actual controller dispatch model, plus removal/move while restart is waiting, and serialize final ownership revalidation before launch.

### 05-06 — P3: Command-list sequencing follows caller order and repeats duplicate targets

**API-only contract issue:** the current Selection supplies a unique list in file order, so no ordinary TUI trigger was found.

- **References:** promised ordering `src/Cmdr.Core/Session/Session.Processes.cs:9-11`; caller list preserved at `:36-42`, `:417-419`; validation `:865-887` does not normalize it. Restart similarly preserves targets at `:150-165`.
- **Trigger:** With sequential file order `[a, b]`, call `Activate([b, a, a])`.
- **Impact:** Commands run `b, a, a`, violating the documented file-order selection behavior and executing a command twice. This affects API consumers supplying an arbitrary list; it does not invalidate the Selection implementation.
- **Evidence:** Linux probe appended command names to a temporary order file and read exactly `b,a,a` after the sequence finished.
- **Fix direction:** Treat targets as a set of sibling commands and project through `group.Commands` to establish unique file order, or explicitly document caller order/repetition as the intended API instead. Apply one policy to Activate, Restart and preflight.
- **Regression test:** Pass a reversed subset and duplicated targets; assert unique file-order execution (or an explicit rejection of noncanonical input), and matching input request order.

## Strengths and maintainability/extensibility assessment

- The core remains independent of UI types. Mixed tree structure, plan nodes, per-command supervisors and source metadata make the basic ownership model easy to inspect. The editing API copies inputs and exported configuration instead of sharing caller-owned mutable collections.
- Run state has a documented lock order; run notifications occur after releasing its lock. Volatile publication of queue flags and active-run references is appropriate for display reads. StopIfCancelled explicitly handles a launch racing cancellation, and completion clears the owning group's registration conditionally rather than clearing another run.
- Error behavior is mostly deliberate and user-visible: malformed imports are distinguished from validation errors, base errors carry their source, removal/move refusals explain the reason, missing inputs remain process output, and nonzero/stopped branch failures suppress later sequence steps. Parallel sibling independence is tested and preserved.
- The main maintainability issue is that ownership is fragmented between run registration, mutable tree membership, asynchronous restart continuations and the controller. The findings above call for a shared operation lifetime and threading model. Refusal checks also exist in several places (actual start, preflight, root/sub-run registration), which helped produce 05-04; centralizing reservation checks would reduce drift.

The following are **risks/recommendations, not additional verified production defects**:

- Public model exposure weakens the otherwise careful editing boundary: `CommandEntry.Spec` exposes a mutable CommandSpec (`CommandEntry.cs:27-28`); tree lists are underlying Lists typed as IReadOnlyList (`GroupEntry.cs:55-62`, `Session.cs:79-80`), and Env is a mutable dictionary behind a read-only interface. Current UI editing uses the session methods, so no ordinary UI corruption was demonstrated. For future embedders/plugins, immutable views or explicit read-only wrappers and command-edit snapshots would better protect dirty tracking, base-file restrictions and parent invariants. Document direct model mutation as unsupported if retaining this design.
- ManagedProcess is concrete/sealed and constructed directly in AttachCommand (`Session.cs:485`), so orchestration tests rely heavily on real Linux children and timing. An internal supervisor/factory seam and a controlled scheduler would allow deterministic run/stop/exit/registration race tests. Keep the public API simple; this need not create a plugin system prematurely.
- Root SequenceRun results and top-level parallel branch tasks have no explicit central observer (`Session.Processes.cs:473`, `:605`). Expected process failures already become snapshots/false results, but a future unexpected exception or throwing observer can fault a background task without a user-visible orchestration error. Define exception containment and event-handler expectations before extending event-based integrations. No independent normal-input crash is claimed from this observation.
- Repeated recursive walks/AllCommands materializations and ActiveRuns scans make busy/refusal checks increasingly expensive on large trees (`GroupEntry.cs:65`, `Session.Processes.cs:778-857`). No user-facing performance failure was measured; consider indexed membership or one snapshot per action after correctness is fixed. Recursive depth was not stress-tested.
- Dispose cancels runs and disposes supervisors but does not join all asynchronous actions (`Session.cs:397-418`); this reinforces 05-01's operation-lifetime fix. Native cleanup behavior differs by platform according to the process contract. No additional claim that disposal itself must kill Linux children is made.

## Missing regression coverage

Existing tests strongly cover happy-path and nested sequencing, branch failure independence, kill-first activation, source-specific paths/buffers, base read-only edits, round-trip saving, marking/pruning and status boundaries. The principal gaps are operation transitions and integration ownership:

1. Pending restart versus StopAll, ForceKillAll, Dispose, another restart, or a later Stop; assert that the superseded operation has no remaining launch authority (05-01).
2. Foreign-session targets and ToggleLog, removed targets, mixed collections, and unique/file-order selection normalization (05-02/06). Current tests check mixed *groups*, not mixed *sessions*.
3. Cancellation before restart and during stop dispatch/exit waits, including a full stdin pipe and no later relaunch (05-03). Present restart tests pass no cancellation token.
4. Commands added/moved into a queued child group, while preserving the outer plan and valid independent parallel siblings (05-04).
5. Actual controller background restart combined with serial UI edits, including unrelated group edits and target revalidation after awaits (05-05). Existing editing/refusal tests act sequentially after waiting for process state.
6. Deterministic cancellation at registration/launch boundaries, queue cleanup for nested pending groups, and repeated operations after a partial parallel failure. Existing stop/failure tests cover stable running states more than these transitions.
7. A session-level readiness control test to pin the existing exit-based semantics; per-process readiness and formatter tests alone do not prove scheduling behavior. Also assert that leader-success-with-leftovers behavior is intentional in nested sequence tests.
8. Windows orchestration integration for stdin stop/restart, console stdin, stopped exit codes and job lifetime. Most real-process session tests simply return when not Linux; cross-platform static formatter tests do not replace these runs.

## Tests performed and limitations

- **Coordinator baseline:** user reports all **1,190 tests pass on Linux**. This review did not repeat the full suite.
- **Six selected existing tests:** **6 passed, 0 failed, 0 skipped**, using the already-built test assembly. Selection: `Sequence_WithSequentialAndParallelChildGroups_RunsStepsInOrder`, `ParallelChildFailure_FailsSequenceFast_AndSiblingKeepsRunning`, `RestartAsync_Group_CancelsRunningSequenceAndStartsItAgain`, `Launch_NestedCommand_InheritsGroupCwdEnv_AndDefaultLogPath`, `Prune_DropsMarksOfMovedAndRemovedCommands`, `Save_WritesOnlyOpenedFileGroups_KeepsBase_AndLeavesBaseFilesUntouched`.
- **Current-source probes:** compiled the unchanged core source into an external .NET 10 executable, with bin/obj excluded. Each of 05-01 through 05-06 reproduced; readiness control confirmed expected behavior. The final preserved harness was compiled and each mode was run successfully. 05-05 is a bounded concurrency reproducer, not a statistical failure-rate estimate.
- Host: Linux x64, Ubuntu 24.04, SDK 10.0.107 / runtime 10.0.7. Shell probes use sh/sleep and temporary directories. They force-kill and wait for their active children before disposal; no production program or network service was started.
- Windows behavior was assessed statically through the shared session code and process contracts only. No Windows execution or end-to-end terminal automation was available/performed. Thus the demonstrated TUI triggers are supported by caller inspection plus core-level reproductions, not a recorded terminal interaction. No native process implementation audit, full-suite rerun, benchmark, or exhaustive concurrency model check was performed. No web research was required.

Re-run the six existing tests without a build or repository output (requires the baseline test assembly):

```bash
dotnet vstest /srv/devshare/projects/cmdr/tests/Cmdr.Core.Tests/bin/Debug/net10.0/Cmdr.Core.Tests.dll \
  --TestCaseFilter:'FullyQualifiedName~Sequence_WithSequentialAndParallelChildGroups_RunsStepsInOrder|FullyQualifiedName~ParallelChildFailure_FailsSequenceFast_AndSiblingKeepsRunning|FullyQualifiedName~RestartAsync_Group_CancelsRunningSequenceAndStartsItAgain|FullyQualifiedName~Launch_NestedCommand_InheritsGroupCwdEnv_AndDefaultLogPath|FullyQualifiedName~Prune_DropsMarksOfMovedAndRemovedCommands|FullyQualifiedName~Save_WritesOnlyOpenedFileGroups_KeepsBase_AndLeavesBaseFilesUntouched' \
  --ResultsDirectory:/tmp/cmdr-review05-existing-test-results
```

## Preserved reproducible probes

The complete harness below is self-contained in the report; it does not depend on the original temporary project surviving. It compiles the current repository core source without writing its source tree. Run on Linux with .NET 10, sh and sleep. Gate files establish process states before actions; only the bounded cancellation observation uses a 300 ms delay. The concurrency case's failure count varies by scheduling; its exception stack establishes the shared tree enumeration failure.

Create the project, then save the following C# block as `$probe_dir/Program.cs`:

```bash
probe_dir=$(mktemp -d /tmp/cmdr-review05-repro.XXXXXX)
cat > "$probe_dir/probe.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="/srv/devshare/projects/cmdr/src/Cmdr.Core/**/*.cs"
             Exclude="/srv/devshare/projects/cmdr/src/Cmdr.Core/obj/**/*.cs;/srv/devshare/projects/cmdr/src/Cmdr.Core/bin/**/*.cs" />
    <Compile Include="Program.cs" />
  </ItemGroup>
</Project>
XML
```

```csharp
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using S = Cmdr.Core.Session.Session;

var root = Directory.CreateTempSubdirectory("review05-probe-").FullName;
var sessions = new List<S>();
async Task Until(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    while (!condition()) await Task.Delay(10, timeout.Token);
}
CommandSpec Sh(string name, string script) =>
    new() { Name = name, Program = "sh", Args = new() { "-c", script } };
GroupSpec G(string name, GroupMode mode, params ItemSpec[] items) =>
    new() { Name = name, Mode = mode, Items = items.ToList() };
S Open(params GroupSpec[] groups)
{
    var dir = Path.Combine(root, sessions.Count.ToString());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "cmdr.json");
    ConfigStore.Save(new() { Groups = groups.ToList() }, path);
    var session = S.Load(path);
    sessions.Add(session);
    return session;
}
string Text(CommandEntry entry) => string.Join("\n",
    entry.Process.Output.Buffer.GetLines(0, int.MaxValue).Select(x => x.PlainText));
try
{
    switch (args.Single())
    {
        case "05-01":
        {
            var s = Open(G("g", GroupMode.Parallel,
                Sh("loop", "trap '' INT; echo READY; while :; do sleep 0.05; done")));
            var e = s.AllEntries[0];
            s.Activate(new[] { e });
            await Until(() => Text(e).Contains("READY"));
            var oldPid = e.Process.Snapshot.Pid;
            var restart = s.RestartAsync(new[] { e });
            await Until(() => e.Process.Snapshot.State == ProcessState.Stopping);
            s.ForceKillAll();
            await restart.WaitAsync(TimeSpan.FromSeconds(8));
            Console.WriteLine($"old={oldPid}, new={e.Process.Snapshot.Pid}, state={e.Process.Snapshot.State}");
            break;
        }
        case "05-02":
        {
            var a = Open();
            var b = Open(G("g", GroupMode.Sequential,
                new CommandSpec { Name = "one", Program = "sleep", Args = new() { "30" } },
                Sh("two", "echo two")));
            var g = b.Groups[0];
            Console.WriteLine("refusal=" + (a.Activate(g.Commands.ToArray()) ?? "null"));
            await Until(() => g.Commands[0].IsActive);
            a.StopAll();
            Console.WriteLine($"A active={a.ActiveEntries.Count}, B active={b.ActiveEntries.Count}, next queued={g.Commands[1].IsQueued}");
            break;
        }
        case "05-03":
        {
            var s = Open(G("g", GroupMode.Parallel, new CommandSpec
            {
                Name = "no-read", Program = "sleep", Args = new() { "30" },
                Stop = new() { Method = StopMethod.Input, Input = new string('x', 2 * 1024 * 1024) }
            }));
            var e = s.AllEntries[0];
            s.Activate(new[] { e });
            var oldPid = e.Process.Snapshot.Pid;
            using var ct = new CancellationTokenSource();
            var restart = s.RestartAsync(new[] { e }, ct: ct.Token);
            await Until(() => e.Process.Snapshot.State == ProcessState.Stopping);
            ct.Cancel();
            await Task.Delay(300);
            Console.WriteLine($"cancelled={ct.IsCancellationRequested}, completed={restart.IsCompleted}");
            s.ForceKillAll();
            try { await restart.WaitAsync(TimeSpan.FromSeconds(8)); Console.WriteLine("restart returned normally"); }
            catch (OperationCanceledException) { Console.WriteLine("restart observed cancellation"); }
            Console.WriteLine($"old={oldPid}, new={e.Process.Snapshot.Pid}, state={e.Process.Snapshot.State}");
            var idle = Open(G("g", GroupMode.Parallel, Sh("idle", "echo ran")));
            try { await idle.RestartAsync(idle.Groups[0], ct: ct.Token); }
            catch (OperationCanceledException) { Console.WriteLine("idle restart observed cancellation"); }
            Console.WriteLine($"pre-cancelled idle restart PID={idle.AllEntries[0].Process.Snapshot.Pid}");
            break;
        }
        case "05-04":
        {
            var s = Open(G("outer", GroupMode.Sequential,
                Sh("gate", "while [ ! -f release ]; do sleep 0.02; done"),
                G("inner", GroupMode.Sequential, Sh("planned", "echo planned")),
                Sh("after", "echo after")));
            var outer = s.Groups[0];
            var inner = outer.Groups[0];
            s.Activate(outer);
            await Until(() => outer.Commands[0].IsActive);
            var extra = s.AddCommand(inner,
                new() { Name = "extra", Program = "sleep", Args = new() { "30" } });
            Console.WriteLine("refusal=" + (s.Activate(new[] { extra }) ?? "null"));
            await Until(() => extra.IsActive);
            File.WriteAllText(Path.Combine(s.ConfigDir, "release"), "");
            await Until(() => !outer.IsSequenceRunning);
            Console.WriteLine($"inner running={inner.IsSequenceRunning}, planned={inner.Commands[0].Process.Snapshot.State}, after={outer.Commands[1].Process.Snapshot.State}");
            Console.WriteLine(Text(inner.Commands[0]));
            break;
        }
        case "05-05":
        {
            var s = Open(Enumerable.Range(0, 300)
                .Select(i => G("g" + i, GroupMode.Parallel)).ToArray());
            var e = s.AddCommand(s.Groups[0], Sh("missing", "echo ${input:x}"));
            int failures = 0, edits = 0;
            string? first = null;
            var restarter = Task.Run(async () =>
            {
                for (int i = 0; i < 1000; i++)
                    try { await s.RestartAsync(new[] { e }); }
                    catch (Exception ex) { failures++; first ??= ex.ToString(); }
            });
            // Only this loop edits; only the worker invokes restart. No subprocess can start.
            while (!restarter.IsCompleted)
            {
                var g = s.AddGroup(null, new("temporary", null, GroupMode.Parallel, null, null));
                s.RemoveGroup(g);
                if (++edits % 100 == 0) await Task.Yield();
            }
            await restarter;
            Console.WriteLine($"attempts=1000, edit pairs={edits}, failures={failures}\n{first}");
            break;
        }
        case "05-06":
        {
            var s = Open(G("g", GroupMode.Sequential,
                Sh("a", "echo a >> order"), Sh("b", "echo b >> order")));
            var g = s.Groups[0];
            s.Activate(new[] { g.Commands[1], g.Commands[0], g.Commands[0] });
            await Until(() => !g.IsSequenceRunning);
            Console.WriteLine(string.Join(",", File.ReadAllLines(Path.Combine(s.ConfigDir, "order"))));
            break;
        }
        case "readiness-control":
        {
            var ready = Sh("ready", "echo READY; while [ ! -f release ]; do sleep 0.02; done");
            ready.Ready = new() { Pattern = "READY" };
            var s = Open(G("g", GroupMode.Sequential, ready, Sh("after", "echo after")));
            var g = s.Groups[0];
            s.Activate(g);
            await Until(() => g.Commands[0].Process.Snapshot.Readiness == ProcessReadiness.Ready);
            Console.WriteLine($"ready leader running; after={g.Commands[1].Process.Snapshot.State}, queued={g.Commands[1].IsQueued}");
            File.WriteAllText(Path.Combine(s.ConfigDir, "release"), "");
            await Until(() => !g.IsSequenceRunning);
            Console.WriteLine($"after exit code={g.Commands[1].Process.Snapshot.ExitCode}");
            break;
        }
        default: throw new ArgumentException("Unknown probe ID");
    }
}
finally
{
    foreach (var s in sessions) s.ForceKillAll();
    foreach (var s in sessions)
    {
        await Until(() => s.ActiveEntries.Count == 0);
        s.Dispose();
    }
    Directory.Delete(root, recursive: true);
}
```

Build once and run individual cases, or use the bounded loop below:

```bash
dotnet build "$probe_dir/probe.csproj" -c Release --nologo
for probe_id in 05-01 05-02 05-03 05-04 05-05 05-06 readiness-control; do
  dotnet "$probe_dir/bin/Release/net10.0/probe.dll" "$probe_id"
done
```

Expected observations on the reviewed baseline:

| Mode | Observed result |
| --- | --- |
| 05-01 | Different old/new PIDs; replacement Running after ForceKillAll |
| 05-02 | Null refusal; A active=0, B active=1, next queued=True after A.StopAll |
| 05-03 | Cancelled=True but Completed=False; after force kill, restart returns normally and replacement is Running; already-cancelled idle restart also has a PID |
| 05-04 | Null refusal; inner run remains active while original planned command and outer successor remain NotStarted; refusal is logged only when the outer reaches its child |
| 05-05 | Collection-modified exceptions; repeat run: 978/1,000 failures, stack through GroupEntry.Walk:90 / ActiveRuns:779 / StartCommands:401 / RestartAsync:165 |
| 05-06 | Order file is `b,a,a` |
| readiness-control | Ready leader leaves successor NotStarted and queued; releasing the leader then yields successor exit code 0 |
