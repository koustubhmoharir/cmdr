# Consolidated review and fix plan

Baseline: `a06d405e01095309617069e43c5d4be9200c0ebc`. Review date: 2026-11-13. Six independent GPT 6.1 Sol reviewers examined the functional areas listed in [the review index](README.md). Production code is unchanged.

## Assessment

cmdr has a useful architecture: a UI-independent Core, explicit process-platform interfaces, immutable process snapshots, source-aware configuration inheritance, and many pure UI helpers. The baseline suite reports **1,190 passes and no failures** on Linux. Those strengths make focused repair feasible, but the suite misses important lifetime, concurrency, filesystem, editor round-trip, and terminal boundary cases.

The most urgent fixes concern process supervision: pending restarts can launch during shutdown; errors in leftover counting can terminate the host; old output can cross run boundaries and affect readiness; and logging can block force kill. Configuration/editor integrity also needs attention: ordinary saves can change permissions or break symlinks, default log paths can collide, and accepting an unchanged editor can alter command arguments or environment values.

Windows containment and executable selection need a dedicated implementation/test pass. The platform reviewer clearly separates pure resolver reproductions from native behavior assessed statically. No native Windows execution was available; passing guarded tests on Linux is not evidence that Windows supervision works.

**Consolidated result: 40 distinct findings — 2 P1, 32 P2 and 6 P3 — plus one measured P3 performance recommendation (04-11).** The six area reports contain 42 numbered entries: 06-04 duplicates 01-05, and 04-11 is a recommendation rather than a verified correctness defect. “Verified” includes source-supported Windows findings whose native consequences remain untested on Windows; see each report for the evidence level.

## Highest-priority changes

| Priority | Finding | Consequence | Primary source |
|---|---|---|---|
| P1 | 05-01: Restart retains launch authority during shutdown | Stop/force-kill can be followed by a newly launched process; quit keeps waiting | [Session.Processes.cs](../src/Cmdr.Core/Session/Session.Processes.cs), lines 140–165, 196–207; [MainController.cs](../src/Cmdr/Tui/MainController.cs), lines 769–788 |
| P1 | 02-01: Uncontained leftover-count exceptions | An operational query failure terminates the entire supervisor; exit waiters may remain incomplete | [ManagedProcess.cs](../src/Cmdr.Core/Processes/ManagedProcess.cs), lines 520, 598 |
| P2 | 02-02 / 02-03: Run resource ownership is incomplete | Stale output falsely marks the new run ready; disposed supervisors retain live pumps/streams | [ManagedProcess.cs](../src/Cmdr.Core/Processes/ManagedProcess.cs), lines 394–395, 440–470, 614–622 |
| P2 | 02-04 / 02-05: I/O is not safely isolated from control | Concurrent stop writes corrupt writer state; stalled logging prevents force kill and snapshot reads | [ManagedProcess.cs](../src/Cmdr.Core/Processes/ManagedProcess.cs), lines 182–209, 645–663 |
| P2 | 05-03 / 05-05: Restart cancellation and tree threading disagree with callers | Cancellation may not stop relaunch; editing during background restart produces collection-enumeration errors | [Session.Processes.cs](../src/Cmdr.Core/Session/Session.Processes.cs), lines 142–165, 197–207; [Session.cs](../src/Cmdr.Core/Session/Session.cs), lines 108–120, 180–207 |
| P2 | 01-01 / 01-02 / 01-04: Persistence changes file identity/metadata or aliases log destinations | Private modes broaden, symlinks break, distinct commands lose/mix logs | [ConfigStore.cs](../src/Cmdr.Core/Config/ConfigStore.cs), lines 156–166; [ConfigPaths.cs](../src/Cmdr.Core/Config/ConfigPaths.cs), lines 80–106 |
| P2 | 06-01 / 06-02: Editor conversion is lossy | A no-op edit changes argv or introduces environment variables/removals | [ArgsText.cs](../src/Cmdr/Tui/ArgsText.cs), lines 15–41; [EnvText.cs](../src/Cmdr/Tui/EnvText.cs), lines 13–64 |
| P2 | 03-01: Windows child starts before job assignment | Early descendants can escape accounting and termination | [WindowsProcessPlatform.cs](../src/Cmdr.Core/Processes/WindowsProcessPlatform.cs), lines 135–198; native consequence statically assessed |

The detailed reports contain exact triggers, observed results, severity, fix direction and regression tests. P1/P2 rank repair urgency; they are not claims of exploitability or prevalence. Fault-injection findings establish failure handling under the injected condition, not how often that condition occurs in production.

## Complete finding inventory

Area reports preserve their original IDs. 06-04 duplicates 01-05 and is counted once in the consolidated total. Cross-review support for 05-01/05-03 is also counted only under those IDs. API-only and Windows-static qualifications remain in the linked reports.

| ID | Priority | Finding | Report | Consolidation note |
|---|---|---|---|---|
| 01-01 | P2 | Saving a private configuration broadens its permissions | [01](01-configuration.md) |  |
| 01-02 | P2 | Distinct valid names can share a default log file and lose output | [01](01-configuration.md) |  |
| 01-03 | P2 | Command-shaped objects in `groups` are accepted and silently discarded | [01](01-configuration.md) |  |
| 01-04 | P2 | Saving through a symbolic link replaces the link instead of updating its target | [01](01-configuration.md) |  |
| 01-05 | P2 | Load failures escape the configuration error boundary or masquerade as a new file | [01](01-configuration.md) |  |
| 01-06 | P2 | UTF-8 BOM files are rejected; recent history is silently lost on read | [01](01-configuration.md) |  |
| 01-07 | P3 | Recent-file validation retains unusable paths that later cannot be removed | [01](01-configuration.md) |  |
| 01-08 | P3 | String enum converters accept comma-separated combinations for single-choice settings | [01](01-configuration.md) |  |
| 01-09 | P3 | Nested JSON errors report item-local lines as whole-file coordinates | [01](01-configuration.md) |  |
| 01-10 | P3 | Editable deep trees pass validation but cannot be saved through the normal error handler | [01](01-configuration.md) |  |
| 02-01 | P1 | A process-count query error terminates the supervisor host | [02](02-process-lifecycle.md) |  |
| 02-02 | P2 | Old output pumps contaminate subsequent runs and can falsely mark them ready | [02](02-process-lifecycle.md) |  |
| 02-03 | P2 | Disposal and run release do not dispose owned redirected streams or stop pumps | [02](02-process-lifecycle.md) |  |
| 02-04 | P2 | Concurrent stop requests write unsafely to the same StreamWriter | [02](02-process-lifecycle.md) |  |
| 02-05 | P2 | Force kill waits for log I/O before sending the kill and holds the lifecycle lock | [02](02-process-lifecycle.md) |  |
| 03-01 | P2 | Early Windows children can permanently escape job supervision | [03](03-platform-processes.md) |  |
| 03-02 | P2 | Relative Windows PATH entries are checked and executed in different directories | [03](03-platform-processes.md) |  |
| 03-03 | P2 | Failed Windows resolution can launch a different executable through native fallback | [03](03-platform-processes.md) |  |
| 03-04 | P2 | Drive-rooted Windows command paths are appended to the working directory | [03](03-platform-processes.md) |  |
| 04-01 | P2 | Complete C1 string-sequence handling | [04](04-output-terminal.md) |  |
| 04-02 | P2 | Preserve surrogate pairs at the forced line boundary | [04](04-output-terminal.md) |  |
| 04-03 | P2 | Backspace must not leave a lone surrogate | [04](04-output-terminal.md) |  |
| 04-04 | P2 | Zero display width does not mean text can be discarded | [04](04-output-terminal.md) |  |
| 04-05 | P2 | Expand tabs from display columns, not UTF-16 length | [04](04-output-terminal.md) |  |
| 04-06 | P2 | Preserve inverse video when theme colors are terminal defaults | [04](04-output-terminal.md) |  |
| 04-07 | P2 | Following must account for the wrapped row, not only the last logical line | [04](04-output-terminal.md) |  |
| 04-08 | P2 | Give partial lines stable identity or invalidate selections when identity changes | [04](04-output-terminal.md) |  |
| 04-09 | P2 | Advance the logged cursor only after a successful flush | [04](04-output-terminal.md) |  |
| 04-10 | P2 | Avoid quadratic copying of growing partial lines | [04](04-output-terminal.md) |  |
| 04-11 | P3 | Count rows without allocating all drawable cells and row ranges | [04](04-output-terminal.md) | Measured recommendation; excluded from defect count. |
| 05-01 | P1 | Pending restart can relaunch commands after force-kill shutdown | [05](05-session-orchestration.md) |  |
| 05-02 | P2 | Command-list actions do not enforce session ownership | [05](05-session-orchestration.md) |  |
| 05-03 | P2 | Restart cancellation does not cover blocking stop dispatch or reliably prevent relaunch | [05](05-session-orchestration.md) |  |
| 05-04 | P2 | A newly added command can start an overlapping run in a queued child group | [05](05-session-orchestration.md) |  |
| 05-05 | P2 | Background restart traverses the editable tree without synchronization | [05](05-session-orchestration.md) |  |
| 05-06 | P3 | Command-list sequencing follows caller order and repeats duplicate targets | [05](05-session-orchestration.md) |  |
| 06-01 | P2 | Accepting the command editor silently changes arguments containing newlines | [06](06-ui-application.md) |  |
| 06-02 | P2 | Environment editing can silently split one value into extra variables or removals | [06](06-ui-application.md) |  |
| 06-03 | P2 | Valid placeholder sets can place required input fields outside the visible dialog | [06](06-ui-application.md) |  |
| 06-04 | P2 | Expected config file/path errors escape both application entry paths | [06](06-ui-application.md) | Same root cause as 01-05; not counted twice. |
| 06-05 | P2 | Windows copied command lines quote for an argument parser, not for the shell that receives the paste | [06](06-ui-application.md) |  |
| 06-06 | P3 | A transient status message never clears when the hint text is unchanged | [06](06-ui-application.md) |  |

## Implementation plan

### Work package A — Own session operations and serialize tree access

**Findings:** 05-01, 05-03, 05-04, 05-05; supporting evidence in 02 and 06. Start here for the shutdown defect.

Introduce explicit ownership for restart operations, including cancellation, completion, and authority to launch. Entering shutdown must invalidate pending launch authority before sending stops/kills. Revalidate immediately before launch under the same ownership protocol; cancelling quit must not revive old operations. Cover both session restart overloads and the separate `ManagedProcess.RestartAsync` helper.

Choose one consistent session threading model. Prefer owner-thread planning/tree mutation with immutable execution plans and background signal/wait work, or synchronize all tree reads and writes consistently. Do not hold a tree/lifecycle lock while awaiting exit, writing stdin, doing log I/O, or invoking subscribers. Reserve queued groups as well as their original command members so commands added later cannot start an overlapping run.

**Acceptance:** Gate a restart, invoke graceful and forced quit, and assert no replacement PID appears. Already-cancelled restarts have no side effects. Cancellation returns promptly while stop-input dispatch is blocked; abandoned dispatch is still observed and cleaned up. Editing an unrelated group during restart cannot throw or leave partial registration. Adding a command to a queued child group cannot disrupt the original sequence. Exercise actual controller/quit integration, not only Core methods.

### Work package B — Make each process run own its resources

**Findings:** 02-01, 02-02, 02-03, 02-04, 02-05. Coordinate with A at the stop/restart boundary.

Create an explicit per-run resource owner for process/group handles, redirected streams, pump tasks, readiness matcher, completion, cancellation, and stdin serialization. Carry run identity through output publication, atomically with parser/buffer run transitions. Explicitly close/cancel/observe resources on release and disposal. Decide how detached children holding inherited output handles are handled; preventing contamination must not introduce an unlimited drain wait.

Contain expected count-query failures at both callback boundaries. Complete leader-exit waiting even if descendant enumeration fails; retain an unknown/error supervision state rather than equating failure with zero leftovers. Deliver control signals independently of logging. Serialize stdin with a per-run queue/gate outside lifecycle locks. If logging is queued, impose a bounded policy rather than moving the blockage into unbounded memory growth.

**Acceptance:** Fault-injected exit/timer count failures do not terminate the host or permit unsafe restart. A delayed old child cannot put text/style/readiness into a new run. Disposal settles pumps and closes captured streams without requiring GC. Simultaneous stops preserve complete records and do not throw writer-internal exceptions. Force kill reaches the group while a log sink is blocked, and snapshots remain responsive. Include failed-start cleanup and disposal during outstanding I/O.

### Work package C — Preserve user configuration and logs

**Findings:** 01-01 through 01-10, plus duplicate UI finding 06-04. The highest-value subset can proceed in parallel with A/B.

Centralize atomic file replacement with explicit metadata and symlink semantics. Create temporary files with appropriate permissions before writing sensitive contents; preserve existing modes. Follow supported links deliberately or reject them clearly. Make default log-path identity collision-resistant across names, ancestry and imported sources; avoid silently changing log-history location without explaining the migration.

Normalize expected load/path failures into contextual configuration errors, distinguishing missing from inaccessible files. Preserve the recent picker after errors and return a normal CLI error status. Reject command-shaped top-level groups; support a leading UTF-8 BOM; canonicalize/drop unusable recent paths; enforce strict single-token enums; preserve nested error locations; and define a tree-depth limit shared by editing, validation, loading and saving.

**Acceptance:** Save a `0600` config and retain its mode; save through supported symlinks without replacing the link. Distinct sanitized names retain independent log output. Unreadable/invalid-path configs fail normally; only genuinely missing opened files yield defaults. BOM inputs load equivalently. Ambiguous top-level shapes are rejected before save can erase fields. Deep trees either round-trip or are rejected before unsavable edits accumulate. Failure paths preserve the old file and dirty state.

### Work package D — Make editing and prompting lossless

**Findings:** 06-01, 06-02, 06-03, 06-05, 06-06. Independent of A/B except shared controller refactoring.

Give arguments and environment values a reversible editor representation, preserving CR/LF, empty values and literal backslashes. Add a scrollable placeholder form that reveals focused fields, with bounded labels and resize-aware widths. Restore hint display mode when transient messages expire. Define the copy target shell explicitly and keep native argv formatting separate from shell-paste formatting.

**Acceptance:** Actual dialog open/accept/save/reload leaves argv and environment byte-for-byte unchanged, including multiline values and directive-looking continuation text. Every field remains visible while focused with 30 placeholders, long names and a small/resized terminal. Stable hints return after message expiry. Native Windows shell argument-echo tests prove copied commands preserve metacharacters and cannot accidentally create another command or redirect output.

### Work package E — Make Windows launch resolution and containment explicit

**Findings:** 03-01 through 03-04. Requires a native Windows runner for acceptance.

Normalize Windows paths with a single policy for relative PATH entries, fully qualified paths, drive-rooted paths and supported drive-relative forms. Return absolute selected executables; a failed resolution must not silently fall through to a different native search policy. Establish job containment before the child can spawn: creation-time association or suspended creation, job assignment, then resume. Own and clean up every intermediate handle/process on failure. This launch redesign can also remove process-wide stdin-handle mutation.

**Acceptance:** Controlled early-spawning children are job members and actually die after termination; verify independent child liveness rather than supervisor counts alone. Distinguishable fixtures in supervisor/child directories prove the selected executable is the intended one. Native argv round trips cover `.exe`, ordinary batch files, node shims and shell metacharacters. Exercise the real Ctrl+C helper and pipe/console stdin on Windows, including single-file publication.

### Work package F — Preserve terminal text and bound output costs

**Findings:** 04-01 through 04-10; measured recommendation 04-11. Output parsing, styled text, wrapping, scrolling/selection and log recovery; detailed findings in [04-output-terminal.md](04-output-terminal.md). Coordinate stale-output/resource ownership with B rather than counting those again.

Unify the text model used for parser mutation, tabs, wrapping, drawing and copying. Do not split UTF-16 surrogate pairs or discard combining marks; use grapheme-aware rendering/selection where required. Handle supported C1 control-string starts/terminators consistently and keep unsupported escape payloads out of visible output without swallowing following text. Separate viewport-following policy from cursor/selection changes, and use stable identities for partial stdout/stderr lines. Preserve inverse styling when terminal-default colors are unresolved.

Avoid rebuilding the entire growing partial line on every tiny input chunk or materializing every drawable cell simply to count rows. Use versioned snapshots/caches or lazy traversal with bounded retention. Keep successful log-write bookkeeping distinct from merely buffered writes so retry after a write/flush failure does not silently omit retained lines. Define the retry/duplicate policy explicitly.

**Acceptance:** Chunk-boundary tests cover C1 OSC/ST, incomplete escapes, supplementary characters, combining sequences, backspace and tab stops. Copy agrees with visible text. Home and mouse selection on a final line taller than the viewport do not snap back to the bottom. A selected partial stdout line does not become stderr on completion. Retrying a failed log sink retains unwritten backlog. Allocation tests vary chunk size and line length; separately measure retained memory and UI responsiveness.

**Measured memory-cost evidence:** A source-linked Release probe delivering 32 KiB of partial ASCII output allocated about 1.085 GB cumulatively in one-character chunks, versus 0.379 MB in 4,096-character chunks. Separately, 100 row-count operations on a 65,536-character line allocated about 133 MB. These figures demonstrate allocation amplification under the stated probes, not retained heap size or whole-app peak memory. The current line-count and per-line limits bound dimensions of storage but do not provide a predictable byte budget across many styled command buffers; a byte budget remains a recommendation, not a demonstrated leak. See report 04 for full methodology and limitations.

### Work package G — Tighten public session API contracts

**Findings:** 05-02, 05-06. These are API-only issues; the reviewers found no ordinary TUI path passing foreign or duplicate/out-of-order selections.

Validate every target's session ownership and removal state before any side effect, including logging and input preflight. Normalize a selection to unique sibling commands in file order, or reject/document a different explicit contract. Apply the policy consistently across activation and restart.

**Acceptance:** Foreign, removed, mixed and duplicate targets cannot partially launch, register queues, log, or mutate another session. A reversed selection follows the documented execution order exactly once per command. Existing legitimate selection behavior remains intact.

## Maintainability and adding future features

The existing Core/UI split should be preserved. The most valuable refactoring is making ownership explicit where it is currently spread across mutable fields and background continuations:

- **Run owner:** streams, pumps, process group, matcher and completion belong to one run. This supports future async input and richer output safely.
- **Session operation owner:** planning, tree mutations, queue reservations and launch authorization use one threading model. This supports new group modes and long-running actions without more ad hoc background tasks.
- **Reversible editing codecs:** argv, environment and stop input need explicit round-trip contracts. Reuse validation rules between forms and config loading while keeping presentation separate.
- **Persistence policy:** centralize permissions, symlinks, atomic replacement and contextual errors. Keep source-specific base-file ownership intact.
- **Windows launch boundary:** prepare/launch/attach should return fully owned resources, not expose a running uncontained child between phases.
- **UI test seams:** extract lifecycle/action dispatch and status state from the large controller where justified by these defects. Inject clocks/dialog adapters; exercise real controls with the buffer driver as well as pure helpers.

Adding another configuration item type currently requires coordinated converter, metadata, normalization, validation, cloning and session traversal changes. Keep the hierarchy deliberately closed or consolidate dispatch so an unsupported type fails explicitly. Unknown-property/comment preservation is a separate compatibility policy: current tests intentionally allow unknown properties, and save drops them; do not present that existing policy as a newly proven defect.

## Delivery and validation gates

The baseline test command and caveats are in [VALIDATION.md](VALIDATION.md). Focused tests run by reviewers overlap baseline coverage and must not be added to 1,190 as if they were new distinct tests. The custom probes establish the missing scenarios; passing existing tests does not contradict their findings.

1. Add the regression tests specified for each package before or with its implementation. Use deterministic gates and injected platform/stream behavior for concurrency/failure paths; isolate intentional crash probes in subprocesses.
2. Run Core and UI tests on both Linux and Windows. Make platform exclusions visibly skipped rather than early-return passes. Add real Windows process, argument, console/helper and independent-child-liveness tests.
3. Exercise the actual UI for edit round trips, input focus visibility, error recovery, pending restart/quit and resize. Buffer-driver probes are useful, but do not establish physical-terminal rendering or clipboard compatibility.
4. After output/lifetime fixes, measure retained memory, allocations, descriptor/handle counts and input/kill responsiveness under sustained styled output, long lines, repeated restarts, detached pipe holders and slow logging. Define budgets before judging optimizations.
5. Smoke-test framework-dependent single-file publications on both supported platforms, especially helper relaunch. Add tracked CI and a documented SDK/dependency reproducibility policy; no tracked CI workflow, SDK pin or dependency lockfile was found in the baseline.

No production patch is included in this review. The implementation packages can become separate changes; A/B require coordination at shared ownership boundaries, C/D can largely proceed independently, and E needs native Windows acceptance before completion.
