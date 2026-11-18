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
public readonly record struct EventExtent(long Start, long End, long HeaderEnd, bool Provisional);
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
