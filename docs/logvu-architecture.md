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
    string? Source;            // logger: log4net %logger, Serilog Properties.SourceContext, slog "logger" attribute
    string Message;            // full message (text: first-line message + continuation lines joined with \n)
    IReadOnlyList<LogField> Fields;   // named fields in scheme order (text) or top-level members (JSON)
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
(key, extent, time, level, source, first-line preview capped at 512 chars, raw/provisional flags). The **page** is the list of
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
