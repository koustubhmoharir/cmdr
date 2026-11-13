# Deferred decisions and finding disposition

Review commit: `1af6ef9` (`docs: add comprehensive code review and prioritized fix plan`), reviewing code baseline `a06d405e01095309617069e43c5d4be9200c0ebc`.

This register distinguishes user-facing or public-contract choices from implementation work. It does not choose a policy. “Outstanding” means the reviewed defect has not yet been fixed in this work pass; it does not mean the issue is accepted.

## User and API contract decisions

| IDs | Question and options | Acceptance criteria |
|---|---|---|
| **01-01 — save metadata** | Preserve an existing Unix mode and define a mode for new files, or use a fixed private mode? Define Windows ACL behavior separately. Apply the rule to recent-file storage too. | Restrictive existing files remain restrictive; temporary contents are protected before replacement; new-file behavior is documented and tested on supported platforms. |
| **01-02 — generated log paths** | How should default log paths distinguish names that sanitize alike and avoid file/directory collisions? Options include reversible escaping, a stable identity suffix, or detecting conflicts and reporting an error. Define whether explicitly identical log paths intentionally share a file, including across imported sources. | Valid distinct commands/sources cannot silently mix or lose default logs; deliberate explicit sharing has a documented outcome; `build` beside `build.log` is handled predictably. |
| **01-04 — symlink saves** | Should save follow a destination symlink and replace its resolved target, or refuse the save with an actionable error? Define relative and dangling-link behavior. | The selected behavior preserves either the link and target relationship or refuses before mutation; relative/absolute/dangling cases and failure cleanup are covered. |
| **01-10 — nesting depth** | Is there a supported maximum tree depth? Define one bound enforced on load/edit/validate/save, or support deep trees with iterative operations and an explicit safe limit. | No editable/valid tree becomes unsavable; the boundary is consistent and over-limit configurations receive a clear diagnostic. |
| **06-01 / 06-02 — editor representation** | What user-visible representation is reversible for argv and environment overlays, including CR/LF, empty strings, and literal backslashes? Options include escaped multiline text, structured row editors, or preserving unchanged originals while blocking unsafe edits. | No-op edit/save/reload preserves exact values. Supported edits preserve multiline/directive-looking data and are documented. |
| **06-05 — copied command target** | Which Windows shell does copy target: cmd.exe, PowerShell, a selectable target, or explicitly labeled native argument syntax? | Actual shell round trips preserve arguments with metacharacters, substitutions, quotes, whitespace, empty strings, backslashes, and Unicode; no extra command or redirection executes. |
| **05-06 — selection API ordering** | Canonicalize command-list targets to unique file order, as the documented contract says, or preserve caller order and duplicates? UI selection currently provides unique file order. | Activate, restart, preflight, and input prompting follow one documented rule; tests cover reversed and duplicate caller input. |

## Outstanding architecture and implementation work

These are not open product choices. They need an engineering design and implementation consistent with the stated acceptance requirements.

| IDs | Work and acceptance |
|---|---|
| **02-01..03, 02-05** | Give each run ownership of streams, pumps, readiness, cancellation, and completion; isolate late output from the next run; contain count-query failures without reporting unknown as zero; make disposal settle owned I/O; ensure blocked logging cannot block force kill or snapshot reads. Test injected failures, delayed old pumps, inherited pipe handles, and disposal during I/O. |
| **05-01, 05-03, 05-05** | Define restart operation lifetime, launch authority, shutdown invalidation, cancellation checkpoints, and a consistent tree threading model. Actual controller/quit tests must show no post-shutdown launch, cancellation-respecting restart, and no partial registration/tree enumeration failure during concurrent edits. |
| **04-09** | On log flush failure, do not advance the log cursor past unwritten data. Specify bounded retries or explicit failure state in implementation. Blocked/failing sink tests must keep force kill and snapshots responsive and show recovery/cursor behavior. |
| **03-01, 03-03** | Establish Windows job containment before child code can spawn descendants (for example, creation-time association or suspended creation followed by assignment and resume). Make resolver misses fail or explicitly model any intended native/system lookup; do not silently switch policies. Native tests must check descendant liveness and exact selected executable. |
| **04-04, 04-05** | Define/preserve zero-width text and provide a shared terminal-width implementation that Core and the UI renderer can use for tabs. This is engineering alignment, not a user policy blocker. Validate combining marks, wide characters, and tabs against rendered columns. |
| **04-07, 04-08** | Align follow-to-tail and selection identity with wrapped rows, partial-line completion, and buffer eviction. Tests must show that follow exposes the final wrapped row and a changing partial line cannot redirect a selection to different text. |

Performance findings **04-10** (quadratic copying of a growing partial line) and recommendation **04-11** (allocation-heavy row counting) are engineering work, not policy decisions. Choose a suitable representation/counting path, then measure streamed long lines and redraw preparation against the baseline while preserving output and row-count results.

## Direct fixes

These have clear repair directions and are not design decisions: **01-03** reject command-shaped objects in `groups`; **01-05** distinguish missing from inaccessible files and normalize expected load/path errors; **01-06** accept a UTF-8 BOM on read; **01-07** normalize/filter/dedupe unusable recent paths; **01-08** reject composite enum strings; **01-09** report nested JSON source locations correctly; **03-02 / 03-04** resolve relative PATH entries and drive-rooted paths consistently; **04-01..03** handle C1 strings and surrogate pairs correctly; **04-06** retain inverse style with default-color sentinels; **05-02** validate target ownership before side effects; **05-04** enforce run reservation for queued subgroups; **06-03** scroll/reveal focused input fields; **06-06** restore hints when transient messages expire. These may require ordinary implementation choices but no product policy selection.

## Validation gaps

Validation is separate from implementation status. The original review had no native Windows run: **03-01..04** need actual Windows resolution, containment, and child-liveness tests; **06-05** needs execution through its supported shell. Some Windows-specific tests return early on non-Windows hosts, so a passing Linux run does not count as Windows coverage; report these as skipped/not exercised. Core output/UI tests do not establish physical terminal rendering or clipboard interoperability. Run fault-injection for process counts, inherited pipe handles, and blocked/failed log sinks (**02-01..03, 02-05, 04-09**); test controller restart/quit ownership and concurrent edits (**05-01, 05-03, 05-05**). Measure **04-10/11** rather than inferring performance. The review's baseline pass count is Linux-only and does not satisfy these checks.

### Full solution validation

The primary agent independently reviewed the patches and ran `dotnet test Cmdr.slnx --no-restore -m:4` on Linux with .NET SDK 10.0.404. The final default run built the solution and passed **651 Core tests plus 579 UI tests (1,230 total)**, with no failures or reported skips. CLI startup also returned `cmdr 0.1.0`. A full run with test collections serialized passed as well. Native Windows behavior remains untested, including tests whose bodies return early on Linux.

Earlier parallel runs exposed immediate-output assertions in `SessionInputTests`: the existing `WaitForExited` helper explicitly permits redirected output after the exit marker because draining is bounded. Four assertions now use the existing bounded `WaitForText` helper before checking output; assertions and timeout values were retained. An archived, unchanged review baseline passed its Core suite twice (618 tests each). This test synchronization correction does not resolve the outstanding production run-resource/drain findings.

Luna subagents implemented the changes; the primary agent independently inspected source and tests, requested corrections to boundary handling and UI focus verification, and performed the final validation before committing. Seventeen distinct review findings are addressed below. The unresolved severe lifecycle and orchestration findings remain outstanding.

## Finding disposition for this work pass

Status below reflects the final reviewed implementation and validation above. Duplicate **06-04** has the same cause as **01-05**. **04-11** is a measured recommendation, not a correctness defect. Engineering items below are outstanding work, not requests for user policy choices.

| Finding IDs | Current disposition |
|---|---|
| 01-01, 01-02, 01-04, 01-10 | Outstanding decisions: save metadata/temp-file policy, generated-log identity, symlink semantics, and depth policy. |
| 01-03, 01-05..08 | Fixed in config work; agent reports implementation/tests for group-shape rejection, load/path errors, BOM, recent-path normalization, and strict enums. |
| 01-09 | Fixed: cumulative source-origin translation corrected nested line/column coordinates; config agent reports 243 focused tests passing, including shallow/deep and Unicode cases. The enclosing JSON path remains limited by custom converter serializer-stack boundaries. |
| 02-01..03, 02-05 | Outstanding severe process-lifecycle work: resource ownership, count-query failure containment, stale output isolation, and blocked logging. |
| 02-04 | Fixed: per-run stdin writer serialization outside lifecycle lock. Process agent reports focused 33-test run. |
| 03-01, 03-03 | Outstanding: native Windows job-creation race and unresolved-command fallback policy. |
| 03-02, 03-04 | Fixed: relative PATH resolution and drive-rooted path handling; process agent reports focused 33-test run. Native Windows acceptance remains outstanding. |
| 04-01..03 | Fixed: C1 sequence handling and surrogate safety; output agent reports 124+41 focused tests. |
| 04-04..05, 04-07..11 | Outstanding: zero-width/text-width model, follow/selection identity, log recovery, and performance work. |
| 04-06 | Fixed via reverse text style and default-color sentinels; included in output focused tests. |
| 05-01, 05-03, 05-05 | Outstanding severe orchestration work: restart/shutdown authority, cancellation, and tree synchronization. |
| 05-02, 05-04 | Fixed: command target ownership validation and queued subgroup reservation; process agent reports focused 33-test run. |
| 05-06 | Outstanding API ordering contract decision. |
| 06-01..02, 06-05 | Outstanding; editor representation and copied shell contract remain undecided. |
| 06-03, 06-06 | Fixed: actual InputDialog focus/reveal/resize test covers 30 fields, a 100-character final label, and width 40; ANSI MainController status regression test covers hint restoration. UI agent reports both targeted tests passing. Narrow terminals can still have one-cell fields and clipped labels. |
| 06-04 | Duplicate of 01-05; fixed with that load-boundary work. |

Retain severe unresolved findings explicitly until implementation and acceptance checks are complete.
