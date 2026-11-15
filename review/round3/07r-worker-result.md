# Worker 07r handoff: TUI views and text rendering

Reviewed repository `/srv/devshare/projects/cmdr`, branch `review-fixes`, HEAD `a27a7ce`. This was a read-only review. No implementation or repository test changes were made, because the worker request explicitly prohibits them. No new design phase was started. The proposals below are the required deliverable B, not an implementation prerequisite.

Result: four P2 findings and one P3 helper-contract defect. No P1 was established. Existing targeted tests: 32 passed. Fresh source-copy probes reproduce the findings described below. Repository status and diff were clean before and after review.

## A. Findings, most severe first

### 07r-01 — P2: Selection validation and copying can observe different buffer states

- **Location at HEAD:** `src/Cmdr/Tui/OutputView.cs:742` and `:747` (validation followed by a separate read); related `:86`, `:389`, `:425`, and `:772` (independent metadata/content reads).
- **Defect:** A writer can move partial lines between selection validation and text extraction, so copying returns a different stream's text under the old coordinates.
- **Concrete failure:** Buffer contains stdout partial `AAA` at line 0 and stderr partial `BBB` at line 1. User selects stdout columns 0–2. `CopySelectionOrCursorLine` finishes `Revalidate()`. Before its `ReadLines`, the stderr pump completes `BBB\n`. The buffer now contains completed stderr `BBB` at 0 and stdout `AAA` at 1. The copy still reads line 0 and copies `BBB`. The selected stdout line remains retained. This is independent of the backward-pin defect below: the selected stdout line moved forward, which the pin finder handles if validation occurs after completion.
- **Evidence:** Fresh probe output: `copy interleaving expected AAA actual=BBB`. The probe invokes the actual validation and read helpers and inserts one real `CommandOutput.WriteStdErr` between the same fragments used by copy. It reconstructs the extraction from the captured selection. This is a deterministic execution of the legal interleaving, not a probabilistic concurrent stress run or an end-to-end clipboard test. Source inspection establishes that the actual copy path permits this schedule.
- **Related consistency exposure:** `Geometry` reads first/count separately and subsequently reads live lines. `Revalidate` reads metadata, looks up pins, and recaptures pins independently. `ReadLines` retries only when the first retained number changes; partial promotion without eviction leaves that number unchanged. In addition, `CommandOutput.cs:410` appends completed lines before `:414` replaces the corresponding partial, under separate buffer locks. Thus one range read can observe a publication intermediate containing the old partial as well as its completed form. These are the same snapshot/publication defect class, not extra counted findings. No retry-loop hang is claimed.
- **Confidence:** High, based on the concrete legal schedule and actual helper results; reproduction frequency under an ordinary terminal workload was not measured.
- **Suggested fix:** Give each UI operation one consistent buffer snapshot/range view, including version, completed boundary, first number, partial identities, and immutable lines. Resolve selection pins and extract against that same view. Publish a chunk's completed lines and replacement partial atomically. A second version check just before extraction still leaves a race unless validation and extraction share captured data. Keep snapshots bounded to the ranges needed; do not copy the whole retained buffer every frame.

### 07r-02 — P2: Completing the selected stderr partial loses the selection and changes the paused cursor's target

- **Location at HEAD:** `src/Cmdr/Tui/PartialPins.cs:39`; consumers `src/Cmdr/Tui/OutputView.cs:394` and `:402`.
- **Defect:** Pin lookup searches only the old coordinate and later coordinates, although stderr completion can move its line one position backward.
- **Concrete failure:** Write stdout `OUT-PARTIAL`, then stderr `ERR-PARTIAL`. Select stderr columns 0–2 on row 1. Complete stderr with `\n` while stdout remains partial. The retained order changes from `[OUT-PARTIAL, ERR-PARTIAL]` to `[ERR-PARTIAL, OUT-PARTIAL]`. Stderr keeps its identity but moves from line 1 to 0. Its selection is cleared. The paused cursor remains at 1 and now targets stdout, so a subsequent no-selection copy copies stdout.
- **Evidence:** Fresh view probe reports the stderr identity and both endpoint pins before completion, then `selection=; cursor=1; lines=2:ERR-PARTIAL|1:OUT-PARTIAL`. Pure lookup separately reports `backward pin: expected=0 actual=null`.
- **Confidence:** High; deterministic, no concurrency required.
- **Suggested fix:** Remove the monotonic-position assumption. Find the pinned identity in the captured completed/partial state in either direction, including at least the preceding partial position. An identity lookup in the snapshot avoids both directional assumptions and an unbounded sequence of live single-line reads. Cover stderr completing first and stdout disappearing through rewrite/backspace while stderr survives. Preserve the existing identity/prefix protection against rebinding to unrelated identical text.

### 07r-03 — P2: Zero-width graphemes in the info pane shift subsequent cells and clip text

- **Location at HEAD:** `src/Cmdr/Tui/DetailsView.cs:233`–`:234`; width adapter `src/Cmdr/Tui/DisplayWidthSetup.cs:37`; hit testing `src/Cmdr/Tui/DetailsSelection.cs:103`.
- **Defect:** Details rendering passes standalone zero-width graphemes to Terminal.Gui, whose drawing cursor advances by one, while wrapping, padding and hit testing count zero.
- **Concrete failure:** A description contains `a\u200Bbcdefghijkl` in a 12-column pane. Cmdr measures 12 columns and puts it on one row. Drawing consumes 13 driver cells: U+200B occupies the driver's column 1, `b` is at 2, and the final `l` is clipped. Mouse selection also maps columns to different characters than the driver's contents. Such characters can occur in descriptions, paths and argument/environment display text.
- **Evidence:** Fresh probe using Terminal.Gui 2.5.0 and actual `DetailsView.DrawRow`:
  - `Terminal.Gui zero-width AddStr columns: model=2 driver=3; cells=0:"a"|1:"\u200B"|2:"b"|3:" "`
  - `Details zero-width row cells=0:"a"|1:"\u200B"|2:"b"|3:" "|4:" "`
  - `Details edge modelWidth=12 lastExpected=l actualLast=k`
- **Library verification:** Read installed XML API docs and downloaded the package's exact source commit `ece73d25c37df9bb17cb0c0d2171abd4d98eeaff`. `Terminal.Gui/Drivers/Output/OutputBufferImpl.cs:199` segments strings into graphemes; `:537`–`:568` draws each grapheme and unconditionally increments `Col`, including width 0. `Terminal.Gui/Text/StringExtensions.cs:57` measures that grapheme as 0. Source: https://github.com/tui-cs/Terminal.Gui/blob/ece73d25c37df9bb17cb0c0d2171abd4d98eeaff/Terminal.Gui/Drivers/Output/OutputBufferImpl.cs
- **Confidence:** High for the actual driver buffer, clipping and coordinate mismatch. Physical-terminal output was not tested.
- **Suggested fix:** Render details through a grapheme/cell representation that explicitly handles standalone zero-width graphemes and places later cells at their measured columns, while retaining logical UTF-16 offsets for copy. The output pane already omits standalone zero-width cells under the documented policy. Reuse that policy without deleting combining components inside a nonzero-width grapheme. Add a real driver-buffer assertion, not just a measurement test.

### 07r-04 — P2: POSIX executable names can be interpreted as shell syntax

- **Location at HEAD:** `src/Cmdr/Tui/CommandLineFormatter.cs:133`, using `QuotePosix` at `:89`; copy choice confirmed at `src/Cmdr/Tui/MainController.cs:876`.
- **Defect:** The POSIX formatter treats program names as ordinary argument tokens and leaves reserved words and assignment words unquoted in command position.
- **Concrete failure:** Valid executable scripts named `if`, `do`, or `X=1` exist in a PATH directory. Cmdr can directly run them, but the copied commands are respectively `if`, `do`, and `X=1`. `/bin/sh` rejects the first two as syntax and treats the third as an assignment without running the executable.
- **Evidence:** Probe created executable scripts only under the permitted scratch directory, each printing `EXECUTED`, then ran copied forms through `/bin/sh` with that directory on PATH:
  - `if`: unquoted exit 2; forced quoted exit 0, stdout `EXECUTED`.
  - `do`: unquoted exit 2; forced quoted exit 0, stdout `EXECUTED`.
  - `X=1`: unquoted exit 0 with no stdout; forced quoted exit 0, stdout `EXECUTED`.
  Ordinary argument quoting separately round-tripped 22 tricky arguments exactly through `/usr/bin/printf` and `/bin/sh`, including empty strings, single quotes, substitutions, backticks, whitespace/control separators, glob syntax, Unicode and shell operators.
- **Confidence:** High.
- **Suggested fix:** Give POSIX command-position formatting a separate rule. Always quoting the program is the smallest robust rule for these syntax forms; alternatively explicitly quote reserved/assignment words. Keep existing quoting for arguments. Test actual execution of PATH executables with syntax-like names. The platform chooser consistently selects POSIX on Linux and PowerShell on Windows; no chooser defect was found. Windows formatter internals were not re-reviewed.

### 07r-05 — P3: Empty details extraction indexes an empty collection

- **Location at HEAD:** `src/Cmdr/Tui/DetailsSelection.cs:67`–`:70`.
- **Defect:** `Extract` clamps an empty input to origin and then enters its inclusive loop once, indexing `texts[0]`.
- **Concrete failure:** `DetailsSelection.Extract(Array.Empty<string>(), default)` throws `ArgumentOutOfRangeException`, rather than returning the empty selected text.
- **Evidence:** Fresh probe: `empty details extraction: ArgumentOutOfRangeException`.
- **Confidence:** High for the helper defect; no reachable production UI crash established.
- **Impact qualification:** `DetailsView.SelectedRange` returns null for the empty clamped range, so its current copy caller avoids this call. This is a latent helper-contract defect, not evidence that copying an empty pane currently crashes cmdr.
- **Suggested fix:** Return `string.Empty` when `texts.Count == 0` or the clamped range is empty. Add empty-extraction assertions alongside the existing empty-clamp test.

## B. Organization and pattern proposals, ranked

These are substantive prevention proposals, scoped to the reviewed area. Costs are rough and do not authorize edits in this read-only task.

| Rank / priority | Proposal and bug class prevented | Files affected | Change sketch | Cost / risk |
|---|---|---|---|---|
| 1 / high | One output read transaction per operation; atomic chunk publication. Prevents 07r-01 and the snapshot concern already recorded in round 1. | Core `OutputBuffer`, `CommandOutput`; TUI `OutputView`, `PartialPins` | Expose immutable, versioned read state with first/completed boundaries and partial identities. Perform geometry, pin resolution, hit testing/copy against captured state. Apply completed append plus partial replacement in one buffer mutation. Capture only requested ranges/tail metadata; never allocate the whole buffer per draw. | M–L / medium: output synchronization and eviction contracts need careful regression coverage. |
| 2 / high | Model a pinned endpoint by stable identity plus validity data, separate from its resolved coordinate. Prevents 07r-02 and repeated 04-08/F06-01 identity regressions. | `OutputView`, `PartialPins`, Core snapshot type; corresponding tests | Use an endpoint discriminated state for completed absolute position versus pinned partial identity/prefix. Resolve all endpoints together against one read state; allow either movement direction. A pure transition function returns updated cursor/selection/follow state. Table-test both streams' appearance, growth, removal, completion and eviction. | M / medium: preserves existing selection clipping and follow policy while replacing scattered mutable updates. The immediate backward-search correction is S. |
| 3 / high | Share drawable grapheme geometry between rendering and selection. Prevents 07r-03 and repeats of 04-04/04-05/forced-grapheme defects. | `TextWidth`, `OutputWrap`, `DetailsText`, `DetailsSelection`, `DetailsView`, `SelectionText`, `DisplayWidthSetup` | Keep each grapheme's logical offset, width, style and explicit draw text/policy together. Drawing uses measured cell positions, with standalone zero-width handling decided once. Keep word-wrap and terminal-wrap as different policies over those units. Add library-boundary tests asserting actual cell positions. | M / medium: offsets, style boundaries and word-wrap dropped spaces must remain intact. |
| 4 / high | Explicit shell target and command-token role. Prevents 07r-04 and the earlier copy-shell defects (06-05 and its follow-ups). | `CommandLineFormatter`; small chooser call sites in `DetailsText` and `MainController`; formatter tests | Replace ambiguous booleans at the boundary with a shell-target enum and keep program versus argument formatting separate. Centralize host-to-copy-target choice. Add POSIX execution round trips for both argv and command names. Retain the already established Windows behavior. | S / low: mostly a local formatter/seam change; avoid expanding Windows scope. |
| 5 / medium | Bound copy extraction before building a giant string. Prevents resource amplification identified as a round-1 concern; no new OOM is claimed here. | `SelectionText`, `DetailsSelection`, `OutputView`, `Osc52`; clipboard call boundary | Extract into a UTF-8-budget-aware result (`text` or `too large`) using the existing one-million-byte policy. Stop traversal/allocation at the boundary; preserve graphemes and current refusal message. Centralize the byte limit instead of depending on a final guard after materialization. | M / medium: newline accounting and replacement encoding must agree with OSC52. |
| 6 / medium | Exception-safe UI harness ownership and pure state tests plus real driver checks. Prevents hidden lifecycle failures and coverage gaps demonstrated by 07r-01/02/03. | `OutputViewHarness`, OutputView tests, Details tests; small view-state seam | Ensure partially constructed temp session/controller/view are disposed on factory failure. Add public-input-path tests for Shift+arrows and copy, and deterministic writer gates through the snapshot seam. Retain a few real Terminal.Gui driver-buffer checks for grapheme placement/styles. | S–M / low: test infrastructure plus a limited internal seam; no controller redesign needed. |

Prepared immutable-line geometry could additionally be cached with a bounded cache keyed by line snapshot identity/content revision and width. Do not key solely by logical `OutputLine.Identity`: a partial retains its identity while its contents grow. Existing operation-local row caches already avoid walking the entire buffer in normal tail rendering; no new whole-buffer-per-frame performance failure was established. This optimization is subordinate to read consistency, not a new defect or separate design project.

## Test-quality issues

1. **Asymmetric pin cases.** `OutputViewPartialSelectionTests.cs:30` selects stdout when stderr completes; `:48` completes stdout in place. `PartialPinsTests.cs:75` explicitly covers completion at a later number. None covers selected stderr completing backward while stdout is partial. Passing 19 pin/view tests therefore misses 07r-02.
2. **No concurrent operation boundary.** The output view tests mutate output between completed UI operations. They cannot exercise output changing between validation, geometry, pin capture and copy. `OutputViewHarness.SelectedText` reconstructs extraction itself instead of invoking the actual copy path; its reads are also separate. Use deterministic gates, not sleeps, and test the operation's single-snapshot contract.
3. **Details tests stop before drawing.** Tests thoroughly cover logical wrap/copy/hit testing and style mapping, but no in-scope Details test asserts actual `DetailsView` driver contents for zero-width graphemes. Pure width assertions cannot detect Terminal.Gui advancing differently (07r-03).
4. **Empty clamp is not empty extraction.** `DetailsSelectionTests.cs:112` asserts only origin/empty clamping; it does not call `Extract` on the same input. Add the latter for 07r-05.
5. **Some agreement tests share the implementation.** `DetailsSelectionTests.cs:188` compares `DetailsText.Wrap` with `WrapRows`, although `Wrap` directly delegates to `WrapRows`; it is not an independent wrapping oracle. `OutputWrapTests.cs:155` compares two paths that now share RowBreaker: useful as a consistency check, insufficient as independent proof of the breaking rule. Existing explicit expected row ranges should remain, and driver-buffer cases should supplement them.
6. **Styled-span assertion is narrower than its name suggests.** `StyledTextTests.cs:48` requires every output span's text to be contained in its original input part. That cannot represent a grapheme beginning in one style part and continuing in another: the documented rule gives the entire grapheme its first unit's style. Current Parts omit this case. Add cross-span combining/ZWJ examples with explicit grapheme/style expectations rather than applying that substring assertion indiscriminately.
7. **A coverage label overstates its case.** `ColorMapTests.cs:190`, `Background_indexed_color_is_applied`, supplies `AnsiColor.FromRgb`, not an indexed background. It tests RGB background application. Add an actual palette-background case if indexed background coverage is intended.
8. **Factory cleanup is incomplete on failure.** `OutputViewHarness.Create` allocates the session/controller/view before returning the IDisposable owner; exceptions during creation/layout bypass the owner's Dispose. No leak was injected or observed; this is a concrete test-helper ownership weakness, handled by proposal 6 rather than counted as a product finding.
9. **No flaky timing dependence found in the reviewed output/details/POSIX tests.** Allocation tests warm the width cache and use allocation counters rather than elapsed-time thresholds. Terminal.Gui view tests use the serial collection. Windows-only and PowerShell-process tests were outside the requested POSIX review and were not executed as Windows evidence.

## Commands run and outcomes

All generated files, copied source, builds, tests, executable fixtures and fetched dependency source are under `/tmp/cmdr-claude-run-review2/probe-07r/`, except this explicitly requested handoff. The earlier `/probe-07/` was read only.

- Read worker request; used `pwd`, `rg --files` for guidance/scope discovery, `cat`, `sed -n` and `rg -n` to read README, design, review history/tracker, every scoped production and matching test file, and relevant Core/caller code. No applicable repository AGENTS.md was found by discovery. Read-only git commands: `git status --short`, `git rev-parse --short HEAD`, `git diff --stat`, `git diff --exit-code`. HEAD was `a27a7ce`; status/diff stayed empty, exit 0.
- Created scratch `setup.py` with a heredoc. First attempted `python setup.py`: failed because `python` is unavailable. Retried as `python3 .../setup.py`: succeeded. The script copied current Cmdr and Core source without bin/obj, copied Directory.Build.props, and created a probe project referencing those copies. No pre-existing binary is relied on for findings.
- `dotnet run --project /tmp/cmdr-claude-run-review2/probe-07r/Probe07r.csproj -p:NuGetAudit=false` succeeded, compiling fresh source. Initial probe confirmed pin, shell syntax and empty-helper leads, rejected the idea that ordinary U+200B removal alone establishes a new output-pane bug, and exercised the copy interleaving.
- `python3 .../library-source.py` fetched primary dependency source through GitHub's tree/raw endpoints at the exact package repository commit. Relevant OutputBufferImpl and StringExtensions sources were downloaded/read. Optional further fetch was interrupted with Ctrl+C (tool session exit 130); the completed local files were sufficient. `stop-source-fetch.py` checked for the exact fetch process; it found no PID to kill. Installed Terminal.Gui XML documentation was read locally.
- `python3 .../copy-tests.py` copied the existing UI tests without bin/obj. Ran only this targeted filter, on the unchanged source/test copies to keep all build artifacts outside the repository:

  ```sh
  dotnet test /tmp/cmdr-claude-run-review2/probe-07r/tests/Cmdr.Tests \
    --filter 'FullyQualifiedName~OutputViewPartialSelectionTests|FullyQualifiedName~PartialPinsTests|FullyQualifiedName~CommandLineFormatterTests.Posix|FullyQualifiedName~DetailsSelectionTests.Clamp_gives' \
    -p:NuGetAudit=false
  ```

  Outcome: **32 passed, 0 failed, 0 skipped**, reported test duration 459 ms. These are existing tests exposing coverage gaps; they do not assert the new failure scenarios. Full suite was not run. Log: `probe-07r/targeted-tests.txt`.
- Added scratch probe cases through `extend-probe.py` and `final-probes.py`. Each iteration ran `dotnet run --project .../Probe07r.csproj --no-restore -p:NuGetAudit=false`; both exited 0. They confirmed driver/detail cell mismatch and clipping, actual PATH executables named like POSIX syntax, and exact tricky-argument round trips. Final build emitted one CA1416 warning because a Linux-only scratch fixture calls `File.SetUnixFileMode` without a platform guard. No production warning/failure is inferred. Final output: `probe-07r/probe-output.txt`.
- After probe/test runs, used `pgrep -fa` checks (including `cmdr-config-tests` and probe names) and scratch process-filter scripts. Initial broad matching also listed the sandbox wrapper containing the command text, which was not a child to kill. Final `python3 .../verify.py` filters that wrapper and reports **no probe/test child processes remain; no cleanup kill needed**.
- Final verification compared every copied source/project/test file, excluding generated bin/obj, with the repository: **170 files checked, 0 differences**. This includes the application and Core sources used by the probe. Final `git diff --exit-code` succeeded; `git status --short` was empty.

## Open questions, accepted behavior and remaining limitations

- **U+200B output lead:** Dropping standalone zero-width graphemes from output cells is explicitly documented in `docs/design.md` and the completed fixes; the probe confirms it. It is not re-reported as a new defect. The distinct info-pane mismatch is 07r-03. The leading isolated combining-mark policy is likewise documented. No physical-terminal shaping claim is made.
- **Empty-details lead:** Confirmed only as the P3 helper defect above; the current view guard prevents the alleged empty-pane copy crash.
- **No ambiguous product intent was assumed to justify a finding.** Details text changing in place can preserve/clamp logical selection offsets onto new text; whether edits should instead clear selections remains a product question. Investigation did not assume a different policy or count it as a defect.
- Copied POSIX commands intentionally contain program/args only: cwd and environment are displayed separately, and placeholders are not prompted by copy. No full launch-reproduction promise was invented. If such a promise is desired, specify it before expanding formatter scope.
- No new issue was established in DisplayLines navigation, ordinary OutputScroll rules, palette/truecolor mapping, inverse selection, EscapeText round trips, OSC52 UTF-8/base64 construction or its controller size check. OSC52 delivery/size support still depends on the terminal/multiplexer as already recorded. No physical clipboard or native Windows tests were run.
- Core was followed only for buffer ordering/identity/publication, parser grapheme/forced-break behavior, and width contracts needed by the views. This is not another review of process or Windows command launching.
- Large-selection extraction and bounded geometry reuse remain prevention/performance recommendations, not measured OOM/hang findings. No sustained memory/handle or whole-buffer latency claim is made.

## Recommended next step

Have the coordinator triage 07r-01 through 07r-04 for narrowly scoped fixes and regression tests, with 07r-05 as a small helper repair. In particular, fixing backward search alone does not fix the validation-to-copy race. Keep Core publication changes coordinated with the output owner, and include actual Terminal.Gui cell assertions for the info-pane correction. The existing source is ready for that follow-up; this read-only review intentionally leaves all findings unfixed.

## Files changed in the repository

none
