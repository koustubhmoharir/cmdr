# 04 — Output pipeline and terminal display review

Reviewed commit: `a06d405` and the source present in the shared workspace. Review performed on Linux (Ubuntu 24.04, x64), .NET SDK 10.0.107 / runtime 10.0.7. No production code or tests were changed. This report is this reviewer's only repository write.

## Summary

Ten verified P2 findings concern escape stripping, Unicode preservation, tab alignment, inverse colors, following/selection behavior, log recovery, and allocation complexity. One P3 recommendation addresses measured redraw preparation costs. No P1 defect was established in this area. Additional hardening opportunities are explicitly separated from verified defects.

| ID | Severity | Verified finding |
| --- | --- | --- |
| 04-01 | P2 | C1 OSC/ST handling leaks payloads or discards following visible output |
| 04-02 | P2 | Forced line splitting divides UTF-16 surrogate pairs |
| 04-03 | P2 | Backspace removes half a supplementary Unicode character |
| 04-04 | P2 | Rendering and truncation discard combining marks |
| 04-05 | P2 | Tab expansion counts UTF-16 units instead of display columns |
| 04-06 | P2 | Inverse video disappears with terminal-default colors |
| 04-07 | P2 | Selecting a scrolled portion of the last wrapped line jumps to the tail |
| 04-08 | P2 | Partial-line positions change identity, silently changing selected/copied text |
| 04-09 | P2 | A failed log flush marks unwritten lines as already logged |
| 04-10 | P2 | Every small chunk copies the entire growing partial line |
| 04-11 | P3 | Recommendation: avoid materializing the whole line just to count wrapped rows |

Related findings owned by other reviewers are **not counted again here**: 02-02 (stale pumps contaminate a new run), 02-03 (undisposed redirected streams/pumps), 02-05 (kill blocked by synchronous logging), and 01-02 (colliding sanitized default log paths). In particular, 04-09 concerns the recovery cursor after failed writes, independently of log-path collisions or synchronous logging blocking process control.

## Functionality and scope

Read all files in `src/Cmdr.Core/Output/`: `AnsiLineParser`, `CommandOutput`, `OutputBuffer`, `OutputLine`, `StyledSpan`, `SpanStyle`, `AnsiColor`, `ColorKind`, `TextAttrs`, and `OutputSource`. Read the complete requested TUI files: `OutputView`, `OutputWrap`, `OutputScroll`, `DisplayLines`, `TextWidth`, `ColorMap`, `SelectionText`, and `Osc52`. Also read `EscapeText`, the relevant clipboard-controller and process-pump call sites, and the output/UI behavior descriptions in README/design documentation.

Read all dedicated core-output tests and helpers, and the complete `OutputWrapTests`, `OutputScrollTests`, `DisplayLinesTests`, `SelectionTextTests`, `ColorMapTests`, `Osc52Tests`, `TextWrapTests`, `StyledTextTests`, and ancillary `EscapeTextTests`; inspected the direct TextWidth truncation/fitting assertions in `RowLayoutTests`.

The pipeline maintains separate streaming ANSI parsers for stdout and stderr. Parsers strip non-SGR escape sequences, retain rendition state, expand tabs, handle CR/BS, expose partial lines, and force a logical break at the line-length limit. `CommandOutput` serializes writes, publishes completed lines, and optionally writes plain-text logs. `OutputBuffer` retains a bounded ring of completed lines plus stream partials. The view prepares cells and attributes, wraps by display width, scrolls by screen rows, follows the tail, and extracts selections using logical line/display-column coordinates. `DisplayLines` independently maps command-list rows to main/detail display lines. Clipboard output is UTF-8/base64 OSC 52 with a controller-side size guard.

The documented CR policy intentionally replaces the entire prior line on the next printable character. Dropped cursor-motion/erase operations and the newline recovery policy for unterminated strings are also intentional repository behavior; this review does not treat the output pane as a full terminal emulator.

## Verified defects

### 04-01 — P2: Complete C1 string-sequence handling

**Locations:** `src/Cmdr.Core/Output/AnsiLineParser.cs:130–165` (`ProcessNormal`); `:269–300` (`ProcessString`/`ProcessStringEscape`).

**Trigger and evidence:** Source-linked parser probes, with `\u001B` meaning ESC, `\u0007` BEL, `\u009D` C1 OSC, and `\u009C` C1 ST:

| Minimal input | Expected plain lines | Actual plain lines |
| --- | --- | --- |
| `a\u009D0;title\u0007VISIBLE\n` | `["aVISIBLE"]` | `["a\u009D0;titleVISIBLE"]` |
| `a\u001B]0;title\u009CVISIBLE\n` | `["aVISIBLE"]` | `["a"]` |
| `a\u0090payload\u009CVISIBLE\n` | `["aVISIBLE"]` | `["a"]` |

The parser recognizes C1 CSI and several C1 string introducers, but omits C1 OSC. Within a string it recognizes BEL and ESC-backslash but omits C1 ST. Consequently it retains an OSC payload as ordinary output, or continues swallowing visible text after a valid string terminator. The newline recovery eventually exits the string, after the visible text has been lost. This affects the buffer, log, and completed-line consumers. These inputs are .NET strings containing the indicated Unicode control characters, not arbitrary undecoded byte `0x9C` in a UTF-8 stream.

**Fix direction:** Recognize C1 OSC alongside the other string introducers, recognize C1 ST in string states, and consistently drop unsupported C1 controls outside sequences rather than retaining escape payload/control artifacts.

**Regression:** Assert the explicit expected outputs above, then feed each input at every split point and one UTF-16 unit per chunk. Existing split-invariance tests do not exercise these missing forms; comparing an unsplit baseline alone would preserve the same wrong result.

### 04-02 — P2: Preserve surrogate pairs at the forced line boundary

**Locations:** `src/Cmdr.Core/Output/AnsiLineParser.cs:65–68`, `:548–564`.

**Trigger:** A supplementary character straddles `maxLineLength`. A small-limit reproduction is `new AnsiLineParser(OutputSource.StdOut, maxLineLength: 3)` fed `ab\uD83D\uDE00X\n` (`ab😀X\n`). The default limit is also affected when the high surrogate falls at UTF-16 position 65,535.

**Evidence:** Actual lines are `["ab\uD83D", "\uDE00X"]`. UTF-8 encoding/decoding each line yields `["ab\uFFFD", "\uFFFDX"]`. The check occurs between individual UTF-16 units, so both output lines are ill-formed. A scalar-safe result for this limit is `["ab", "\uD83D\uDE00X"]`.

**Impact:** Valid process output becomes replacement characters in rendering, clipboard encoding, and plain logs. This is permanent corruption at the synthetic line boundary, independently of how input chunks are partitioned.

**Fix direction:** Make forced completion scalar-aware, retaining a pending high surrogate across chunks and splitting before a pair when necessary. Define behavior when the configured limit is smaller than one scalar's UTF-16 representation. Preserve style and avoid inserting a boundary inside the pair.

**Regression:** Exercise supplementary characters immediately before, across, and after the limit, both in one chunk and split between surrogates; verify every emitted line is valid UTF-16 and UTF-8 round trips without replacement. Include a default-limit case and a styled case.

### 04-03 — P2: Backspace must not leave a lone surrogate

**Location:** `src/Cmdr.Core/Output/AnsiLineParser.cs:534–545`.

**Trigger/evidence:** Input `\uD83D\uDE00\bX\n` (`😀\bX\n`) produces `"\uD83DX"`. Under the repository's existing delete-last-character BS policy, the expected result is `"X"`. `_text.Length--` removes only the low surrogate.

**Impact:** An ordinary backspace operation corrupts an otherwise valid supplementary character, introducing invalid UTF-16 and replacement characters downstream.

**Fix direction:** Remove a complete Unicode scalar and update run boundaries for the removed range. Separately define whether combining sequences should be removed as a whole; scalar integrity is the minimum required correction and does not require changing the documented BS policy into cursor emulation.

**Regression:** Backspace a supplementary character, including at a chunk boundary and after a style transition; assert valid UTF-16, correct remaining text/styles, and no empty runs. Keep the existing empty-line and pending-CR tests.

### 04-04 — P2: Zero display width does not mean text can be discarded

**Locations:** `src/Cmdr/Tui/OutputWrap.cs:30–36`; `src/Cmdr/Tui/TextWidth.cs:49–55`; rendering consumer `src/Cmdr/Tui/OutputView.cs:677–707`.

**Trigger/evidence:** Parse `e\u0301x\n`. `OutputWrap.Cells` supplies the renderer only `"ex"`, although the line contains `"e\u0301x"`. Character selection of column zero correctly copies `"e\u0301"`. Separately, `TextWidth.Truncate("e\u0301xyz", 3)` returns `"ex\u2026"`, instead of preserving the accent in `"e\u0301x\u2026"`.

**Impact:** Decomposed accents disappear from visible output and truncated labels. Visible and copied text disagree. The same unconditional zero-width discard can rewrite Unicode sequences containing joiners or variation selectors; no physical-terminal width claim is needed to establish the loss of their text.

**Existing coverage:** `tests/Cmdr.Tests/OutputWrapTests.cs:71–76` explicitly asserts that the combining mark is skipped. That assertion encodes the defect rather than protecting the stated “nothing is dropped” output behavior. `SelectionTextTests` already expects the accent to stay with its base.

**Fix direction:** Preserve trailing zero-width components as part of a drawable text/grapheme unit, with column accounting separate from its text. Keep those components attached through wrapping, style transitions, truncation, and selection. Define handling for leading combining marks and terminal-dependent grapheme widths.

**Regression:** Check drawn/prepared text as well as row counts for decomposed Latin accents, multiple combining marks, a wrap boundary, a style boundary, and truncation retaining the base. Add a joined emoji/variation-selector preservation case, with terminal-specific width expectations kept separate.

### 04-05 — P2: Expand tabs from display columns, not UTF-16 length

**Location:** `src/Cmdr.Core/Output/AnsiLineParser.cs:149–154`.

**Trigger/evidence:** `日\tX\n` produces `日` plus **seven** spaces plus `X`; `日` occupies two display columns, so `X` lands at column 9 instead of the next eight-column tab stop, column 8. Expected expansion is six spaces. Conversely `e\u0301\tX\n` produces six spaces after the base/mark sequence; expected expansion is seven, since that sequence occupies one column.

**Impact:** Unicode tables and aligned command output have incorrect spacing, also persisted in copied text and logs. A supplementary character can happen to have width two and UTF-16 length two, masking this error in some examples.

**Fix direction:** Track the current display column independently of stored UTF-16 length, using a width policy shared or deliberately aligned with rendering. Maintain it through CR, BS, tab expansion, and forced breaks without introducing a Terminal.Gui dependency into the core parser.

**Regression:** Expand tabs after ASCII, CJK, supplementary characters, and combining sequences, in one chunk and split chunks. Assert the following printable character's display column, not merely the stored character count.

### 04-06 — P2: Preserve inverse video when theme colors are terminal defaults

**Location:** `src/Cmdr/Tui/ColorMap.cs:67–74`; related selection inversion in `src/Cmdr/Tui/OutputView.cs:745–749`.

**Trigger/evidence:** Map `SpanStyle.Default with { Attrs = TextAttrs.Inverse }` against `new Attribute(Color.None, Color.None)`. The resulting attribute is `[None,None,None]`, identical to ordinary output. Swapping the same sentinel values cannot represent reverse video. Terminal.Gui's installed package documentation defines `Color.None` as using the terminal's default foreground/background; it is not a resolved concrete color.

**Impact:** SGR 7 is silently lost under terminal-default themes. The view's own selection-inversion comment already identifies why swapping terminal defaults is ineffective, but `ColorMap` still uses that approach for process output.

**Fix direction:** Represent SGR inverse using `TextStyle.Reverse`, or resolve actual terminal defaults before swapping. If the flag approach is used, selection inversion should toggle the flag so already-inverse text still gets visible selection feedback.

**Regression:** Cover normal colors `(None,None)`, `(explicit,None)`, `(None,explicit)`, and two concrete colors, including selection of inverse text. Current inverse tests use only concrete normal colors (`ColorMapTests.cs:91–103`).

### 04-07 — P2: Following must account for the wrapped row, not only the last logical line

**Locations:** `src/Cmdr/Tui/OutputView.cs:380–382`, `:424–426`, `:494–498`, `:535–541`; tail-position choice at `:330–335`.

**Trigger/evidence:** Source-linked `OutputView` probe, viewport **4 columns × 3 rows**, one partial line `abcdefghijklmnopqrst` (five screen rows):

1. Initial following position is `(line 0, row 2)`.
2. Wheel up three rows: following becomes false, top is `(0,0)`.
3. At that position, pointer `(x=1,y=1)` maps to logical column 5.
4. Begin a mouse selection there: `_following = point.Line == geometry.Last` becomes true, jumping the top back to `(0,2)`.
5. Move the pointer one cell right to `(2,1)`: active column is now **14**, rather than **6**. The selection expands over text displaced by the viewport jump.

In the same setup, Home leaves `Following == true` and the effective top at `(0,2)` despite storing `(0,0)`. Moving the cursor onto a tall last line can likewise override the first-row position chosen by `EnsureVisible`.

**Impact:** Selecting earlier portions of a long final line moves the content under the mouse and copies an unintended range. Home cannot expose the start of a single tall line. This is view-state behavior; the pure `OutputScroll` tests do not exercise it.

**Fix direction:** Keep following paused during selection and when the requested viewport precedes the tail, even if the cursor lies on the last logical line. Resume at an explicit End action or actual arrival at the tail. Reconcile cursor navigation's follow decision with the wrapped-row position returned by visibility calculation.

**Regression:** A view-level test with the geometry above must retain top `(0,0)` during press/drag and select columns 5–6. Cover Home on a single tall line and cursor movement onto a tall last line, plus End and wheel-to-bottom resumption.

### 04-08 — P2: Give partial lines stable identity or invalidate selections when identity changes

**Locations:** `src/Cmdr.Core/Output/OutputBuffer.cs:206–240` (completed lines followed by stream partials); `src/Cmdr/Tui/OutputView.cs:81–87`, `:577–582`, `:604–616` (positions/readback by absolute numeric index); completion/promotion in `src/Cmdr.Core/Output/CommandOutput.cs:248–256`.

**Trigger/evidence:** A deterministic view probe, no threads required:

1. `WriteStdOut("OUT-PARTIAL")`; `WriteStdErr("ERR-PARTIAL")`.
2. Select columns 0–2 of the stdout partial. Copy gives `"OUT"`.
3. `WriteStdErr("\n")` completes stderr first. Buffer order becomes `["ERR-PARTIAL", "OUT-PARTIAL"]`.
4. Copy the unchanged selection: it now gives **`"ERR"`**.

Partials do not have committed absolute line numbers; the view nevertheless derives their coordinates by adding their current index to `FirstLineNumber`. Completing another stream's line changes which content occupies that coordinate. Similar remapping occurs when stdout partials appear/disappear or system lines are appended.

**Impact:** A persistent selection/cursor silently switches to different output, making subsequent copying incorrect. The stdout line in the reproduction is still retained; this is not ordinary eviction of selected history.

**Fix direction:** Introduce stable identities for partial lines and remap positions when they move or become completed lines. At minimum invalidate affected selections on identity changes rather than silently reusing their coordinates. Keep completed-line absolute numbering and its eviction behavior.

**Regression:** Repeat the exact sequence above and assert selected text remains stdout, or that an explicit invalidation occurs. Cover both stream completion orders, appearance of a second partial, and a system-line append. Existing tests verify buffer ordering and completed-line number stability separately, missing the view's identity assumption.

### 04-09 — P2: Advance the logged cursor only after a successful flush

**Locations:** `src/Cmdr.Core/Output/CommandOutput.cs:309–320` (cursor advancement), `:324–338` (flush failure), `:187–199` (same-path backlog replay).

**Trigger/evidence:** Linux failure injection entirely under `/tmp`: create a log-path symlink to `/dev/full`; begin a run; enable logging to that path. The retained start marker fits in the StreamWriter buffer, `_nextUnloggedLine` becomes **1**, and `Flush` fails with ENOSPC. Logging is disabled. Remove the symlink and re-enable the **same configured path**, now backed by a normal file.

Expected recovery includes the retained start marker that never reached the log. Actual recovered file contains only the `log disabled: No space left on device ...` notice. The original marker remains in the buffer but is skipped because the cursor was committed before the failed flush. Retained process lines buffered in the same way follow the same code path.

**Impact:** Recovering from a temporary write/flush failure silently omits retained output. This is separate from the process-control blocking and log-path collision findings owned by other reviewers.

**Fix direction:** Distinguish staged writes from successfully flushed progress. Do not advance the replay boundary past an entirely failed batch. Define replay/deduplication policy for partial writes: unconditional exactly-once recovery cannot be inferred from a failed stream flush. A successful flush is an application-level boundary, not a claim of filesystem crash durability.

**Regression:** A failing stream or the Linux `/dev/full` reproduction should fail before any batch bytes persist; re-enable the same destination and assert all retained uncommitted lines are replayed. Add a separate partial-write case with an explicitly documented policy. Existing tests exercise log-open failures, not buffered write/flush failures.

### 04-10 — P2: Avoid quadratic copying of growing partial lines

**Locations:** `src/Cmdr.Core/Output/CommandOutput.cs:248–257`; `src/Cmdr.Core/Output/AnsiLineParser.cs:54–55`, `:590–600`; line materialization in `src/Cmdr.Core/Output/OutputLine.cs:19–46`.

**Trigger:** A command produces a long line in many small chunks. Every write evaluates `parser.Partial`, rebuilding immutable spans/text for the entire accumulated prefix, even though the UI polls only every 100 ms. Work therefore grows with the sum of prefix lengths rather than the amount of new input.

**Measured evidence:** A Release-mode temporary console project compiled the actual source files directly. For each row below it created a fresh `CommandOutput(10)`, wrote exactly **32,768 ASCII `a` characters**, with no newline, no logging and no event subscribers, using a reused string of the indicated chunk size. Allocation was the delta from `GC.GetAllocatedBytesForCurrentThread()` around the write loop. One captured run:

| Chunk size (UTF-16 units) | Number of writes | Allocated bytes during loop | Elapsed time |
| --- | ---: | ---: | ---: |
| 1 | 32,768 | 1,084,636,672 | 153.1 ms |
| 64 | 512 | 17,059,328 | 8.9 ms |
| 4,096 | 8 | 378,944 | 5.2 ms |

These are **cumulative allocation volumes, not retained heap sizes or peak resident memory**. Old partial snapshots are eligible for collection; this experiment does not establish a gigabyte of live memory or an out-of-memory condition. Timings are illustrative, not a controlled throughput benchmark: JIT/tiering, GC, and concurrent reviewers can affect them. The allocation scaling and full-prefix materialization are directly established. Input chunk delivery depends on pipe scheduling; the table uses explicit public write calls to isolate pipeline behavior.

**Impact:** Tiny-chunk output causes disproportionate memory churn and copying/GC work. The 65,536-unit line cap bounds each prefix, but does not remove the large amplification for repeated long lines. No separate process-control-blocking finding is claimed here; see 02-05 for that issue.

**Fix direction:** Decouple partial snapshot publication from every input chunk, or represent partial contents incrementally and materialize at UI snapshot cadence. Preserve immediate parser progress, correct completed-line notifications, bounded storage, and synchronization guarantees.

**Regression:** Add an allocation/performance check for equal total output delivered in one-character versus moderate chunks. Prefer a broad allocation-scaling bound over wall-clock assertions. Verify snapshot freshness at the intended publication cadence and completion correctness.

## Measured performance recommendation

### 04-11 — P3: Count rows without allocating all drawable cells and row ranges

**Locations:** `src/Cmdr/Tui/OutputWrap.cs:26–44`, `:47–52`; `src/Cmdr/Tui/OutputView.cs:92–99`, `:643–647`.

`RowCount` calls `Rows`, which builds a full `List<Cell>`, a separate list of widths, and row ranges. Geometry caches only the integer count for the current operation. Rendering then prepares cells/ranges again. A narrow viewport viewing only part of a long line still materializes that entire line to count its rows.

**Evidence/conditions:** In the same source-linked Release harness, create a plain ASCII line of **65,536 characters** with the parser, warm `OutputWrap.RowCount(line, 80)` three times, then call it **100 times**. The measured current-thread cumulative allocation was **132,906,672 bytes**, elapsed **801.3 ms** in one captured run. The allocation delta included negligible stopwatch/report bookkeeping. These are aggregate allocations across the calls, not retained memory; this was not a full-view or physical-terminal benchmark and does not by itself prove missed frames.

**Recommendation:** Implement a streaming count-only traversal, and consider sharing prepared immutable-line geometry across operations using line identity and viewport width. Attribute caches also need theme/source/style invalidation. Keep partial changes and resize behavior correct; avoid an unbounded cache.

**Regression:** Compare count-only results with actual row ranges over mixed narrow/wide/zero-width input and all small widths. Benchmark count-only allocations separately from preparation and drawing. Existing functional wrapping tests are a good oracle, after addressing 04-04.

## Strengths and maintainability

- The parser is an explicit bounded state machine, independent of the UI. It carries escape/CR/style state across chunks and bounds CSI parameter accumulation. String payloads are discarded incrementally rather than accumulated.
- ANSI tests thoroughly cover common SGR color forms, attributes/resets, CR/CRLF, malformed input recovery, and many single-character/every-split chunk partitions. The escape dropping policy is substantially tested.
- The ring buffer avoids front-removal costs, releases overwritten entries, clamps read ranges with widened arithmetic, and provides stable absolute identities for completed history. Buffer mutations are locked, and notification lock ordering/reentrancy constraints are documented.
- Pure wrap, scroll, selection, palette, and command-list mapping helpers make algorithmic behavior inspectable and easy to test. The binary search for row-start cells avoids repeatedly scanning the whole prefix while drawing wrapped rows.
- Selection extraction includes a partly selected wide character as a whole and keeps combining marks with their base. Wrapped output is copied from original logical lines instead of introducing display-wrap newlines.
- Color cube/grayscale mapping and OSC 52 UTF-8/base64 construction are straightforward and covered. The controller checks UTF-8 byte size before building the escape sequence; payload base64 prevents embedded input text from terminating the generated OSC sequence.
- `DisplayLines` main/detail mapping and ordinary tested scroll math showed no independently established defect. No incorrect OSC 52 sequence construction was found.

The largest extensibility concern is the mismatch between storage units, drawable units, and identity: parsing edits UTF-16 units, display uses runes with zero-width text removed, selection preserves zero-width components, and partial rows use unstable numeric positions. Explicit shared contracts for scalar/grapheme text, display columns, and stable line identity would simplify fixes across these layers. A core-facing width policy should remain independent of Terminal.Gui. `OutputView` also contains enough stateful behavior that pure helper tests cannot establish mouse/follow/copy correctness; a small view-state test seam would be valuable.

## Risks/recommendations, not additional verified defects

- **Snapshot consistency:** `OutputView.Geometry` reads `FirstLineNumber` and `Count` separately (`OutputView.cs:66–73`); `ReadLines` retries on eviction (`:604–618`), but line content, partial promotion, and geometry are not one atomic snapshot. `CommandOutput` also publishes completed lines and updates a partial in separate buffer mutations (`CommandOutput.cs:250–256`). Consider a versioned range/snapshot API and atomic completion/partial updates. Apart from the deterministic identity bug 04-08, this review did not establish a distinct persistent concurrency failure or retry-loop hang.
- **Copy preallocation:** `OutputView.cs:577–583` constructs all selected text before the controller checks `Osc52.Fits` (`MainController.cs:826–834`). A very large retained selection can therefore incur substantial scanning/string allocation only to be refused. Consider size-bounded extraction with UTF-8 accounting. No huge-selection stress or out-of-memory probe was performed.
- **Read-only exposure:** `OutputLine.Spans` returns its backing array (`OutputLine.cs:49–50`), as does `DisplayLines.Lines` (`DisplayLines.cs:58–59`). A caller can cast and mutate these despite the read-only/immutable-facing contracts. No repository consumer performing that mutation was found; hardening the public contract is a recommendation, not an observed application failure.
- **Retention budget:** Line-count bounds and the parser's per-line character cap are useful, but do not constitute a small byte budget for many long, heavily styled lines across many commands. Consider an optional byte/character budget if predictable memory use is a requirement. No retained-heap capacity experiment was performed.
- **Terminal-specific behavior:** Joined emoji, ambiguous-width characters, leading combining marks, one-column views containing wide characters, OSC 52 terminal size limits/policies, and multiplexer transport warrant explicit compatibility decisions. Pure byte/width tests do not establish their behavior on every terminal. No new defect is asserted solely from those possibilities.

## Missing coverage to prioritize

1. The concrete expected-output regressions for 04-01 through 04-09; especially view-level press/drag/follow and partial promotion/copy tests.
2. Unicode scalar validity across forced splits and BS, accent preservation through drawing/truncation, and tabs after text whose UTF-16 length differs from its display width.
3. Terminal-default colors in inverse/style/selection combinations; current inverse tests use concrete colors.
4. Buffered log-write/flush failures and recovery, rather than only log-open failures. Keep process/log-path concerns cross-referenced to 02-05 and 01-02.
5. Allocation scaling for partial publication and count-only wrap operations; multi-threaded snapshot tests with deterministic synchronization rather than timing assumptions.

## Tests performed and limitations

**Existing focused tests:** Invoked VSTest directly against existing build artifacts, without building in the repository or running the full suite:

- `Cmdr.Core.Tests.dll`, filter `FullyQualifiedName~Cmdr.Core.Tests.Output`: **182 passed, 0 failed, 0 skipped**.
- `Cmdr.Tests.dll`, filters for `OutputWrapTests`, `OutputScrollTests`, `SelectionTextTests`, `DisplayLinesTests`, `ColorMapTests`, `Osc52Tests`, `TextWrapTests`, `StyledTextTests`: **154 passed, 0 failed, 0 skipped**.

Results directories were under `/tmp/cmdr-review04-aYw1bK/`. These executions validate the existing binaries; the parent owns the full-suite/fresh-build validation. The direct source-linked probes independently compiled the reviewed production sources.

**Temporary probe project:** `/tmp/cmdr-review04-aYw1bK/Probe.csproj`, with `Program.cs`, `Stubs.cs`, and captured `probe-output.txt`. It linked the actual core-output sources plus `OutputView`, `OutputWrap`, `OutputScroll`, `TextWidth`, `ColorMap`, `SelectionText`, and `Osc52`, using Terminal.Gui 2.5.0. Small controller/session stubs supplied output ownership and captured copied text. Private view operations were invoked through reflection with explicitly assigned viewport geometry; production wrap/selection/follow code was unchanged. This establishes the reported state transitions without simulating a complete application input loop or driver.

Probes covered escaped C1 parser inputs, supplementary-character splitting/backspace, Unicode tabs, combining-mark cells/copy/truncation, inverse attributes with `Color.None`, wrapped-line Home/wheel/drag transitions, partial-line copy identity, log recovery via `/dev/full`, and the allocation loops described above. Probe results are reported as observed incorrect outcomes, not claimed passing regression tests. All generated projects, logs, restore/build artifacts, and probe results were outside the repository.

**Limits:** Linux execution only; Windows behavior was reviewed statically, with no Windows process/console execution. No interactive physical-terminal rendering, real OS clipboard readback, SSH/tmux session, native Windows driver, retained-heap profiler, or controlled end-to-end throughput benchmark was used. No full suite was duplicated. The findings rely on minimal reproductions and source evidence rather than speculative terminal behavior. Further investigation stopped at the coordinator's request; the unverified concerns above remain recommendations/limitations.
