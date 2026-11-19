# logvu

A terminal viewer for the log files of one application: a set of rotated files in one directory, read as a single
chronological stream. It follows new events as they are written, keeps working through log rotation, and shows each event
in detail, with stack traces made readable. It reads log4net text logs and Serilog and Go `slog` JSON logs.

Runs on Linux and Windows. Built from this repository alongside cmdr; see the main [README](../README.md#install) for the
.NET 10 SDK and the Terminal.Gui submodule.

```sh
dotnet run --project src/Logvu -- path/to/logvu.json
dotnet publish src/Logvu -c Release -r win-x64      # or linux-x64 → logvu(.exe)
```

`./publish.ps1` publishes cmdr and logvu for both platforms into `publish/<rid>/`.

## Usage

```sh
logvu <config.json>
logvu
logvu --version
```

A config that fails to load is reported with the offending field and logvu exits with status 1. Every config that opened
successfully is remembered (the last 20) in `%LOCALAPPDATA%\logvu\recent.json` on Windows and `~/.local/share/logvu/recent.json`
on Linux (`$XDG_DATA_HOME/logvu/recent.json` when set). `logvu` without arguments shows that list — also on first use, when it
is empty: Enter opens the selected config, a number selects by position, Del removes an entry, `o` or Ctrl+O opens a config
file through a file dialog, Esc or Ctrl+Q quits.

## Configuration

```json
{
  "logsDirectory": "../Logs",
  "filePrefix": "SheetKraft",
  "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
  "activeFile": "SheetKraft.log",
  "format": "log4net-core"
}
```

| Setting | Meaning |
|---|---|
| `logsDirectory` | Required. The directory of the log files, relative to the config file. It may be missing; logvu waits for it. |
| `filePrefix` | Required. A literal prefix every member's file name starts with. |
| `fileNamePattern` | Required. A regular expression the whole file name must match. Both selectors must match. Only regular files directly in the directory count; compressed files (`.gz`, `.zip`, `.bz2`, `.xz`, `.zst`, `.7z`, `.br`, `.lz4`) are skipped. Names compare case-insensitively on Windows. |
| `activeFile` | Optional. The fixed name of the file being written (log4net numbered rotation). Without it, the newest file is the live one (date-named files). Numbered backups of the active file (`SheetKraft2.log`) order before lower numbers when their first events have the same time. |
| `format` | `auto` (default: detected per file) or a scheme name: `log4net-core`, `log4net-web`, `log4net-scheduler`, `log4net-date-first`, `serilog-json`, `slog-json`, or a configured scheme. |
| `timeZone` | Time zone for timestamps written without an offset (log4net): `local` (default), `utc`, or a system time zone id. Times are also displayed in this zone. |
| `recentCapacity` | Events kept in memory near the newest end for fast paging (default 20000, 100–1000000). Older history is read from disk on demand. |
| `schemes` | Custom text formats: `{ "name", "header", "timeFormats" }`. `header` is a regular expression matched against a whole line, with named groups `message` (required), `time`, `level`, `logger` and any others (shown as fields). Lines that do not match the header continue the previous event. |
| `highlights` | Ordered rules `{ "field", "pattern", "style" }` colouring matches in the detail: `field` is `message`, `raw`, a field name or a JSON dotted path (`Properties.SourceContext`; a path to an array covers its items). The first matching rule wins where matches overlap. |
| `styles` | Named styles for `highlights`: `{ "name": { "foreground", "background", "bold", "italic", "underline" } }` with ANSI colour names (`red`, `brightCyan`, …) or `#rrggbb`. Built-in names: `error`, `warning`, `info`, `debug`, `accent`, `emphasis`, `dim`, `success`. |
| `frameworkPrefixes` | Namespace or package prefixes whose stack frames are dimmed and can be folded. Default: `System.`, `Microsoft.`, `Newtonsoft.`, `log4net.`, `Serilog.`, `runtime.`, `net/http.`, `github.com/labstack/`. |

Unknown settings are errors, so typos are reported.

## Screen and keys

The timeline shows one row per event — **Time, Level, Source** (the logger), **Message** (its first line) — oldest at the top.
When a page spans more than one day every row shows the date. The status line shows Follow or Paused, the number of new events
while paused, the active filter, search progress and read problems.

| Timeline | |
|---|---|
| ↑ / ↓ or `k` / `j` | Select the previous / next event (the page scrolls at its edges) |
| Shift+↑ / Shift+↓ | Scroll the page one event, keeping the selection while it stays visible |
| PgUp / PgDn | Scroll a page, keeping the selection's position on screen |
| Space | Pause / follow. Following resumes at the newest event |
| End | Follow the newest event |
| Enter, → or `l` | Open the selected event in detail |
| `/` or Ctrl+F | Search; Enter searches toward older events |
| `n` or F3 / `N` or Shift+F3 | Next match toward older / newer events |
| `f` / `F` | Edit the filter / clear it |
| `-` or `_` / `=` or `+` | Lower / raise the minimum level (any → Debug → Info → Warn → Error → Fatal), keeping the rest of the filter |
| Mouse | Wheel scrolls; a click selects (and pauses) |
| Ctrl+Q | Quit |

Any movement pauses Follow; scrolling back to the bottom stays paused until Space or End.

**Detail.** The fields (time, level and logger as written) and the full message — or, for JSON, the message and error summary
followed by an expandable tree. The status line shows the event's file name. .NET exceptions (including inner exceptions) and Go stack traces are
styled: exception types and messages, method names and `file:line` stand out, directories and framework frames are dimmed.
Runs of framework frames get a `… N framework frames` marker that folds them.

| Detail | |
|---|---|
| ↑ / ↓ or `k` / `j`, PgUp / PgDn, Home / End | Move the cursor |
| Enter / Space | Expand or collapse the node or fold under the cursor |
| `z` | Fold or unfold all framework-frame runs |
| `r` | Raw text of the event |
| `f` | Filter the timeline to the value of the field under the cursor |
| `/`, `n`, `N` | Search within the event |
| Esc, ← or `h` | Back to the timeline, at the same position |

**Search** matches the full text of events (all lines, JSON names and values, stack frames, and Serilog's rendered message),
case-insensitively by default; the bar's toggles switch on case sensitivity, whole words and regular expressions. Only events
passing the filter are searched. A search does not wrap: the status line says when there are no more matches.

**Filter** combines a minimum level, source and file name substrings, text, a time range and `path = value` field conditions
(all must match). Incoming events go through the same filter. The status line distinguishes a scan in progress from a scan that
found nothing.

## Rotation and live files

logvu tracks files by identity, not by name, so rotation (log4net renaming `SheetKraft.log` to `SheetKraft1.log`, lumberjack
moving to a timestamped backup, Serilog starting a new dated file) neither repeats nor loses events. The last events of a
rotated file are read before the first events of its successor. A file truncated or replaced in place is read as a new file.
Events that were deleted before logvu could read them are reported as a gap; opening the detail of an event whose file has
since been deleted shows **Source file expired**.

logvu never keeps log files open: each read opens, verifies and closes the file, with sharing that lets the writer rename and
delete it.
