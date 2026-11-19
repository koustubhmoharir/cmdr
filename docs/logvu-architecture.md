# logvu architecture notes

Implementation notes that refine [logvu-design.md](logvu-design.md). Sections are added as milestones start.

## Lines (M1)

Files are UTF-8. Lines are split on the `\n` byte before decoding: that byte never occurs inside a UTF-8 multi-byte sequence, so a
character split across read blocks never splits a line, and decoding happens per complete line. A line is identified by byte
offsets, so offsets are exact in both directions.

```csharp
public readonly record struct RawLine(long Start, long End, bool Terminated, string Text, bool Truncated);
```

- `Start` is the first byte, `End` the byte after the line including its `\n` (or the end of data for an unterminated line).
- `Text` is the decoded content without `\r\n` / `\n`, cut to a prefix cap (`LineReader.TextCap`, default 8 KiB of bytes,
  cut at a character boundary). `Truncated` says the cut happened. Framing only needs the prefix; full text is read on demand
  from the extent.
- A UTF-8 BOM at offset 0 is skipped: the first line starts at offset 3. Invalid UTF-8 decodes with replacement characters.
- `LineReader` works on a `Stream` (seekable, shared-read) with a fixed block size (default 64 KiB; tests use tiny blocks):
  - `ReadForward(long from, long limit)` yields lines starting at `from` (which must be a line start) while `Start < limit`.
    The last line is yielded with `Terminated = false` if no `\n` follows before `limit`.
  - `ReadBackward(long before)` yields lines in reverse order, the first being the line that ends at `before` (`before` must
    be a line end or the data end). An unterminated tail before `before` is yielded first with `Terminated = false`.
  - Memory is bounded by the block size plus the text cap, whatever the line length.

## Events (M1)

An event is a run of lines `[Start, End)` in one file. `HeaderEnd` is the end of its first line.

- **Text schemes.** An event starts at a header line (anchored regex match) and includes every following non-header line,
  blank lines included. Lines before the first header in a file form one **orphan** raw event.
- **JSON schemes.** Each non-blank line is one event. A line that is not a JSON object, or does not parse, is a raw event with
  a parse error. Blank lines belong to the preceding event (or to an orphan run at file start).
- **Live vs closed.** In a live (still written) generation an unterminated last line is held back: it is not part of any
  event. In a closed roll it is accepted. In a live text file the last event is **provisional**: further continuation lines
  can extend it.
- Framing forward from 0 and backward from the end must yield exactly the same extents. Backward framing that starts inside a
  multi-line event walks back to its header (or to file start, giving the orphan event).

```csharp
public readonly record struct EventExtent(long Start, long End, long HeaderEnd, bool Provisional, bool Orphan);
```

## Parsed events (M1)

```csharp
public enum LogLevel { Unknown, Trace, Debug, Info, Warn, Error, Fatal }

public sealed class LogEvent
{
    DateTimeOffset? Time;      // null when absent or unparseable
    string? TimeText;          // original spelling
    LogLevel Level; string? LevelText;
    string Message;            // full message (text: first-line message + continuation lines joined with \n)
    IReadOnlyList<LogField> Fields;   // named groups in pattern order, `message` included (text) or top-level members (JSON)
    JsonElement? Json;         // the parsed object for JSON events
    string? ParseError;        // set for raw events
    bool IsRaw;
}
public sealed record LogField(string Name, string Value);
```

Level normalization (case-insensitive): `TRACE|VERBOSE|ALL|FINEST → Trace`, `DEBUG|FINE → Debug`, `INFO|INFORMATION|NOTICE →
Info`, `WARN|WARNING → Warn`, `ERROR → Error`, `FATAL|CRITICAL|EMERGENCY|ALERT|SEVERE → Fatal`. slog offsets (`INFO+2`,
`DEBUG-4`) map to the base level. Anything else is `Unknown` with `LevelText` kept.

Timestamps without an offset are interpreted in the configured timezone (default local). Fractions longer than 7 digits
(Go's RFC 3339 nano) are truncated to 7 before parsing.

### Built-in schemes

| Name | Header (anchored) | Time format |
|---|---|---|
| `log4net-core` | `LEVEL date [thread] logger - message` | `yyyy-MM-dd HH:mm:ss,fff` |
| `log4net-web` | `LEVEL date v.version u.user sourceAddress localAddress [thread] logger - message` | same |
| `log4net-scheduler` | `[date] LEVEL message` | `yyyy-MM-dd HH:mm:ss` |
| `log4net-date-first` | `date [thread] LEVEL logger - message` | `yyyy-MM-dd HH:mm:ss,fff` |
| `serilog-json` | Serilog `JsonFormatter`: `Timestamp`, `Level`, `MessageTemplate`, `RenderedMessage?`, `Properties`, `Exception?` | ISO 8601 with offset |
| `slog-json` | Go `slog.JSONHandler`: `time`, `level`, `msg`, arbitrary attributes | RFC 3339 nano |

log4net renders a missing property as `(null)`; `%-5level` pads the level with spaces.

Serilog message: `RenderedMessage` if present; otherwise render `MessageTemplate` with `Properties`: `{Name}`, `{Name:fmt}`,
`{Name,align}`, `{@Name}`, `{$Name}`, positional `{0}`; `{{`/`}}` are literal braces. Strings render unquoted, other scalars
as their JSON text, objects/arrays as compact JSON. A token whose property is missing stays as written. Fields are the top-level members; `Exception` (a .NET `ToString()` with stack trace) is one of them, and
`Properties.ExceptionDetail` / `Properties.Scope` are reached through `LogEvent.Json`.

slog: errors appear either as top-level `cause` (array of strings or `{message, attrs}` objects) and `stack` (array of
`function (file:line)` strings), or as an attribute whose value is an object `{message, attrs?, cause?, stack?}`.

### Detection

When `format` is not configured, the first up to 20 non-blank lines of a file are tried against each scheme. JSON schemes are
chosen when the first non-blank line parses as an object: `serilog-json` if it has `Timestamp` or `MessageTemplate`, `slog-json`
if it has `time` or `msg`. Otherwise the text scheme whose header matches the most lines wins (ties in table order); no match
leaves the file undetected (retried as it grows).

## Configuration (M2)

```json
{
  "logsDirectory": "../Logs",
  "filePrefix": "SheetKraft",
  "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
  "activeFile": "SheetKraft.log",
  "format": "log4net-core",
  "timeZone": "local",
  "recentCapacity": 20000,
  "schemes": [ { "name": "my-app", "header": "(?<time>…) (?<level>\\w+) (?<message>.*)", "timeFormats": ["yyyy-MM-dd HH:mm:ss"] } ],
  "highlights": [ { "field": "message", "pattern": "timeout|deadlock", "style": "warning" } ],
  "styles": { "deploy": { "foreground": "brightCyan", "bold": true } },
  "frameworkPrefixes": ["System.", "Microsoft.", "runtime.", "net/http."]
}
```

- Parsed like cmdr's config (UTF-8, optional BOM, comments and trailing commas allowed, camelCase), but **unknown members are
  errors** (`JsonUnmappedMemberHandling.Disallow`) so typos are reported. Every error is a `LogvuConfigException` whose
  message names the config file and the JSON path of the offending field (`fileNamePattern: …`, `schemes[1].header: …`),
  plus line/position for malformed JSON.
- Required: `logsDirectory` (relative to the config file's directory), `filePrefix` (non-empty literal), `fileNamePattern`
  (regex matched against the whole basename: wrapped `\A(?:…)\z`, case-insensitive on Windows/macOS, 250 ms timeout).
- `activeFile`: optional basename; it must start with `filePrefix` and match `fileNamePattern`.
- `format`: `"auto"` (default) or the name of a built-in or configured scheme. Configured scheme names must be unique and
  must not shadow built-ins; their `header` is validated by `TextScheme`'s constructor.
- `timeZone`: `"local"` (default), `"utc"`, or a system time zone id (IANA or Windows); unknown ids are errors.
- `recentCapacity`: events kept in the recent-history ring; default 20000, range 100–1000000.
- `minLevel`: the timeline's initial minimum level (`any`, `trace` … `fatal`, `warning` = `warn`); default `info`. The
  controller passes it as the filter of `TimelineModel.Start`, so the first load is already filtered.
- `columns`: extra timeline columns, each a path string or `{path, title?, width?}` (`ColumnSpec`; path syntax checked by
  `JsonFieldPath.IsWellFormed`, width 1–200, default 30). `RowBuilder.BuildFromText` stores one value per column on
  `EventRow.Columns` (JSON path for JSON events, first field of that name for text events; first line, capped at 256 chars;
  raw rows and `BuildHead` rows have none), so drawing never re-parses. The layout drops extra columns first on narrow
  terminals and keeps per-column grow-only widths; `keepEnd` columns are cut from the front.
- `highlights`: ordered; `field` is `message`, `raw`, or a field name (JSON dotted path for JSON events); `pattern` is a
  regex (validated, 250 ms timeout); `style` is a built-in style (`error`, `warning`, `info`, `debug`, `accent`, `emphasis`,
  `dim`, `success`) or a key of `styles`.
- `styles`: name → `{foreground?, background?, bold?, italic?, underline?}`; colors are the 16 ANSI names (`black` … `white`,
  `brightBlack` … `brightWhite`) or `#rrggbb`.
- `frameworkPrefixes`: namespace/package prefixes whose stack frames are dimmed and foldable. Default: `System.`,
  `Microsoft.`, `Newtonsoft.`, `log4net.`, `Serilog.`, `runtime.`, `net/http.`, `github.com/labstack/`.

### File set

Members are regular files (not directories, not symlinks to directories) directly in `logsDirectory` whose basename starts
with `filePrefix` (ordinal, case-insensitive on Windows/macOS) and matches `fileNamePattern`, excluding compressed files
(extensions `.gz`, `.zip`, `.bz2`, `.xz`, `.zst`, `.7z`, `.br`, `.lz4`). A missing directory is not a config error: the view is
empty and waits (the directory may be created later).

## Catalog (M3)

A **member** is one generation (file incarnation) in the set. Per member: session generation id (int, increasing), current path,
file identity (from the open handle: device+inode / volume serial+file index), scheme, first-event offset and time, state
(`Pending` = no complete event yet, `Dated`, `UnknownTime`, `Expired`), and `IsLive` (the active file, or the newest member
when there is no `activeFile`).

Ordering: dated members by first-event time (compared as instants); equal times broken by rotation order — only when `activeFile` is
configured and both names have the form `<activeStem><n><activeExt>` (the active file itself counts as 0), higher numbers are
older (log4net numbered backups) — else by basename (ordinal). Serilog's `_001` sequences and lumberjack's timestamped backups
are thus ordered by name, which is chronological for them. At equal times the key is: rotation names (as defined above) before other names, higher rotation number first, then basename,
then generation — one lexicographic key, so the order is transitive. Unknown-time members sort before all
dated members, in basename order, as a separate marked group. Pending members are not in the order until their first event.

## Paging and memory (M4)

A **row key** is `(generation, offset)` of an event start. The **ring** holds the newest `recentCapacity` row summaries
(key, extent, time, level, column values, first-line preview capped at 512 chars, raw/provisional flags). The **page** is the list of
row summaries currently visible plus a margin; it is filled from the ring when possible and otherwise by framing backward or
forward from an anchor row, crossing to the adjacent member at a file boundary. Detail and search read full text through
`EventText.Read` on a freshly opened handle whose identity is verified against the member.

## Live tail and rotation (M5)

**No long-lived handles.** Every read opens the member's path with `FileShare.ReadWrite | FileShare.Delete`, verifies the
handle's identity against the member, reads, and closes. Holding handles would, on Windows, leave a deleted backup
"delete pending", and log4net's next rename onto that name would fail — logvu must never disturb the producer. A handle whose
identity differs from the member's means the path now belongs to another file: the read is abandoned and reconciliation runs.

Identity: `Shared.Core.FileIdentity` — `TryGet(path)` (copied from cmdr) and `TryGet(SafeFileHandle)` (Linux `statx` with
`AT_EMPTY_PATH` on the fd → device+inode; Windows `GetFileInformationByHandle` → volume serial+file index).

**Reconciliation** (triggered by a `FileSystemWatcher` on the directory — coalesced over 100 ms — by a periodic timer every
2 s, and after watcher overflow/error; always run on the store's worker, never concurrently):
1. Enumerate the file set; take each path's identity (retry a transient failure up to 3 times, 50 ms apart; a path that keeps
   failing is skipped this round).
2. Match known members by identity. Same identity, other path → rename: update the path, keep generation and offsets. Known
   member whose identity is gone → `Expired` (rows stay; their disk reads report **Source file expired**).
3. A member whose current length is below its read cursor → truncated: the old generation expires and the identity gets a
   fresh generation read from 0.
4. Unknown identities → new members (fresh generation, `Pending` until the first event is found).
5. The live member is the one named `activeFile` (if configured) or else the newest member in catalog order. A member that
   stops being live is **drained**: framed forward from its cursor as a closed file (unterminated last line accepted), its
   provisional event finalized.
6. Members are drained/read in catalog order, so rows from a predecessor are published before rows from its successor.

**Tail polling.** The live member's length is checked every 250 ms by opening it (directory metadata lags for files held open
by the writer on Windows). New bytes are framed forward from the provisional event's start (or the cursor). The provisional
row's extent is updated in place (not a new arrival); events after it are new arrivals. The read cursor advances to the end of
the last complete event; a held-back unterminated tail is re-read next time.

**Retention gap.** If a member expires before its cursor reached its length (unread bytes lost), a gap marker row is inserted
after its last known row ("N bytes of <file> expired before they were read").

**Idempotence.** Rows are keyed `(generation, offset)`; publishing an existing key updates it instead of adding a row, so
repeated notifications and re-reads cannot duplicate rows.

## Store and timeline source (M3–M5)

`LogStore` (Logvu.Core) owns the catalog, the ring and a single worker task. The UI talks to it only through:
- `Snapshot` events: batched changes (new rows, updated provisional row, expired generations, catalog/status changes),
  marshalled to the UI thread by the caller.
- `Task<RowPage> ReadAsync(RowKey? anchor, Direction, int count, EventFilter?, CancellationToken)`: rows older/newer than the
  anchor (null anchor = newest), served from the ring when it covers the range, else framed from disk via the catalog.
- `Task<EventDetail> ReadDetailAsync(RowKey, CancellationToken)`: full text and parsed event, or an expired/unavailable result.
Superseded requests are cancelled by the caller; results carry the request id so stale ones are dropped.

### Read cursors and the tail (M5)

`ReadCursor` is the boundary between history (served by `HistoryReader` from snapshots) and the tail (owned by the worker).
It is always an event boundary.
- **Discovery.** A closed member's cursor is its length. A live member's cursor is the start of its last event when that event
  is provisional (text schemes), otherwise its live data end. The provisional event is read by the first tail pass.
- **Tail pass** (any readable member whose length is greater than its cursor, in catalog order): frame forward from the cursor
  — live framing for the live member, closed framing otherwise. Every complete event becomes a new row (appended to the ring and
  published as an arrival); a provisional event becomes/updates the provisional row (published as an update, not an arrival).
  The cursor moves to the provisional event's start, or to the data end when there is none.
- **Drain.** A member that stops being live gets one closed tail pass: its provisional row is finalized (same key, now complete)
  and an unterminated last line is accepted.
- A member that has a provisional row and is later found shorter than its cursor + the provisional extent is truncated (see
  reconciliation step 3).

`Reconciler` is the synchronous core: `ChangeBatch Run(Catalog, RowRing, bool rescanDirectory)` performs reconciliation (when
asked) and a tail pass and returns what changed — appended rows, the updated provisional row, finalized rows, expired
generations, gap markers, catalog/status changes and read errors. `LogStore` wraps it with the worker, the watcher, timers and
the async read API, and is the only code with threads.

## Timeline model (M6)

`TimelineModel` (Logvu.Core, no Terminal.Gui) holds the reader's position; the timeline view only renders it and forwards keys.

State: a **window** of loaded rows (oldest first) with `AtStart` / `AtEnd` (the window reaches the start / newest end of
history under the active filter), viewport height `H ≥ 1`, `Top` (window index of the first visible row), `Selected` (window
index), `Mode` (Follow | Paused), `NewCount` (matching arrivals while paused), and the active filter. Rows are identified by
`RowKey`; indices are an implementation detail re-derived after window changes.

Invariants: `Top ≤ Selected ≤ Top + H − 1` (when the window has rows); the viewport is full whenever the window has ≥ H rows
(no blank rows above when older rows exist); in Follow the newest row is selected on the bottom visible row.

Operations (every manual one pauses Follow first):
- **Up / Down**: move the selection one row. From the top (bottom) visible row the viewport also shifts one row. At the start
  (end) of history nothing moves.
- **Shift+Up / Shift+Down**: shift the viewport one row toward older (newer) rows; the selection keeps its row while visible,
  else it becomes the nearest visible row (bottom row for Shift+Up, top row for Shift+Down). At the history edge nothing moves.
- **PgUp / PgDn**: if the viewport can move, move it by up to H rows and keep the selection's screen row; if it cannot (already
  at the edge), select the first (last) row. At a history edge the move is clamped.
- **End**, and **Space** while paused: Follow — load the newest page, select the newest row at the bottom, `NewCount = 0`.
  **Space** while following: Pause (nothing else changes).
- **Resize** to height H': keep the selected row and its screen row where possible, clamp, refill.
- **Arrivals** (store batch `Added`/`Updated`, already filtered by the caller's predicate): Follow → append, keep the newest
  selected at the bottom. Paused → `NewCount += added matching rows`; if `AtEnd`, append them to the window without moving the
  viewport; updated rows replace their keys in place wherever they are.
- **Reset** (store `RingReset`, filter change, catalog change that invalidates order): reload a window around the selected key
  (or the newest rows in Follow).
- **Loading**: the model keeps a margin of at least 2·H loaded rows beyond each viewport edge unless `AtStart`/`AtEnd`, requesting
  more from an `ITimelineSource` (`ReadOlder/ReadNewer/ReadNewest` returning `Task<RowPage>`). A move that needs rows not yet
  loaded waits: navigation commands are queued in order and applied when the load completes. Rows far outside the viewport
  (beyond 8·H on either side) are trimmed, clearing the corresponding `AtStart`/`AtEnd`. Results of superseded loads
  (after a reset) are discarded by a generation counter.
- Rows in the window are independent of the ring: a paused selection survives ring eviction and rotation.

## Detail (M8)

Enter, → or `l` opens the selected event full-screen (the timeline pauses); Esc, ← or `h` returns to the same selection and
viewport. In the detail the arrows and `hjkl` only move; Enter/Space toggle nodes and folds. The detail is
built by a pure `DetailDocument` (Logvu.Core, no Terminal.Gui) from `EventDetail` + config; the `DetailView` renders and
navigates it.

**Document model.** A list of logical lines, each a sequence of styled spans (`SpanStyle`: none, field-name, punctuation,
exception-type, exception-message, method, file-location, directory, framework (dim), json-key, json-string, json-number,
json-literal, highlight:<name>, fold-marker, muted). Lines carry an optional `NodeId` (JSON node or fold) so the view can toggle
expansion, and a `Kind` (field, message, json, raw, fold). Wrapping to the width is the view's job (continuation rows
indented by 2 under the value column).

There is no header block: the document starts with the content. The status line shows the event's file name.

**Text events.** Named fields (`name: value`) in pattern order, as written. The `message` field is the full multi-line
message as one value (stack analysis, continuation lines at the value indent).

**JSON events.** The tree only: the message and error are already values in it. The one exception is a Serilog message
logvu rendered from `MessageTemplate` + `Properties` (`LogEvent.MessageRendered`), which is shown first as `Message:` since it
appears nowhere else. The tree: objects and arrays as expandable nodes (`▾ key {n}` / `▸ key [n]`), scalars as `key: value` with JSON styling.
Default expansion: depth ≤ 2 expanded, deeper collapsed, except nodes on the path to an error/exception/stack/cause value
(expanded so stacks are visible). Strings containing newlines (Serilog `Exception`) are shown as multi-line values and get
stack-trace treatment.

**Stack traces.** `StackTraceAnalyzer` classifies lines of a multi-line value (or elements of a Go `stack` array):
- .NET: exception header `Namespace.TypeException: message` (also after ` ---> `), frame `   at Ns.Type.Method(args) in
  path:line N` (and without ` in …`), `--- End of inner exception stack trace ---`, `--- End of stack trace from previous
  location ---`.
- Go: frame `pkg/path.Func (path/file.go:42)` / `pkg.(*T).Method (file.go:42)`; `cause` arrays (strings or `{message, attrs}`)
  render as a numbered cause chain.
- Styles: exception type and message emphasized; method name emphasized; `file:line` emphasized; the directory part of paths
  dimmed; a frame whose method starts with a configured `frameworkPrefixes` entry is a framework frame (whole line dimmed).
- Folding: every run of ≥ 2 consecutive framework frames gets a fold marker line `… N framework frames` above it; folds start
  expanded ("keep all frames visible by default"); Enter/Space on a marker toggles it; `z` toggles all folds of the event.
  Navigating to a search hit inside a folded run expands it.

**Highlight rules.** Applied after structural styling to the scoped field's text (`message`, `raw`, a field name, or a JSON
dotted path; a rule on an array path also covers its elements, e.g. `err.stack` covers `err.stack[0]`); ordered, first rule wins on overlapping spans; search hits and the selection line override everything.

**Raw view.** `r` toggles between the structured document and the raw event text (exact lines, no styling except highlight
rules scoped to `raw`).

**Limits.** Event text is capped by the store (4 MiB); a truncated event shows `… truncated at N bytes` at the end. Lines
longer than 64 KiB are wrapped lazily by the view (only visible rows are measured).

**Expired.** `Unavailable` details render a single message (`Source file expired` or the error) with the row's preview.

## Search (M9)

`/` or Ctrl+F opens the shared `SearchBar` above the status line (case-insensitive literal by default; `Aa`, `Word` and `.*`
toggles; session history). Enter confirms and searches **toward older events** from the selected row (inclusive of rows above
it, exclusive of the selected row) — the reader usually starts at the newest event, at the bottom. `n` / F3 repeat toward older
events, `N` / Shift+F3 search toward newer events. There is no wrap-around: at the start/end of history the status line says
`No more matches above` / `below` and the selection stays.

- **What matches**: the full event text (all lines, JSON field names and values, stack frames) and, for JSON events, the
  rendered message (Serilog templates). Only events passing the active filter are searched.
- **Engine**: `IRowFilter.Matches(EventRow row, LogEvent ev, string text)` receives the event text; `LogStore.FindAsync(RowKey
  from, SearchDirection, SearchPattern, IRowFilter? filter, IProgress<long>, CancellationToken)` scans (ring rows are re-read
  from disk like history) and returns the first matching row, a boundary result, or `TimedOut` when the regex timed out on an
  event (the shared `SearchPattern` then abandons the pattern; the status line says so instead of claiming there are no more
  matches). The UI compiles a fresh pattern for every search, including `n`/`N` repeats. Scans run off the UI thread, report scanned
  bytes, and are cancelled by Esc or a new search.
- **Timeline**: `TimelineModel.JumpTo(RowKey)` pauses, reloads around the key and selects it, keeping surrounding (filtered)
  rows visible. Matches in visible previews are highlighted with the shared search colors.
- **Detail**: Enter on a hit opens its detail scrolled to the first match; matches are highlighted (search hits override
  structural and rule styles); `n`/`N` inside the detail move between matches within the event; a hit inside a collapsed
  fold/node is revealed (`ExpandToReveal`).
- **Status**: `Searching… 12.3 MB` while scanning (Esc cancels), then `Match 2026-11-18 10:20:31` or the boundary message.

## Filter (M10)

`f` opens the filter editor (a dialog): minimum level (or a level set), file name contains, time from / to
(in the config time zone, `yyyy-MM-dd HH:mm[:ss]`), text (literal or regex, case-insensitive by default) and any number of
field predicates `path operator value` (`FieldPredicate`; operators `equals`, `prefix`, `suffix`, `contains`, all ignoring
case; keywords in `FieldOperators` so variants such as `equals_case` can be added). Paths resolve through
`EventFields.TryGetValue`, shared with `columns`: a JSON dotted path, else `message` = the event's message; for text events
the field of that name. All predicates are ANDed. There is no special source: the logger is an ordinary field/member.
`EventFilter : IRowFilter` checks row-level predicates (level, file, time) before reading anything else.

- **Time-interval skipping**: `IRowFilter.TimeWindow` lets `HistoryReader` skip whole members whose range `[FirstTime, next
  member's FirstTime]` lies outside the window; members touching a boundary are kept (equal times are candidates).
- **Incoming events**: with a filter active, the controller does not pass store arrivals to the model directly; when the model
  is at the newest end it requests `ReadNewer` from its newest row with the filter (the store applies the same predicate), and
  `NewCount` counts the matching rows returned. Without a filter arrivals flow as before.
- **Filter to this value**: `f` on a field / JSON scalar line in the detail adds `path equals value` to the active filter and returns
  to the timeline.
- **Status**: `Filter: level ≥ WARN · text ~ timeout`; while a filtered load scans: `Scanning… 34 MB`; when a completed scan found
  nothing: `No matching events`.

## Scan performance

History scans frame events with an allocation-free span scanner shared by `LineReader` and `EventFramer`, and take each event's
text from the framer's cached blocks (no second read). Filters may opt into two pre-checks before an event is parsed:
`MayMatchHead` (the header line: level and time for text schemes) and `MayMatchText` (the raw text). Pre-checks must be
exact — they may only reject events the full `Matches` would reject. The raw-text pre-check for search is exact for text schemes,
and for slog only with a literal, non-whole-word pattern and a line without backslashes (escapes make the decoded message differ
from the raw line); Serilog is never pre-checked (messages are rendered from templates). Batches hold a handle for up to 4096
events / 16 MiB. Measured (Release, opt-in `LOGVU_PERF=1` benchmark in `tests/Logvu.Core.Tests/Perf`): literal search ≈ 110 MB/s
on log4net text, ≈ 210 MB/s on slog JSON; a level-only filter ≈ 30 MB/s, bounded by the two compiled-regex calls per event
(framing + header parse).
