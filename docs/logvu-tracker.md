# logvu implementation tracker

Remaining work for [logvu-design.md](logvu-design.md). **When an item is done, tick it and name the commit.** Work proceeds in
milestone order; each milestone ends with a commit (prefix `logvu:`, `cmdr:` or `core:`). Items marked *(Windows)* can only be
verified by the user on Windows.

## M0. Scaffolding and shared code

cmdr is not touched until M12. Shared code starts as copies adapted from cmdr, generalized only as far as logvu needs.

- [x] `src/Shared.Core` (no Terminal.Gui): DisplayWidth, RecentFiles (store folder per app), SearchPattern, SearchHistory,
      FileIdentity. Tests in `tests/Shared.Core.Tests`. (`core:`)
- [x] `src/Shared.Tui`: SearchBar (+SearchColors, SearchTarget), StatusLine, Confirm, NumberTyping, DisplayWidthSetup,
      TextWidth, a recent-file picker generic over "open this path" (moved to M7). SearchBar has no shared tests yet (cmdr's need its session); covered by logvu's in M9. (`core:`)
- [x] Create `src/Logvu.Core`, `src/Logvu`, `tests/Logvu.Core.Tests`, `tests/Logvu.Tests`; add to `Cmdr.slnx`; Terminal.Gui
      reference with `TerminalGuiBuildProperties`; `logvu --version`. (`logvu:`)

## M1. Decoding and framing (Logvu.Core)

- [x] Block reader: UTF-8 with/without BOM, LF/CRLF, characters and lines split across blocks; forward and backward line
      iteration over byte offsets.
- [x] Text scheme model: anchored header regex with named captures, timestamp formats, message capture; continuation lines
      (including blank) appended; orphan text → raw entry.
- [x] Built-in log4net schemes: core, web, scheduler, date-first. Level normalization.
- [x] JSON schemes: Serilog JsonFormatter (RenderedMessage else MessageTemplate rendering over Properties, Exception,
      ExceptionDetail, Scope) and Go slog (time/level/msg, cause/stack, error object). Malformed lines → raw entries.
- [x] Event framer in both directions with identical results; backward start inside a stack trace walks to its header;
      partial last line held in live files, accepted in closed rolls.
- [x] Content detection per file (SchemeSniffer); custom schemes are wired in M2 (TextScheme ctor validates).
- [x] Fixtures for every scheme (incl. event larger than a block); forward/backward equivalence tests.

## M2. Configuration

- [x] Config model and loader: logsDirectory (relative to config), filePrefix, fileNamePattern, activeFile, format,
      timezone, ring capacity, highlight rules, custom schemes; errors name the offending field; regex timeouts.
- [x] File set enumeration: regular files directly in the directory, both selectors, compressed files skipped.
- [x] FileSet: exclude FIFOs/devices (Linux only) (use FileIdentity.TryGet(path), which only accepts regular files).
- [x] logvu recent-file history (own store).

## M3. Catalog

- [x] Discovery: first event per member → start time and scheme; identity, path, generation; pending empty members;
      unknown-time group with diagnostic.
- [x] Ordering: by first timestamp with offsets/timezone; rotation order on equal starts; physical order within a file.

## M4. Paging and memory


- [x] Row keys (generation + offset); first-page fill reading backward across members (HistoryReader.ReadNewest).
- [x] Recent-history ring (bounded; offsets, extents, normalized fields, capped previews), independent of filters.
- [x] Page older/newer history from an anchor row across member boundaries (HistoryReader).
- [x] Immutable CatalogSnapshot so history scans run off the store worker; HistoryReader over snapshots.
- [x] Visible rows survive ring eviction (page holds its own rows).
- [x] On-demand full event fetch with bounded buffers (LogStore.ReadDetailAsync, 4 MiB cap).
- [x] Oversized detail values: store caps at 4 MiB with a truncation notice; the view wraps lazily.
- [x] Background worker: off-UI reads, cancellation of superseded work, batched publication.

## M5. Live tail and rotation

- [x] Per-generation read cursor; provisional current event grows without counting as a new arrival.
- [x] LogStore: worker, directory watcher (coalesced) + 2 s rescans + overflow, 250 ms tail polls, async reads over snapshots (transient-identity retry is in Reconciler).
- [x] Reconcile by identity (Reconciler; identity includes birth time on Linux, plus a 256-byte content fingerprint): rename (keep offsets), delete (expire), new file (fresh generation), truncation (fresh
      generation), unverifiable identity (rediscover). Drain predecessor before successor.
- [x] A file shrinking mid-read surfaces as EndOfStreamException (an IOException), recorded by the readers' IOException handlers (ShrinkingStreamTests).
- [x] Retention gap indication (RetentionGap in ChangeBatch).
- [x] "Source file expired" for detail reads of deleted generations.
- [ ] Windows share modes read/write/delete *(Windows)*: covered by docs/logvu-windows-testing.md §1.

## M6. Timeline model (UI-free)

- [x] Viewport/selection state machine: Up/Down, Shift+Up/Down, PgUp/PgDn, End, history boundaries, single-row viewport.
- [x] Follow/Pause: manual navigation pauses; resume jumps to newest; new-arrival counter.
- [x] Tests incl. the three-row Shift+Up acceptance scenario and its mirror.

## M7. TUI shell and timeline

- [x] Program: `logvu <config>` (config errors → stderr, exit 1), remember on success.
- [x] `logvu` → picker with **Open file** (works with empty history): generic recent picker in Shared.Tui (faithful copy of cmdr's).
- [x] Timeline view: Time/Level/Source/Message columns, day crossing dates, level colors + labels, selection.
- [x] Status line: Follow/Paused, new count, filters, search progress, read errors.
- [x] Key routing: printable shortcuts in timeline focus only.

## M8. Detail view

- [x] Text detail: named fields, multiline message value, raw view, path and byte offset (DetailDocument).
- [x] JSON detail: expandable tree, message and error summary surfaced, raw view, path and offset.
- [x] Highlight rules: ordered, field-scoped, named styles, first rule wins; selection/search priority.
- [x] Stack traces (DetailDocument/StackTraceAnalyzer; the view comes next): .NET exception/inner boundaries, Go cause/stack; emphasize messages, methods, file:line; dim
      directories and framework namespaces; fold framework-frame runs with a count.
- [x] DetailView: render/wrap/navigate the document, toggles (Enter/Space on nodes and folds, z, r), Esc returns to the same selection and viewport; opening detail pauses.

## M9. Search

- [x] Engine: IRowFilter gets the event text; LogStore.FindAsync (older/newer, filter AND pattern, live row, progress, cancel, TimedOut); TimelineModel.JumpTo.

- [x] Search editor (shared SearchBar): literal case-insensitive and regex; `/`, Ctrl+F; n/F3, N/Shift+F3.
- [x] Traverse full events in the active filter across pages and files; boundary indication; cancel; progress.
- [x] Opening a hit scrolls/expands detail to it, including inside folded frames.

## M10. Filter

- [x] Predicates: source, level, time interval, text, field value; AND; same predicate for history and incoming (EventFilter; filtered arrivals refill from the changed row).
- [x] Time-interval file skipping via catalog boundaries (keep equal-time candidates).
- [x] Filter editor (`f`, Shift+F clears); **Filter to this value** from detail (DetailLine.Value); scanning vs zero-match distinction.

## M11. Acceptance and docs

- [x] Perf: row-level predicates are pre-checked on the header line before the event is parsed.
- [x] Startup: Release build shows rows in 0.8–1.0 s (cmdr 0.6–0.8 s); 253 MB set: rows within 1.5 s, RSS 67–83 MB while paging and searching.
- [x] Scan throughput: literal search 18 → 112 MB/s (log4net), 21 → 209 MB/s (slog); exact pre-checks, span framing, larger batches.
- [ ] Level-only filter scans ≈ 30 MB/s (two compiled-regex calls per event). Option: a hand-written header matcher for the built-in schemes, verified against the regex by randomized equivalence tests.

- [ ] Run the design's acceptance list end to end (pty driver for TUI checks).
- [x] User guide (docs/logvu.md), README pointer, publish script entry.
- [x] Windows checklist for the user: docs/logvu-windows-testing.md with tools/logvu/log4net-writer.ps1.
- [ ] *(Windows)* User runs docs/logvu-windows-testing.md and reports results.

## Notes for M12

- Shared SearchBar raises FocusPaneRequested for Enter, Tab and Shift+Tab alike; logvu runs the search on all three. Consider distinguishing them when generalizing.

## M12. Refactoring round (after logvu is done)

- [ ] Replace cmdr's copies with the shared components; generalize where the two apps differ. (`cmdr:` / `core:`)
