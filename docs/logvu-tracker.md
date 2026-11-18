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

- [ ] Block reader: UTF-8 with/without BOM, LF/CRLF, characters and lines split across blocks; forward and backward line
      iteration over byte offsets.
- [ ] Text scheme model: anchored header regex with named captures, timestamp formats, message capture; continuation lines
      (including blank) appended; orphan text → raw entry.
- [ ] Built-in log4net schemes: core, web, scheduler, date-first. Level normalization.
- [ ] JSON schemes: Serilog JsonFormatter (RenderedMessage else MessageTemplate rendering over Properties, Exception,
      ExceptionDetail, Scope) and Go slog (time/level/msg, cause/stack, error object). Malformed lines → raw entries.
- [ ] Event framer in both directions with identical results; backward start inside a stack trace walks to its header;
      partial last line held in live files, accepted in closed rolls.
- [ ] Content detection per file; custom schemes from config.
- [ ] Fixtures for every scheme (incl. event larger than a block); forward/backward equivalence tests.

## M2. Configuration

- [ ] Config model and loader: logsDirectory (relative to config), filePrefix, fileNamePattern, activeFile, format,
      timezone, ring capacity, highlight rules, custom schemes; errors name the offending field; regex timeouts.
- [ ] File set enumeration: regular files directly in the directory, both selectors, compressed files skipped.
- [ ] logvu recent-file history (own store).

## M3. Catalog

- [ ] Discovery: first event per member → start time and scheme; identity, path, generation; pending empty members;
      unknown-time group with diagnostic.
- [ ] Ordering: by first timestamp with offsets/timezone; rotation order on equal starts; physical order within a file.

## M4. Paging and memory

- [ ] Row keys (generation + offset); first-page fill reading backward across members.
- [ ] Recent-history ring (bounded; offsets, extents, normalized fields, capped previews), independent of filters.
- [ ] Page older/newer history from an anchor row across member boundaries; visible rows survive ring eviction.
- [ ] On-demand full event fetch with bounded buffers; oversized values streamed/paged.
- [ ] Background worker: off-UI reads, cancellation of superseded work, batched publication.

## M5. Live tail and rotation

- [ ] Per-generation read cursor; provisional current event grows without counting as a new arrival.
- [ ] Directory watcher + periodic reconciliation + overflow handling; coalescing; transient-open retry.
- [ ] Reconcile by identity: rename (keep offsets), delete (expire), new file (fresh generation), truncation (fresh
      generation), unverifiable identity (rediscover). Drain predecessor before successor.
- [ ] Retention gap indication; "Source file expired" for deleted generations.
- [ ] Windows share modes read/write/delete *(Windows)*.

## M6. Timeline model (UI-free)

- [ ] Viewport/selection state machine: Up/Down, Shift+Up/Down, PgUp/PgDn, End, history boundaries, single-row viewport.
- [ ] Follow/Pause: manual navigation pauses; resume jumps to newest; new-arrival counter.
- [ ] Tests incl. the three-row Shift+Up acceptance scenario and its mirror.

## M7. TUI shell and timeline

- [ ] Program: `logvu <config>`, `logvu` → picker with **Open file** (works with empty history), remember on success.
- [ ] Timeline view: Time/Level/Source/Message columns, day crossing dates, level colors + labels, selection.
- [ ] Status line: Follow/Paused, new count, filters, search progress, read errors.
- [ ] Key routing: printable shortcuts in timeline focus only.

## M8. Detail view

- [ ] Text detail: named fields, multiline message value, raw view, path and byte offset.
- [ ] JSON detail: expandable tree, message and error summary surfaced, raw view, path and offset.
- [ ] Highlight rules: ordered, field-scoped, named styles, first rule wins; selection/search priority.
- [ ] Stack traces: .NET exception/inner boundaries, Go cause/stack; emphasize messages, methods, file:line; dim
      directories and framework namespaces; fold framework-frame runs with a count.
- [ ] Esc returns to the same selection and viewport; opening detail pauses.

## M9. Search

- [ ] Search editor (shared SearchBar): literal case-insensitive and regex; `/`, Ctrl+F; n/F3, N/Shift+F3.
- [ ] Traverse full events in the active filter across pages and files; boundary indication; cancel; progress.
- [ ] Opening a hit scrolls/expands detail to it, including inside folded frames.

## M10. Filter

- [ ] Predicates: source, level, time interval, text, field value; AND; same predicate for history and incoming.
- [ ] Time-interval file skipping via catalog boundaries (keep equal-time candidates).
- [ ] Filter editor (`f`); **Filter to this value** from detail; scanning vs zero-match distinction.

## M11. Acceptance and docs

- [ ] Run the design's acceptance list end to end (pty driver for TUI checks).
- [ ] README section / logvu README, publish script entry.
- [ ] Windows checklist for the user *(Windows)*.

## M12. Refactoring round (after logvu is done)

- [ ] Replace cmdr's copies with the shared components; generalize where the two apps differ. (`cmdr:` / `core:`)
