# Worker 08 handoff — cross-cutting synthesis and incremental roadmap

Reviewed repository `/srv/devshare/projects/cmdr`, supplied branch `review-fixes`, HEAD verified with `git log`: `a27a7ce` (`docs: record commit for root-created files`). This is a read-only review and synthesis of the existing proposals, not a new design phase. No implementation was authorized by the worker request, and none was performed.

The architecture is usable: Core is independent of Terminal.Gui; platform launch is behind interfaces; process snapshots and published parser lines already support immutable observations; Session has restart ownership and a tree lock. The repeated defects arise where a guarantee stops at a component boundary. The useful change is to finish those boundaries, rather than replace the architecture.

There are **29 distinct open round-3 production/helper findings: 5 P2 and 24 P3**, using the coordinator's verdicts, not the workers' original ratings. Two P2 findings remain plausible Windows-only cases. Test-quality issues and preventive recommendations are separate and do not increase that count. Round-1 and round-2 findings below are historical evidence, mostly already fixed; they are not reopened.

## 1. Recurring bug classes

Notation: **R1** refers to `review/REPORT.md` and the six area reports; **R2** to `review/COMPLETED-FIXES.md`, section “Review of the fixes”; **R3** to the seven current handoffs, with verdicts in `00-coordinator-plan.md`. R2 reuses short IDs such as R1/R2/R3 across areas; the area/commit is included below to disambiguate. That document aggregates `R03-01..05` rather than preserving five separate descriptions; this report does not invent a finer correspondence. Some findings have more than one cause.

### C1. Authority is checked before a delay, rather than carried through the effect

**Evidence:** R1 `05-01`, `05-03`, `05-04`, `05-05`; R2 `R03-01..05` (restart revocation, moved/removed targets, original group membership; `9b3ae25`), force-kill versus pending detach (`c0799b3`); R3 `05-R3-01..04`, `06-R3-01`, `06-R3-03`, `04-02`, and the unused-helper `02/A1`.

**Structures:**

- `MainController.RestartTargets` queues a lambda before `Session.RestartAsync` registers its operation. Stop/Quit can see no operation to revoke. `Activate` calls `InputsForActivate`, runs a nested modal loop, then independently asks Session to calculate the action again.
- `Session.Processes.StartCommands` uses the full `ThrowIfRevokedLocked` predicate once, then only `IsRevoked` between starts. `StartEntry` subsequently reads the entry's current ancestry. Membership authorization and actual launch are different transactions.
- `StartSequence`/`StartGroup` unregister the restart when registering a `SequenceRun`; the sequence has its own token, with no caller-token handoff. `Register` publishes a run whose `Result` is still a completed placeholder until `Launch` replaces it.
- `CommandOutput.EnableLog` validates writer constraints, releases its lock to open, then commits without repeating all constraints. This is the same reserve/perform/revalidate problem as process launch, despite a different resource.

**Conclusion:** A lock around a check is insufficient if the effect occurs later. Each delayed effect needs an owner/reservation, a precise validity predicate, and reconciliation if revocation arrives during the effect. A modal confirmation is also a delay. Keep I/O and callbacks outside the locks; enlarging lock scopes across dialogs or process waits would recreate earlier failures.

### C2. Identity, progress, and cleanup belong to a lifetime, not to the reusable container

**Evidence:** R1 `02-02`, `02-03`, `02-04`, `02-05`, `03-01`, `04-08`, `04-09`; R2 Process.Dispose deadlock (`9b3ae25`), `04-R2` stdin cleanup, `F06-01` pin identity (`0608a8e`), `C1` duplicate Windows exit handle (`d0ccf63`), `04-R1`/`04-R3` waiting-writer replay/bounds (`b08f88f`), dedicated log threads (`5a7eb36`); R3 `02/A3`, `04-01..03`, `05-R3-01`, `07r-01..02`, `W03-04`. The unsafe Windows cleanup is a test defect, but demonstrates the same ownership error.

**Structures:**

- `ManagedProcess` has `_runId`, `_outputRun`, process/group, readiness, exit completion, timers and stop flags as separate fields. `RunPipes` owns only part of one run. Pumps and exit callbacks carry identity; `SignalGroup` and `WriteStopInput` publish failures to whichever output run is now current.
- `PendingRestartsOf` returns reusable `CommandEntry` identities. `ForceKillForPendingRestart` acts on their current process without checking the original operation or process generation.
- `CommandOutput` keeps a global `_lastLoggedPath`/`_nextUnloggedLine` while multiple destinations' writers can still exist. Its mutable writer list encodes ownership and sequencing indirectly. `LogWriter.PrependLocked` adds replay to a permanent queue limit instead of tracking live versus replay work.
- `OutputBuffer` locks individual properties and range reads. `OutputView.Geometry`, `ReadLines`, `Revalidate`, and copy combine different observations. Stable line identity already exists, but `PartialPins.Find` assumes identity can only move forward; stderr promotion can move backward.
- The Windows probe treats process name and recent start time as ownership. That cannot authorize killing a process outside the fixture.

**Conclusion:** Retain the useful existing identities, and carry them through all delayed publications and conditional control actions. Give destination progress, operation completion and resource cleanup one clear owner each. A coherent read is a separate need: stable IDs do not make multiple reads one snapshot.

### C3. Representation boundaries lose information or confuse units/roles

**Evidence:** R1 `01-03`, `01-06`, `01-08`, `01-09`, `04-01..08`, `06-01..03`, `06-05`; R2 `F06-01` identity, `F06-02` trailing grapheme, `R1 (06-03)` dialog sizing (`5a7eb36`), `R3 (06-05)` PowerShell program position (`d0ccf63`), `R1 (06-05)` batch copy (`345138f`); R3 `06-R3-02`, `04-04`, `07r-02..05`, `W03-01..02`, `W03-05`.

**Structures:**

- `KeptArgs`/`KeptEnv` repair two editor representations; other strings still pass through single-line `TextField` setters and form trimming. Pure string codecs cannot prove what the actual control preserves.
- `AnsiLineParser.Process(char)` and forced-break logic make a grapheme decision before a supplementary scalar is complete. The earlier surrogate fix and later trailing-grapheme fix repaired adjacent, different boundaries.
- `DetailsView.DrawRow` writes string pieces via `AddStr` while `TextWidth`/selection use measured columns. The output pane already has explicit grapheme cells and a zero-width draw policy. Measuring correctly does not prove the terminal driver's cursor advances identically.
- `CommandLineFormatter` distinguishes program position only for PowerShell. Native argv, cmd source, and PowerShell source are still related through ordinary strings/booleans; `--%` is binder syntax even when it looks quoted. The batch bridge owns no preservation scope for `$env:CMDR_CMD`.
- JSON DTO normalization, strict enums, and source-coordinate translation are separate conversions. Their historical issues do not imply that every current DTO must be replaced.

**Conclusion:** State the round-trip contract at each boundary, including the real UI control or shell. Distinguish storage units, complete scalars, graphemes, columns, logical identities, program tokens, and argument tokens. Reuse small representations where rules actually match; do not build a terminal emulator or one universal quoting function.

### C4. Filesystem names, file identity, and commit outcomes are conflated

**Evidence:** R1 `01-01`, `01-02`, `01-04`, `01-05`, `01-07`, `03-02..04`; R2 symlink-plus-dotdot save (`05 symlink save`, `c0799b3`), in-place fallback/log alias changes (`c0799b3`), forward-slash UNC (`R2 (03-04)`, `d0ccf63`), drive-relative/ComSpec decisions (`ce2c06d`, `f6dcc28`), save/root ownership (`1aebfbe`, `dff75f3`); R3 config `R01..04` and `W03-03`.

**Structures:**

- `PhysicalPath.Resolve` is shared by prospective log comparison and save-target resolution. Its `exists` flag does not prevent a later `..` from erasing an untraversable prefix; `Path.Exists` also does not prove a traversed component is a directory.
- `AtomicFile.Write` separates path resolution, existence/ownership inspection, temp writing and replacement. `TryReplaceFile` reports a bool for fallback and always deletes the temp in `finally`; a native partial replacement is not equivalent to “destination unchanged.” Setting final mode before the last write fails a postcondition even though the individual chmod succeeded.
- Canonical strings detect many aliases but not hard links. Conversely, existing-file identity cannot identify prospective nonexistent destinations.
- Windows executable resolution already has a pure injected-file predicate and explicit drive-relative policy. Batch launch adds a second interpreter whose cwd rules are not represented in its plan.

**Conclusion:** Use named existing/prospective resolution contracts, file identity only where a file exists, and classified commit outcomes with explicit artifact ownership. This is not evidence that every filesystem issue is a TOCTOU exploit. In particular, `R02` reproduces without concurrency. The File.Exists/OpenOrCreate root-ownership race is a preventive concern from worker 04, not a 30th confirmed finding. Native handle-relative race-proof saving across hostile concurrent directory changes would be a larger, separately justified requirement.

### C5. Rules and failure semantics are duplicated or implicit at boundaries

**Evidence:** R1 `01-03`, `01-05/06-04` (one finding), `01-08..10`, `02-01`, `03-03`, `05-02`, `05-04`, `05-06`, `06-06`; R2 restart boundary checks, leftover/detach revisions, conhost accounting and ComSpec policy; R3 `02/A1..02/A2`, `05-R3-02..04`, `06-R3-03`, `07r-05`, config `R05`, `W03-03`.

**Structures:** preflight and execution build separate target sets; hints approximate the action using yet another set of booleans. Session and ManagedProcess each expose restart implementations with different cancellation strength. A completed `Task.FromResult(false)` means “not launched yet,” an elapsed group-establishment deadline means success, and an empty extraction clamps to coordinate zero then indexes it. CLI input validation is outside the config-error boundary.

**Conclusion:** Prefer one policy/result per domain, with explicit not-yet-started, gone, unavailable, refused and completed states. Share the action planner across preflight and execution before adapting hints. Do not add a repository-wide result framework or catch every exception as an operational failure. Subscriber exception handling and restricted `/proc` policy remain explicit questions, not invented fixes.

### C6. Work is bounded at the wrong stage

**Evidence:** R1 `02-05`, `04-10` and measured recommendation `04-11`; R2 dedicated log threads and `F06-03` single-scalar width caching (`5a7eb36`), waiting-writer limits (`b08f88f`); R3 `04-02..03`. Large-copy preallocation and byte budgets remain recommendations, not newly measured failures.

**Structures:** publishing a growing partial formerly copied the prefix per chunk; counting rows formerly materialized drawing geometry. Those are now substantially repaired with copy-on-write lines and streaming row counting. Logging now isolates synchronous filesystem I/O on dedicated threads, but replay accounting permanently enlarges the live allowance and concurrent opens defeat the writer bound. Copy still materializes text before OSC52's size check.

**Conclusion:** Specify what is bounded (live queue, replay, in-flight batch, open reservations, allocation), and test after each transition, not only at construction. Preserve current streaming/immutable-line optimizations when adding snapshots. Do not schedule stalled synchronous sink I/O back onto the shared pool.

### C7. Tests establish agreement or eventual completion, but not the claimed branch or invariant

**Evidence across rounds:** R1's baseline passes coexisted with all 40 findings and guarded Windows tests; R2 found a leaked INT-ignoring process, blocking pool writers and flaky tests while repairing lifecycle/output; R3 all seven areas report concrete gaps. Particularly strong examples: the mutation-tested controller status regression survives removal of its fix; root in-place save tests never assert identity; the blocked-input case may not block; SessionConcurrencyTests has no overlap gate; reset tests start from default style; the pin suite tests only forward promotion; the color test labelled indexed supplies RGB.

**Structures:** sleeps and worker-loop timing stand in for transition gates; generic Cleanup helpers poll the supervisor being tested then fall through; `CompletesWithin` leaves a timed-out background thread and does not capture exceptions; OutputViewHarness duplicates extraction instead of exercising copy; early returns count unsupported tests as passes. Fixtures can allocate resources before returning an IDisposable owner and leak on construction failure.

**Conclusion:** These weaknesses explain why successive repairs pass tests but miss nearby states. Add independent oracles and branch evidence, deterministic gates, owned fixtures and visible skips. A general increase in test count is not the remedy.

## 2. Merged proposals and feasibility against current code

| Merged change | Area proposals served | Code-based judgment, cost and limits |
|---|---|---|
| **Owned action intent from UI preparation through Session reservation and execution** | 05 proposals 1–4; 06 proposals 1, 2, 4; 02 B1 | Feasible by extending `RestartOperation`, `PlanNode`, registration and `StartEntry`; MainController should gather data and submit an intent. Existing `_sync` and post-start cleanup are useful foundations. The sequence completion fix is S; a shared authority handoff is M; an immutable action plan plus hint adoption is M–L. A controller token alone does not close membership or sequence-token gaps. A Session-only registry created in the queued worker does not close the controller dispatch gap. Use a synchronous prepare/reserve entry point with background execution rather than two competing registries. |
| **Run-scoped control and publication** | 02 B2/B4/B6; 05 proposal 3; 03 B4/B5 | `_runId`, `_outputRun`, `RunPipes`, `ReleasedRun` and run-tagged readiness already exist. Add conditional run control and run-qualified system output first (S–M); grouping the remaining fields into a complete owner is M–L with medium/high lifecycle risk. Group-use leasing/helper identity is a later separate change. It must preserve detached old-pipe draining and out-of-lock disposal. Moving every platform call out of `StartLocked` at once is L/high risk and unnecessary for the current diagnostic defect. |
| **Per-destination logging owner and explicit queue accounting** | 04 B1/B2; 01's path/identity distinction at validation boundary | A destination object can replace the global cursor pair and repeated writer-list searches while retaining the existing dedicated I/O worker. M/medium. Keep active destination separate from running/waiting writers; open results require an identity and revalidation. Separate replay/live counters rather than altering `_limit`. Decide state retirement/revisit replay policy before retaining history for every path. Validation's hard-link identity and runtime writer scheduling are related but should not be one global log service. |
| **Atomic output publication and coherent operation reads** | 04 B4/B6; 07r proposals 1, 2, 6 | `OutputLine` already supplies stable IDs and copy-on-write snapshots; `OutputBuffer` already copies requested reference ranges. Add metadata-and-range capture plus atomic append/partial replacement. Then copy/revalidation must resolve pins and extract from that same captured state. M for the copy path, M–L for all view geometry paths. A metadata snapshot followed by live range reads is insufficient. Do not copy the whole ring per frame or hold its lock during layout/drawing/clipboard work. |
| **Field draft/preservation and semantic text boundaries** | 06 proposal 3; 01 proposal 4 at adapters; 04 B3/B6; 07r proposal 3 | These share a principle, not one implementation. Editors need original/kept/edited state through actual controls (M); parser needs complete-scalar intake before grapheme decisions (M); details need drawable grapheme geometry consistent with offsets and zero-width policy (S–M locally, M if shared). No need to convert JSON DTOs to immutable objects to fix these. |
| **Explicit filesystem stages and platform plans** | 01 proposals 1–3; 03 B2–B4; 02 B3 | Local strict/prospective resolution methods and an AtomicFile replacement outcome seam are feasible (M combined). Metadata finalization is a small ordered fix. Existing Windows resolver is already mostly a pure policy; extend it rather than add OS branches to Session/ManagedProcess. Linux establishment needs explicit timeout and cleanup coordination. Do not merge filesystem names and shell source into one path abstraction. |
| **Owned, deterministic test fixtures with independent oracles** | All seven areas' fixture proposals | Shared ownership rules are valuable; one giant cross-platform fixture is not. Extend ProcessTestHelpers/SessionTestHelpers with scoped process ownership; keep Windows child-report handles, output sink gates and Terminal.Gui layout/clock fixtures as domain adapters. S per local repair, M for migrations. Production seams should remain internal and minimal, not become a public scheduler/plugin system. |

### Conflicts and proposals to reject or defer

- **Snapshot versus identity:** both are needed. Backward pin search repairs `07r-02`; it cannot repair `07r-01`. An operation ID cannot make a stale confirmation safe unless the process control itself is conditional on the waited-for run. Do not present a recheck followed by unconditional `ForceKill()` as closing that race.
- **Reservation versus frozen launch:** `PlanNode` freezes membership structure but retains mutable entries; `StartEntry` deliberately resolves settings later. Freeze the authorized launch request at the actual launch boundary first. Freezing settings for an entire long-running sequence would change edit semantics and is not assumed. A coarse global tree version would unnecessarily cancel unrelated parallel work; use target/membership validity or scoped revisions.
- **Two restart authorities:** worker 02 suggests factoring common restart logic; the coordinator recommends deleting the unused public ManagedProcess helper. Prefer deletion if its API is not supported externally. If it is supported, repair its dispatch cancellation locally. Do not introduce a common stop/wait framework solely to preserve an unused helper.
- **Run-owner refactor versus immediate repairs:** the broad owner helps several areas but costs more than `02/A3` alone. Ship run-qualified diagnostics and conditional control without waiting for the full extraction. Retain platform-specific handles behind existing interfaces.
- **Pure scheduler versus sink threads:** extract only enough queue transitions to test bounds/acknowledgements; retain dedicated synchronous writer threads. An actor runtime or wholesale async logger is unjustified.
- **Immutable config rewrite / private runtime tree:** defer the M–L DTO/domain split and removal of all public live entries. Current ordinary UI editing is owner-thread based, and no concurrent-save defect was established. Defensive launch copies and scoped snapshot APIs address demonstrated boundaries at much lower migration cost.
- **Universal text layout:** share grapheme/offset/draw policy, not output terminal wrapping and details word wrapping. Keep distinct wrap policies, selection spaces, first-unit styling and documented long-grapheme exceptions.
- **Full filesystem abstraction / race-proof persistence:** use a small replacement fault seam and named resolver operations. A virtual filesystem covering all operations, adversarial directory-race protection and broad metadata policy would exceed the demonstrated fixes. Do not claim pathname rechecks eliminate concurrent mutation races.
- **Optimizations without current evidence:** defer a global byte budget, cross-frame geometry cache and general copy pipeline rewrite. A bounded extraction helper may be worthwhile later; it is not a new open defect. Any cache must include content revision and width, not only stable partial-line identity.

## 3. Ranked incremental roadmap

Costs include implementation and focused regressions: **S** localized; **M** coordinated files/contract; **L** broad migration. Rank is repair priority, not a requirement to serialize independent patches. Every substep can ship alone unless an explicit dependency is stated. “Closes” below means the stated invariant under the tested API, not proof against all future concurrency bugs.

### S0 — Fix unsafe test ownership before native Windows verification (fix now)

- **Prevents/fixes:** C2/C7; `W03-04` P2. Also strengthens cleanup evidence for all lifecycle fixes.
- **Files/shape:** `WindowsJobContainmentTests.cs`, a tiny owned child fixture/PID-report helper, `ProcessTestHelpers.cs`, `SessionTestHelpers.cs`, output test fixtures. First remove name/time-based cleanup authorization. A fixture must report its own child identity independently of job membership, retain its handle, and kill/reap only owned children. Repair each other helper as its affected test is touched, not in a repository-wide migration.
- **Dependencies:** none. This is a hard prerequisite for the native containment checklist, including tests intended to detect escaped children.
- **Cost/risk:** S to remove unsafe cleanup; M including independently reported escaping-child fixture. Low production risk, medium test integration risk.
- **Verification:** on Windows, a separately owned sentinel PING started in the same interval must survive; the fixture child must be independently observed dead after kill/dispose, including injected assertion failure. Bound and observe stream drains, exit and teardown. Linux cannot establish this result.

### S1 — Make pending-restart escalation conditional on operation and run (fix now)

- **Prevents/fixes:** C1/C2; `05-R3-01` P2 (also reported by worker 06, counted once).
- **Files/shape:** `Session.Processes.cs`, `RestartOperation.cs`, `ManagedProcess.cs`, `MainController.cs`; restart boundary and controller tests. Return an opaque pending-target handle containing operation and waited-for run identity. Validate pending authority under Session `_sync`; control the process through a conditional expected-run operation under the process lock. Capture the original group for signal dispatch outside the locks. A completed/superseded handle becomes a harmless stale result. Never redirect it to the replacement.
- **Dependencies:** none on the broad run owner. A small stable process-generation control seam is required. Preserve session → process lock order and do not hold Session lock during OS I/O.
- **Cost/risk:** M; medium. A simple Session recheck alone is S but does not close the check/use gap and is not the recommended completed fix.
- **Verification:** gate modal confirmation; let original restart finish and replacement run start, then confirm. Replacement must remain alive. Also cover original still pending, a later restart of the same entry, leftover-only target, removed target, and completion racing the conditional call. Retain existing “force-kill pending, then relaunch” success test.

### S2 — Repair save traversal and failure cleanup (fix now; two independent patches)

- **Prevents/fixes:** C4/C5; config `R02` P2 and plausible Windows `R01` P2; config `R04` P3 fits a third small patch.
- **Files/shape:** `PhysicalPath.cs`, `AtomicFile.cs`, `UnixOwnership.cs` only as needed, config/recent/ownership tests. (a) Add strict existing-target traversal separate from prospective log comparison; never erase missing/non-directory traversal failures with `..`. (b) Give replacement a classified outcome and temp/recovery-artifact ownership; cleanup must preserve a potentially surviving replacement and surface its path. Do not fall back to in-place overwrite after an unknown/partial commit. (c) Keep safe initial access bits, then restore required final mode after the final write/length change and before durability flush/rename; test restoration failure for non-owned in-place files.
- **Dependencies:** none on S1. Narrow fault seam needed for (b); existing non-root/capability-aware test support for (c). Power-loss durability and broader metadata promises are not expanded.
- **Cost/risk:** (a) M/medium (Windows links); (b) M/medium (error handling); (c) S–M/medium (permission ordering).
- **Verification:** missing/../target and regular-file/../target links throw and preserve bytes/link/no created artifacts; retain directory-symlink-plus-dotdot success, dangling/loop tests. Inject documented native replacement states for 1175/1176/1177, asserting surviving paths and cleanup, then verify on Windows. Non-root setuid and executable-setgid cases assert final mode and independent identity for replacement versus in-place branches; root passes alone do not establish the branch.

### S3 — Publish stable sequence completion and synchronously reserve UI restart (fix now)

- **Prevents/fixes:** C1/C5/C7; `05-R3-04` P3 and `06-R3-01` P3.
- **Files/shape:** `SequenceRun.cs`, `Session.Processes.cs`, `RestartOperation.cs`, `MainController.cs`, `QuitFlow.cs` if revocation routing requires it; boundary/controller tests. (a) Construct one readonly asynchronous-continuation TCS per sequence; registration exposes its real task, and unregister precedes settlement; faults settle the same task. Scheduling never replaces it. (b) Split restart reservation/preparation from background execution so the UI registers launch authority before queueing. Dispose/Stop/Quit can revoke queued reservations. Avoid simply invoking today's async method on the UI thread: its synchronous prefix can start work and block in log open/process launch.
- **Dependencies:** none for (a); reuse the current restart registry for (b), then S4 extends its handoff. No second controller registry is necessary if Session owns the reservation.
- **Cost/risk:** (a) S/low–medium; (b) S–M/medium.
- **Verification:** gate between registration and launch; concurrent restart must wait for actual unregister and eventually launch once. Gate background scheduling before worker entry; Stop, StopAll, forced quit, graceful quit and Dispose prevent later start. Cancelling quit must not revive revoked work; a later explicit F5 remains usable. Ensure exception paths settle completion.

### S4 — Carry one action authorization through planning, input collection and each launch (structural, stage deliberately)

- **Prevents/fixes:** C1/C2/C5; `05-R3-02`, `05-R3-03`, `06-R3-03` (all P3). Also prevents recurrence of R1 `05-01/03/04/05` and R2 restart boundary fixes.
- **Files/shape:** `Session.Processes.cs`, `Session.cs` edit boundaries, `RestartOperation.cs`, `SequenceRun.cs`, `PlanNode.cs`, `MainController.cs`, later `EnterHint.cs`. First transfer caller cancellation across restart-to-sequence registration until the defined launch handoff; revalidate membership at each launch and reconcile anything started during invalidation. Then expose an action decision containing intended target set, required inputs, refusal/kill mode and scoped validity data. Preflight and execution consume it; hints may consume the same pure policy later. External I/O remains outside `_sync`, with an authorized copied launch request and tracked reservation bridging the gap.
- **Dependencies:** S3 completion/reservation first. Stop at Q1–Q3 before choosing modal target-set policy, group relocation policy or cancellation after handoff. The already documented no-launch-after-cancellation-before-first-launch case can be fixed without deciding the entire later sequence lifetime.
- **Cost/risk:** M for handoff/membership, M–L including action planner and hint migration; medium/high at orchestration boundaries. Deliver handoff, membership and modal policy as separate patches.
- **Verification:** gates at sequential registration, first-launch boundary, between parallel starts, and modal return. Cancel both restart overloads before any launch; assert no PID/log side effects and queues cleared. Move a later parallel target and assert it never launches with new ancestry; reconcile already-started targets per existing revocation behavior. Make a command become startable during prompting; require complete input coverage or a typed changed/retry result before effects. Table-test kill-first, nested reservations, ordered unique targets and independent parallel siblings using the same policy.

### S5 — Give log destinations their own progress and live budget (fix now, localized structure)

- **Prevents/fixes:** C1/C2/C6; R3 `04-01`, `04-02`, `04-03` (P3 each).
- **Files/shape:** `CommandOutput.cs`, `CommandOutput.LogWriter.cs`, log tests. Introduce destination state for committed cursor, running/waiting writer and open reservation. Post-open commit revalidates all constraints and disposes redundant streams outside the output lock where appropriate. Separate replay/live pending accounting and in-flight batches; successful flush alone commits progress. Keep dedicated workers and bounded waiting-writer policy.
- **Dependencies:** independent of S4/S6. Resolve Q4 before expanding completed-destination retention/revisit replay semantics; the proven waiting-writer omission and post-open bound can be fixed now without changing ordinary A→B→A behavior.
- **Cost/risk:** M/medium; queue counter repair and complete post-open recheck may ship as S patches before owner extraction.
- **Verification:** gate A's sink; A → waiting A → B flush → reuse waiting A must retain never-written A replay. Cover run reset/eviction while waiting. Gate three concurrent opens, disable between releases, and assert at most one running plus one waiting writer; repeat with abandonment/disposal during open. After replay drains, 10,001 pending live lines must trigger the 10,000-line bound, regardless of replay history. Retain failed-flush replay/allowed-duplicate tests and nonblocking kill/snapshot tests. Release all sink gates, join all fixture work before deleting files.

### S6 — Capture one coherent output state for copy, then other view operations (structural, bounded)

- **Prevents/fixes:** C1/C2/C3; `07r-01` and `07r-02` (P3).
- **Files/shape:** `OutputBuffer.cs`, `CommandOutput.cs`, `OutputView.cs`, `PartialPins.cs`, buffer/view/pin tests. First make append-completion plus partial replacement one buffer transaction. Provide an immutable read result containing version, absolute bounds, completed boundary, requested line references and resolved pin identities from the same lock acquisition. Resolve selection and capture its text sources together; draw/encode outside the lock. Adapt geometry incrementally. A small backward pin lookup patch may ship first, preserving identity+prefix checks.
- **Dependencies:** none on logging owner or complete run owner. Snapshot query must encompass pin resolution and range capture; independently captured metadata and later range reads do not close `07r-01`.
- **Cost/risk:** S for backward search; M for transactional publication/copy; M–L for all geometry consumers. Medium synchronization and allocation risk.
- **Verification:** deterministic writer gate between former validate/read calls; real copy action must never copy another stream. Test both streams completing before the other, both movement directions, rewrite/backspace disappearance, growth, identical text with different IDs, clear/eviction and partial-to-complete transition. Assert each captured state is either before or after publication, never duplicate completed+stale partial. Preserve existing allocation thresholds; viewport operations capture only needed references, not the whole retained ring. Use a few current-source stress tests as supplements, not substitutes for gates.

### S7 — Apply editor preservation to all fields through the real controls (fix now)

- **Prevents/fixes:** C3/C7; `06-R3-02` P3 and recurrence of R1 `06-01/02`.
- **Files/shape:** `CommandDialog.cs`, `GroupDialog.cs`, `CommandForm.cs`, `GroupForm.cs`, existing ArgsText/EnvText adapters, multiline/form tests. A small field-draft helper owns original value, representability and kept/edited state. Extend the existing read-only-and-preserve policy to single-line controls that cannot represent CR/LF; build from the preserved original rather than the truncated display value. Do not force all fields into one codec. Preserve current documented trimming rules pending Q5.
- **Dependencies:** none on immutable DTO conversion or planner. Confirm exact-value versus intentional normalization expectations in tests.
- **Cost/risk:** S–M/low–medium.
- **Verification:** actual dialog construct/layout/accept, unrelated edit, save/reload for every scalar string field including ready/busy, names, cwd, program and log path, plus args/env/stop input. Use embedded LF/CR, empty and whitespace cases; assert unchanged fields exactly survive and an intentional representable edit is committed. Add nondefault Ready/Leftovers/Stdin to the all-fields session round trip.

### S8 — Correct scalar intake and details drawing policy (fix now; independent patches)

- **Prevents/fixes:** C3/C7; R3 `04-04` and `07r-03` (P3).
- **Files/shape:** `AnsiLineParser.cs`, parser tests; `DetailsView.cs`, `DetailsSelection.cs`, `TextWidth.cs`/shared grapheme helper only as necessary, actual-driver tests. Complete a supplementary scalar before forced-break continuation decisions, retaining pending-high-surrogate style/state across chunks. Draw details through measured grapheme positions and the existing standalone-zero-width policy; keep logical offsets for extraction and combining components inside wider graphemes.
- **Dependencies:** independent of S6. Preserve documented scalar backspace behavior and whole-line/over-64-unit grapheme exceptions rather than choose a new terminal policy.
- **Cost/risk:** parser M/medium (SGR, Flush and copy-on-write); details S–M/medium (selection offsets).
- **Verification:** all code-unit offsets through flags/ZWJ/combining sequences at forced limits, unsplit and one-unit chunks, style switches and default 65,536 limit, explicit expected output and snapshot immutability. Assert real Terminal.Gui driver cells for U+200B, leading marks, styled combining clusters, right-edge clipping and mouse selection; pure width agreement alone is insufficient.

### S9 — Repair shell/token semantics and decide UNC batch cwd (fix now in separate patches)

- **Prevents/fixes:** C3/C4/C5; `07r-04`, `W03-02`, `W03-05` (P3); plausible `W03-03` P2 after policy decision.
- **Files/shape:** `CommandLineFormatter.cs`, `WindowsBatchCommandLine.cs`, `WindowsCommandResolver.cs`/`WindowsLaunchPlan`, `WindowsProcessPlatform.cs` only as needed, formatter/launch tests. Separate shell target and program/argument role. Always quoting the POSIX program is the smallest robust program-position repair. Make PowerShell `--%` handling semantic rather than merely quote-based. Preserve/restore the existing CMDR_CMD presence and value with guaranteed cleanup; assert no loss of command exit status. Add explicit batch cwd policy to the existing plan, shared with copy preparation where applicable.
- **Dependencies:** S0 before native tests; Q6 must be answered before implementing UNC support or rejection. Do not claim CR/LF/bang safety based on escape strings; Q7 needs native evidence. Existing copy remains program+args, not a promise to reproduce cwd/env.
- **Cost/risk:** POSIX S/low; PowerShell bridge S–M/medium; UNC rejection S or supported mapping wrapper M/medium after decision. No universal shell serializer.
- **Verification:** actual PATH executables named `if`, `do`, `X=1` must execute; semantic argv-echo tests cover literal `--%`, empty args, quotes/metacharacters and trailing slashes. Check CMDR_CMD absent, empty and preexisting values on success/failure, and exit status. Native PS7/5.1 and real batch/shim fixtures on a UNC share must report intended cwd or fail clearly according to Q6. Keep native argv and shell-source tests separate.

### S10 — Small standalone boundary repairs (fix now; no framework prerequisite)

| Item / bug class | Files and shape | Dependencies; cost/risk | Proof |
|---|---|---|---|
| `02/A3` C2 | ManagedProcess signal/stdin dispatch captures output run; CommandOutput adds run-qualified system publication checked under BeginRun's lock. | None; S/low. Full run owner not required. | Stall old stop, replace run, release old failure; no old failure in replacement output, ordinary current-run failure still reported. |
| `02/A1` C1/C5 | Delete unused ManagedProcess.RestartAsync after API support decision; otherwise give its blocked dispatch cancellation, pre-start checks and observation equivalent to Session. | Q8; S deletion or M supported repair; low/medium API risk. | Repository caller search plus compilation if deleted; otherwise cancellation before dispatch, during blocked stop, after exit and before launch. |
| `02/A2` C5 | LinuxProcessPlatform typed establishment/deadline result and failure cleanup; coordinate ProcessLaunched callback release with ManagedProcess failed-launch ownership. | Must preserve out-of-lock Process.Dispose; S decision test, M safe cleanup; medium risk. | Inject stalled setsid/stat, deadline and immediate leader exit; never report unestablished group as Running; owned leader reaped, callback completes without lock inversion. Do not merely throw into the current under-lock Dispose catch. |
| `W03-01` C3 | Validate embedded NUL in argv before native command-line serialization, with argument index; tests in WindowsCommandLine/launch contracts. | None; S/low. | Rejection before CreateProcess/child side effects; ordinary empty/Unicode args still serialize. |
| Config `R05` C5 | Program.cs explicit empty config-argument guard; a pure CLI parser only if it simplifies existing dispatch. | None; S/low. Unknown-option policy unchanged. | Empty argument yields usage/normal failure status, no unhandled exception; zero/valid/helper/version/multiple inputs retain behavior. |
| `07r-05` C5 | DetailsSelection.Extract returns empty for empty text/range. | None; S/low. | Call Extract on empty input, not just Clamp; retain nonempty selection expectations. UI already guards this call. |
| Config `R03` C4 | ConfigStore.LogPathErrors plus narrow existing-file-identity helper and LogPathConflictTests; supplement canonical strings with device/inode or Windows file ID. | S2 resolver contract useful but not required; M/medium platform work. | Distinct hard links conflict; distinct files and prospective paths remain distinct; cross-base opened-only error reporting preserved; native Windows identity cases visibly gated. Do not alter config hard-link save semantics. |

These items are ranked below current P2 repairs as a group, but the S/low items can ship immediately in independent patches. Do not defer them for broad structural work.

### S11 — Finish per-run ownership and platform preparation only where justified (structural, plan deliberately)

- **Prevents/fixes:** C1/C2/C4/C5; reinforces S1/S10 and the historical process/resource/handle failures. No additional R3 finding depends exclusively on this step.
- **Files/shape:** incrementally group ManagedProcess's process/group, identity, exit completion, readiness, flags, timers and RunPipes into an owned run; retain a separate immutable current snapshot. Then add narrowly scoped group-operation leases and owned helper deadline/drain handling in WindowsProcessGroup/CtrlCHelperCommand; freeze LaunchSpec constructor inputs at launch. Extend the existing Windows plan into explicit direct/batch variants and inputs; do not add OS checks to Session. Tighten native allocation transfer/error capture only while touching those paths.
- **Dependencies:** S1/S3/S4 define control/authority contracts; S0 fixtures plus affected S10 regressions precede resource movement. Q8–Q10 govern API/subscriber/restricted-proc/helper semantics. Q6 governs batch cwd. Do not move all launch/count/detach I/O out of lifecycle locks in one patch.
- **Cost/risk:** M–L/medium–high, native/helper migration M/medium separately. Each extraction must preserve shippable behavior; the wholesale lifecycle reducer (L/high) is deferred.
- **Verification:** retain old detached-output draining, generation checks, no kill-on-Linux-dispose, Windows job kill-on-close, unknown-leftover blocking, bounded exit drain, cancellation responsiveness and process-disposal lock-order tests. Fault each resource-acquisition/transfer step and assert exact cleanup; run rapid-exit/failed-start/dispose-with-I/O cases. Native Windows handles/helper/job checks and single-file helper launch remain acceptance gates. Do not equate Linux guarded passes with these results.

## 4. Practices and guard rails with direct evidence

1. **A boundary invariant on each asynchronous feature.** Extend docs/design's threading contract with a compact ownership table: UI-only state, Session tree/reservations, sequence state, process run, output notify/output/buffer locks, destination writer state. State which identity crosses each await/modal/dispatch and who settles completion. Preserve session → sequence → process ordering, output notify → process ordering, and out-of-lock callbacks/disposal. Correct superseded stdin-swap/never-close explanations when documenting those boundaries; this supports audits, not a cosmetic rewrite.
2. **Transition gates instead of timing as proof.** Every race regression must show both participants reached the intended transition before releasing either. Use controllable internal scheduler/sink/stat seams where needed; don't rely on arbitrary production subscribers under locks. Stress loops remain supplemental. A blocked-I/O test must assert entry into the actual write, not infer blockage from string size or a short sleep. Use fake monotonic time for deadlines.
3. **Independent oracles and branch evidence.** Assert inode/file handle identity for replacement/in-place claims; OS liveness of owned children for containment; received argv/cwd/env for shell claims; actual driver cells for layout; explicit expected text/styles for parser decisions. Invariant comparisons between two functions sharing one helper are useful consistency tests, not independent correctness evidence.
4. **Mutation-check a few critical regressions.** Locally remove/bypass the relevant guard/reset/identity check and ensure the regression fails. Prioritize status-message setup, cancellation checks, reset spellings and pin identity/direction; no new mutation-testing framework is needed. Fix the tautological EnvText assertion and the RGB-versus-indexed fixture. Tests must start from a state that the claimed behavior changes.
5. **Fixture owns construction, work, and teardown.** Capture each resource immediately; unwind partially failed construction. Release gates, stop producers, kill only independently owned children, reap/observe tasks and threads, dispose streams, then remove temp files. Teardown deadlines fail with diagnostics instead of silently falling through. A timeout around WaitForExit does not bound a preceding ReadToEnd. Isolate irrecoverably blocked kernel-I/O/fatal-callback probes in watchdog subprocesses. No generic process-name cleanup.
6. **Visible platform/capability coverage and a shared semantic corpus.** Use explicit skips for OS/tool/privilege prerequisites. Assert actual chmod/chown/rename capability before ownership branches; root/non-root cases differ. Gate native Windows acceptance on safe fixtures first. Share CR/LF, supplementary/ZWJ, `--%`, NUL, empty strings, shell syntax names, percent/bang and existing-environment cases across the appropriate boundaries, without assuming identical platform semantics. Linux and Windows CI plus SDK/dependency reproducibility remain tracker recommendations, not newly demonstrated defects.
7. **Postconditions and bounds at every stage.** Test metadata after the last write; progress only after flush; completion only after unregister; pending-live budget after replay drains; writer count after open commits; snapshot consistency after partial promotion. Retain allocation regression checks for tiny chunks and row counting when changing representation. These directly target the discovered defects.

No repository-wide analyzer project is justified by this evidence. Existing thread-safety attributes/comments cannot prove stale-intent validity; narrow internal assertions on owner/state invariants and table/gate tests provide more value. Do not add tests that simply mirror new implementation fields.

## 5. Consolidated open round-3 findings

All severities/statuses below are coordinator verdicts. `04-xx` is qualified as R3 to distinguish reused R1 IDs. “Standalone” means S10, not omitted. Windows-static qualifications are retained.

| ID (R3) | Severity / evidence qualification | Deduplicated issue | Roadmap |
|---|---|---|---|
| 01/R01 | P2, plausible; native Windows unverified | ReplaceFile 1176 cleanup can delete surviving temp after partial replacement | S2(b) |
| 01/R02 | P2, confirmed | Missing/file component before `..` redirects symlink save | S2(a) |
| W03-04 | P2, confirmed source; Windows test helper | Containment probe cleanup kills unrelated recent PING processes | S0, before Windows checklist |
| W03-03 | P2, plausible; native Windows unverified | Batch UNC cwd falls back to Windows directory | S9, blocked on Q6 policy |
| 05-R3-01 | P2, confirmed | Stale pending-restart confirmation kills replacement run | S1; worker 06 duplicate counted here |
| 01/R03 | P3, confirmed, niche | Hard-link log aliases missed | Standalone S10 |
| 01/R04 | P3, confirmed non-root | Last write clears special mode bits | S2(c) |
| 01/R05 | P3, confirmed | Empty single CLI argument escapes as ArgumentException | Standalone S10 |
| 02/A1 | P3, confirmed; no production caller | ManagedProcess.RestartAsync cancellation gap | Standalone S10, Q8 |
| 02/A2 | P3, confirmed source; rare >2s stall | Group-establishment timeout treated as launch success | Standalone S10 |
| 02/A3 | P3, confirmed | Old stop failure appended to new run | Standalone S10; S11 reinforces |
| W03-01 | P3, confirmed source; rare input | NUL truncates native argument command line | Standalone S10 |
| W03-02 | P3, confirmed by pwsh probe | Literal `--%` changes PowerShell argument passing | S9 |
| W03-05 | P3, confirmed | Batch copy overwrites/removes existing CMDR_CMD | S9 |
| 04/04-01 | P3, confirmed | Waiting writer reuse applies another destination's cursor | S5 |
| 04/04-02 | P3, confirmed; serial TUI doesn't supply trigger | Concurrent enables exceed writer bound | S5 |
| 04/04-03 | P3, confirmed | Replay permanently raises live queue allowance | S5 |
| 04/04-04 | P3, confirmed | Forced break splits supplementary grapheme continuation | S8 |
| 05-R3-02 | P3, confirmed; token API path | Cancellation lost between sequence registration and launch | S4 |
| 05-R3-03 | P3, confirmed; narrow edit window | Parallel restart launches command after it moves groups | S4 |
| 05-R3-04 | P3, confirmed | Sequence exposes completed placeholder before launch | S3(a) |
| 06-R3-01 | P3, confirmed; pool scheduling window | Controller restart queues before revocable registration | S3(b) |
| 06-R3-02 | P3, confirmed by control probe | Single-line controls truncate preserved config fields | S7 |
| 06-R3-03 | P3, confirmed | Post-modal activation starts newly eligible targets without inputs | S4, Q1 |
| 07r-01 | P3, confirmed source; narrow window | Selection validation and extraction read different output states | S6 |
| 07r-02 | P3, confirmed by probe | Selected stderr partial promotes backward, losing pin/cursor | S6; backward lookup can ship first |
| 07r-03 | P3, confirmed driver-buffer probe | Details zero-width drawing disagrees with columns | S8 |
| 07r-04 | P3, confirmed shell execution probe | POSIX program token treated as keyword/assignment | S9 |
| 07r-05 | P3, latent; current UI guards call | Empty details extraction indexes empty collection | Standalone S10 |

Test defects are tracked as acceptance work rather than additional production rows: S0 owns unsafe cleanup/owned resources; S3/controller tests own stable-hints preconditioning and dispatch gates; S4 owns overlap/cancellation/membership gates; S5 owns reset/SGR/counter/log cleanup gaps; S6 owns backward/concurrent copy harness cases; S7 owns exact edit/all-fields assertions; S8 owns boundary/driver expectations; S9 owns bounded shell subprocess tests and semantic argv assertions. Practices 3–6 cover visible skips, indexed-color data and tautologies. Remaining R1/R2 Windows verification and known JSON-path limitation stay in review/TRACKER.md; they are not new R3 rows.

## 6. Open questions and decision boundaries

Do not implement behavior dependent on these questions by assumption. Unambiguous narrow repairs above can proceed independently; no new design work was performed here.

- **Q1 — Activation after input modal:** execute exactly the originally prepared start set, or recompute and gather any additional inputs before effects? Both can meet the input-completeness guarantee; the roadmap does not select one.
- **Q2 — Group restart relocation:** if an original command/subgroup moves outside a restarting group's subtree while waiting, should it be omitted or invalidate the restart? Command-list restart's original direct-group rule is already explicit; do not extrapolate it silently to group restart.
- **Q3 — Caller cancellation handoff:** when does the caller token cease to govern a sequential restart: first launch or completed scheduling handoff? The clear pre-first-launch bug must be fixed; automatically cancelling the whole future sequence after RestartAsync returns is not assumed.
- **Q4 — Log destination revisit:** after an already completed A→B→A switch, replay whole retained run to A or only its missing lines? This determines destination-state retirement/history. The confirmed waiting-writer omission is wrong under either policy.
- **Q5 — Editor normalization:** existing form tests trim names/program/cwd/log path. Should future drafts preserve whitespace-significant values exactly? Preserve multiline unsupported fields now; do not use that repair to change intentional trimming without a decision.
- **Q6 — UNC batch cwd:** support with a mapping/pushd wrapper, or reject clearly? A wrapper changes environment/drive lifetime/cleanup and must be shared by actual launch and relevant copy preparation; rejection is smaller but changes supported inputs. No choice made.
- **Q7 — Batch representability:** what guarantee applies to CR/LF arguments and registry-enabled delayed expansion? Obtain native echo evidence and define rejection/support before broadening escaping. Current probes do not establish a new injection finding.
- **Q8 — Public ManagedProcess restart support:** may the unused helper be removed, or must external consumers retain it? Repository absence of callers alone does not decide public API compatibility. Coordinator recommends deletion.
- **Q9 — Subscriber failures:** are Changed/ProcessLinesCompleted subscribers required not to throw, or should the host contain/report each failure? No production throwing subscriber was established; catch/continue versus propagation is a policy decision, not a new P1.
- **Q10 — Supervision/helper contracts:** restricted `/proc` unreadability versus disappearance, delayed Ctrl+C dispatch after detach/dispose, and graceful console broadcast scope need explicit support limits before S11 changes semantics. Numeric PID rechecks are not atomic identity protection.
- **Q11 — Persistence scope:** process-crash atomicity versus power-loss durability; mode/owner/group versus ACL/xattr/capability preservation. The roadmap preserves existing explicit guarantees and does not assert that flushing a file defines all rename durability.
- **Other proposals not prerequisites for current fixes:** supported minimum terminal dimensions, reserved Windows log filenames, lexical versus physical import identity, concurrent public BuildConfig/BuildRows support, width delegate mutability, scalar versus grapheme backspace, details-selection behavior after text edits, unknown single dash-prefixed CLI inputs. Preserve current accepted behavior and ask only if a later change actually depends on one of these.

## 7. Investigation, commands and outcomes

Work performed: reconciled the coordinator's severities and deduplication; examined R1 inventory and relevant detailed reports, R2 completed fixes and tracker; read the seven R3 handoffs' findings/proposals/test issues/questions; traced the named production boundaries and test helpers; costed and narrowed the merged proposals. No new per-area bug hunt, source modification, test implementation, or design phase was started.

Commands were read-only shell inspection except the explicitly required handoff heredoc:

- `cat /tmp/cmdr-claude-run-review2/08-worker-request.md` — read the authoritative read-only constraints and required result format.
- `pwd`, `ls -la`, `ls /tmp/cmdr-claude-run-review2`, `rg --files -g AGENTS.md -g '!node_modules' -g '!vendor'`, ancestor `ls` for AGENTS.md — cwd correct; no repository/ancestor AGENTS.md found. The no-match rg exited 1; ancestor ls reported files absent. These were discovery outcomes, not implementation failures.
- `cat`, `sed -n`, `rg -n` over `00-coordinator-plan.md`, results `01`–`06` and `07r`, README.md, docs/design.md, review/REPORT.md, review/COMPLETED-FIXES.md, review/TRACKER.md, and the six R1 reports — inventory, evidence qualifications, organization proposals and existing policies inspected. Large combined output was occasionally tool-truncated; focused section reads were used for the relevant proposal/history/question material.
- `rg --files src tests`, `cat`/`sed`/`rg` on ManagedProcess, LaunchSpec, PreparedLaunch, LinuxProcessPlatform, WindowsCommandResolver/launch plan, Session/Session.Processes, RestartOperation, SequenceRun, PlanNode, CommandOutput and its LogWriter partial, OutputBuffer, MainController, OutputView, PartialPins, DetailsView, CommandLineFormatter, CommandForm/CommandDialog/GroupDialog, AnsiLineParser — verified feasibility against actual current structure, including lock boundaries, registration/publication, output mutation and control dispatch.
- `cat`/`sed`/`rg` on ProcessTestHelpers, SessionTestHelpers, OutputTestHelpers, TempDir, SessionConcurrencyTests, MainControllerStatusTests, OutputViewHarness, ConfigStore in-place tests, WindowsJobContainmentTests and output writer tests — verified cleanup, setup, branch and oracle weaknesses used in the roadmap.
- `git log -1 --format='%h %s'` — HEAD `a27a7ce`, expected subject. `git diff --stat` — empty before handoff generation. Final `git diff --exit-code` and `git diff --stat` are recorded below after writing this handoff.
- `cat > /tmp/cmdr-claude-run-review2/08-worker-result.md <<'EOF' ... EOF` — created this required deliverable, outside the repository. No scratch probe files were needed.

**Tests/builds run by worker 08: none.** No proposal required another executable probe to decide feasibility beyond the existing source-linked area probes and current-code inspection. Avoided repository build artifacts and did not rerun a full or targeted suite merely to repeat prior evidence. Existing reported area passes are not worker-08 test results; confirmed probes and Windows plausibility remain attributed to their reviewers/coordinator. No native Windows, physical terminal, clipboard or power-loss verification is claimed.

## 8. Remaining problems and recommended next step

All 29 R3 findings remain unfixed by this read-only task. Several proposed structural changes depend on the questions above; no dependent policy has been guessed. First land S0's safe Windows fixture, then S1 and S2's P2 regression/fix patches, with S3's small completion/dispatch fixes alongside them. Cheap independent S7/S8/S10 repairs need not await S4/S11. Promote the already controlled area probes into focused checked-in regressions during that implementation work. Use targeted tests per patch, followed by the coordinator's normal validation and native Windows acceptance only after the fixture is safe.

The main prevention investment is completing action authority, destination ownership and coherent output reads. Defer immutable-model rewrites, a wholesale lifecycle reducer and generalized frameworks until a concrete change needs them.

## 9. Files changed in the repository

none

Outside-repository deliverable: `/tmp/cmdr-claude-run-review2/08-worker-result.md` only.

Final verification: `git diff --exit-code` exited 0 and `git diff --stat` was empty after the handoff was written. A focused `rg -n` of handoff section headings and inventory rows confirmed all nine required result sections, roadmap S0–S11, and 29 mapped finding rows. The handoff write completed successfully. No repository files were changed.
