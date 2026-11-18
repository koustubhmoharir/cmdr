# logvu design

logvu is a .NET 10 terminal log viewer for Windows and Linux, built alongside cmdr with the repository's Terminal.Gui fork. Each config opens one set of uncompressed files from one directory. The set represents a single chronological stream. Its files cover non-overlapping time ranges. The unit of display, navigation and search is a complete event, which may span many lines.

## Launch and configuration

`logvu <config.json>` opens the configured set. `logvu` opens a recent-config picker with an **Open file** action, including on first use. Remember a config after it opens successfully. Use logvu's own recent-file history. Resolve relative directories against the config file. Report invalid settings at the offending field. An empty matching set is a valid live view waiting for its first event.

Example for the numbered rotation described in this design:

```json
{
  "logsDirectory": "../Logs",
  "filePrefix": "SheetKraft",
  "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
  "activeFile": "SheetKraft.log",
  "format": "log4net-core"
}
```

- Require a directory, literal common prefix and whole-basename pattern. Both selectors must match. Enumerate regular files immediately inside the directory, skipping compressed files. The example admits SheetKraft1.log and excludes SheetKraftWeb.log. A pattern can also admit dated rolls, numbered extensions, or Serilog's day and sequence suffixes.
- `activeFile` is optional and identifies a fixed current filename. If supplied, validate that it belongs to the set. Date-named writers can create a new current member without a fixed active name.
- `format` defaults to content detection per file. Allow an explicit built-in or custom scheme. Additional settings cover the timezone for offset-free text (default local), recent index capacity and highlight rules. Validate expressions when loading the config and bound their execution time.

## Timeline and keyboard

The viewport contains one row per matching event, oldest at the top. Show **Time, Level, Source, Message**. Source is the logger name (log4net logger, Serilog `SourceContext`, or a slog `logger` attribute), falling back to the member filename when the event has none. Give the remaining width to the first-line message preview. Include the date where a page crosses days. Use text labels as well as severity colors. The status line shows Follow or Paused, active filters, search progress and relevant read errors.

Start in Follow with the newest matching row selected at the bottom. Pause freezes the reading position while the reader continues ingesting. Count newly arrived matching events in the status line. Growing the current multiline event does not count as another arrival.

| Key in the timeline | Behavior |
|---|---|
| Up / Down | Select the previous / next matching event. At the top or bottom visible row, shift the viewport one row to reveal the new selection. |
| Shift+Up / Shift+Down | Shift the viewport one matching row toward older / newer events. Keep the same event selected while it remains visible. If it leaves the viewport, select the nearest visible row. |
| PgUp / PgDn | Move one viewport of matching events, preserving the selected screen row where possible. |
| Space | Toggle Pause and Follow. Resuming jumps to the newest matching event. |
| End | Resume Follow and select the newest matching event. |
| / or Ctrl+F | Open search. Enter confirms. |
| n or F3 | Go to the next match in the active filter. |
| N or Shift+F3 | Go to the previous match in the active filter. |
| f | Open the filter editor. |
| Enter / Esc | Open selected event detail / return to the same selection and viewport. |

Manual navigation that changes the selection or viewport pauses Follow. At the start or end of available history, movement stops. Scrolling back to the bottom stays paused until an explicit resume. Opening detail pauses the timeline. Bind printable shortcuts in timeline focus only, so typing `f`, `n` or `/` in an editor enters text.

## On-disk formats and event boundaries

Ship the following schemes based on the adjacent SheetKraft repository. Paths below are relative to its root.

| Scheme | Event boundary and field mapping | Producer reference |
|---|---|---|
| log4net core | Level, date, bracketed thread, logger, separator and message. | `SheetKraftCore.net/SheetKraft.log4net.xml` |
| log4net web | Also capture version, user, source address and local address from the header. | `SheetKraftWebV3/Config/SheetKraftWeb.log4net.xml` |
| log4net scheduler | Bracketed timestamp, level and message. | `TaskScheduleRunner/TaskScheduleRunner.log4net.xml` |
| log4net date-first | Date, bracketed thread, level, logger and message. | `Programs/BankBalance/AxisBankBalance/App.config` |
| CodeKraft .NET | One Serilog JSON object per line. Map Timestamp and Level. Prefer RenderedMessage, otherwise render MessageTemplate using Properties, retaining the original template when rendering is incomplete. Preserve Exception, scope and structured exception details. | `SampleDotNetApp/appsettings.json`, `CodeKraft/dotnet/Logging.Serilog/Enrichers.cs` |
| CodeKraft Go | One slog JSON object per line. Map time, level and msg. Preserve arbitrary attributes, including top-level cause and stack or an error object with message, attrs, cause and stack. | `CodeKraft/golang/ckiecho/utils/log_handlers.go`, `CodeKraft/golang/ckiecho/utils/errors.go` |

For text, a scheme defines an anchored header expression with named captures, timestamp formats and the message capture. Append continuation lines, including blank lines, to that message. A complete header line in a live file makes a provisional event visible. Subsequent complete continuation lines extend the same event until the next header or end of the generation. For live JSON, hold a partial line until complete. Accept a final valid record without a trailing newline when reading a closed roll. Preserve malformed complete JSON lines and orphan text as inspectable raw entries with a parse indication. Retain unknown fields and original timestamp spelling in detail. Handle UTF-8, BOM, LF and CRLF, including characters and headers split across read blocks.

## Catalog, paging and memory

1. **Discover.** For each member, read its first event to obtain its start time and scheme. Store the file identity, current path, generation and first-event location/time. Keep empty members pending. Show a diagnostic for an unreadable start time and keep that member accessible in a clearly marked unknown-time group. Order dated members by first timestamp, with rotation order resolving equal starts when known (higher numbered backups are older in the example) and the filename otherwise. Preserve physical order within each file. Comparing timestamps across files uses their offsets or the configured timezone.
2. **Fill the first page.** Starting at the end of the newest member, read backward in blocks to find enough complete event boundaries to fill the viewport. Continue into the preceding member as needed. Reverse the collected locations for display. Use the same framing rules in both directions. A backward read that begins inside a stack trace continues to its header.
3. **Maintain recent history.** Keep a bounded in-memory ring of recent generation/byte-offset pairs, extents, normalized table fields and length-limited previews. Warm it from the tail as needed and update it with appends. Maintain a per-generation read cursor and the current provisional entry. Recent history is independent of filters.
4. **Page older history.** Read from the first or last displayed event using its generation and offset as the anchor. Crossing a boundary means opening the adjacent catalog member. Retain the visible rows independently of ring eviction. Fetch full content for detail and search on demand. Use bounded block buffers and capped previews even for exceptionally large events. Stream or page oversized detail values.
5. **Keep the UI responsive.** Perform file reads, filtering and searches off the UI thread. Cancel superseded work and discard stale results. Publish batches of changes to the visible page. Memory scales with file count, configured ring size and visible content. All catalog and index state is session-local.

## Rotation and retention

A generation denotes one incarnation of a file's contents. A row key is its session generation plus starting byte offset. Capture filesystem identity from the opened file and verify it before using offsets after a reopen. Use handles shared for read, write and delete on Windows. Directory notifications trigger reconciliation of the matching set. Also reconcile periodically and after watcher overflow. A transient missing active filename is normal during rotation.

| Generation | Before numbered rotation | After | Reader action |
|---|---|---|---|
| A | SheetKraft2.log | Deleted | Expire its disk locations. |
| B | SheetKraft1.log | SheetKraft2.log | Update the catalog path and keep its offsets. |
| C | SheetKraft.log | SheetKraft1.log | Drain outstanding writes from its read cursor and finalize its last event. |
| D | — | SheetKraft.log | Assign a fresh generation and read from offset zero. Add its first event to the catalog when available. |

Reconcile by identity across the whole set. Coalesce intermediate notifications and retry transient opens. Drain reachable generations in chronological order before publishing rows from a successor. Use read cursors and row keys to make repeated notifications idempotent. Detect observed truncation and start a fresh generation. If identity cannot be verified, invalidate the old offset and rediscover the file. If retention removes unread data before the reader can reach it, indicate the gap.

Keep paused previews and already-loaded detail readable through rotation. If a detail read needs bytes from a deleted generation, show **Source file expired**. Continue paging through surviving history. Opening a former pathname must never substitute content from its new occupant.

## Search, filter and detail

Search traverses full events in the active filter, including multiline messages, JSON field names and values, and stack frames. Start with case-insensitive literal search and offer regex mode. Next and previous navigate by matching event across page and file boundaries. Show a boundary indication when there are no further hits. Keep surrounding matching-filter rows visible. Opening a hit expands or scrolls to the matching detail content.

The filter editor combines source, member filename, normalized level, time interval, text and field-value predicates with AND. Offer **Filter to this value** on a detail field. Apply the same predicate to historical and incoming events. Use catalog boundaries to skip files outside a time interval, retaining candidates at equal-time boundaries. Stream other predicates on demand with progress and cancellation. Distinguish a scan in progress from a completed scan with zero matches.

JSON detail is an expandable tree of objects, arrays and scalar values. Surface the message and error summary on entry. Text detail lists named fields followed by the multiline message as one value. Provide raw view, path and byte offset in both cases.

Apply ordered field-scoped highlight patterns with named styles. On overlapping spans, the first configured rule wins. Give selection and search hits priority while keeping the selected row legible. Recognize .NET exception and inner-exception boundaries and Go cause arrays and stack strings. Emphasize exception messages, methods and source filename/line. Dim directories and configured framework namespaces or package prefixes. Keep all frames visible by default. Allow folding a consecutive run of framework frames with its count, and reveal a matching frame when navigating a search hit.

## Implementation and acceptance

Place parsing, framing, file discovery, rotation handling and queries in `src/Logvu.Core`. Put the CLI, picker, timeline, detail and input routing in `src/Logvu`. Reference the Terminal.Gui submodule with the same build properties as cmdr. Reuse the picker, search editor and display-width behavior where practical. Keep viewport and selection transitions testable without a terminal.

Verify the following before shipping:

- Open a config directly, reopen it from logvu's picker, and open the first config from an empty history. Confirm a similarly prefixed unrelated log and a compressed roll are excluded.
- Read representative fixtures for all listed schemes in both directions. Page through a multiline exception larger than a read block, an empty active file and a rotation boundary with equal timestamps. Check for skipped or repeated rows.
- With a three-row viewport, move the selection to the middle and press Shift+Up. The page moves toward older events and the same event stays selected on the bottom row. Press Shift+Up again and selection clamps to the new bottom row. Verify the mirror case, ordinary arrows at page edges, a single-row viewport and history boundaries.
- Exercise both search launch keys, both next keys, both previous keys and the filter key. Find a hit inside a folded frame in an older file, filter by a nested value, and confirm incoming matches respect the same filter.
- Pause on an entry in SheetKraft1.log, rotate, and open detail. Verify the original event now comes from SheetKraft2.log. Resume Follow and observe the final events from the former active file and the first from the replacement exactly once. Repeat with coalesced notifications, partial writes and expiry of the selected generation.
- Open a set much larger than the ring capacity. Verify initial reads are limited to discovery and page fill, memory stays bounded as history is traversed, a scan can be cancelled, and a paused selection survives ring eviction.
