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

`./publish.ps1` publishes cmdr and logvu for the current platform into `publish/<rid>/` and puts them on the PATH (see the README); `-All` publishes for both platforms, `-NoInstall` skips the install.

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
| `minLevel` | The minimum level the timeline starts with: `info` (default), `trace`, `debug`, `warn` (or `warning`), `error`, `fatal`, or `any` for no filter. Events without a recognised level (text before a file's first header, malformed lines) only show with `any`. Change it at run time with `-` / `+` or `f`. |
| `columns` | Timeline columns between Level and Message (Time, Level and Message are always shown): each item is a path string or `{ "path", "title", "width", "keepEnd" }`. `path` is a JSON path for JSON events (`status`, `req.path`, `err.attrs[0].code`, `Properties.SourceContext`) or a field name for text events (`logger`, `thread`). `message` is the event's message in every format; for slog it is `msg` followed by the `error`/`err` object's `message` and its `cause` chain (strings, and each cause object's `message` then its own causes), joined with `: `.  `title` defaults to the path's last name; `width` is the most columns it may take (1–200, default 30); `keepEnd: true` cuts a long value from the front (`…Engine.Runner`), for logger names. A column fits its widest value on screen and does not shrink while scrolling; on narrow terminals extra columns are dropped first, from the right. Objects and arrays show as compact JSON; a missing value is blank. |
| `messageFields` | Optional list of JSON paths (same syntax as `columns`, such as `status`, `req.path`, `err.attrs[0].code`) whose values a JSON event's timeline preview shows after the message's first line, as `path=value` pairs: the first preceded by two spaces, the rest by one. Only paths that resolve in the event's JSON are shown, and each value is its first line; the preview is still cut to its usual length. The message itself, search and filters are unchanged, and text events are not affected. Default: none. |
| `recentCapacity` | Events kept in memory near the newest end for fast paging (default 20000, 100–1000000). Older history is read from disk on demand. |
| `schemes` | Custom text formats: `{ "name", "header", "timeFormats" }`. `header` is a regular expression matched against a whole line, with named groups `message` (required), `time`, `level`, `logger` and any others (shown as fields). Lines that do not match the header continue the previous event. |
| `highlights` | Ordered rules `{ "field", "pattern", "style" }` colouring matches in the detail: `field` is `message`, `raw`, a field name or a JSON dotted path (`Properties.SourceContext`; a path to an array covers its items). For JSON events `message` applies only to a message logvu rendered from a Serilog template; scope rules to the JSON field (`msg`, `RenderedMessage`) instead. The first matching rule wins where matches overlap. |
| `styles` | Named styles for `highlights`: `{ "name": { "foreground", "background", "bold", "italic", "underline" } }` with ANSI colour names (`red`, `brightCyan`, …) or `#rrggbb`. Built-in names: `error`, `warning`, `info`, `debug`, `accent`, `emphasis`, `dim`, `success`. |
| `frameworkPrefixes` | Namespace or package prefixes whose stack frames are dimmed and can be folded. Default: `System.`, `Microsoft.`, `Newtonsoft.`, `log4net.`, `Serilog.`, `runtime.`, `net/http.`, `github.com/labstack/`. |

Unknown settings are errors, so typos are reported.

## Screen and keys

The timeline shows one row per event — **Time, Level**, the configured `columns` (for example the logger), **Message** (its
first line) — oldest at the top.
When a page spans more than one day every row shows the date.

The status bar on the first row shows Follow or Paused, the number of new events while paused, the active filter, search progress,
read problems and notices such as a copy. The last row lists the keys that work where the cursor is, with the keys of an active
search first. While the search bar is open it sits on the row above those keys.

| Timeline | |
|---|---|
| ↑ / ↓ or `k` / `j` | Select the previous / next event (the page scrolls at its edges) |
| Shift+↑ / Shift+↓ | Scroll the page one event, keeping the selection while it stays visible |
| PgUp / PgDn | Scroll a page, keeping the selection's position on screen |
| Space | Pause / follow. Following resumes at the newest event |
| End | Follow the newest event |
| Enter, → or `l` | Open the selected event in detail |
| `/` or Ctrl+F | Search; Enter searches toward older events. With a text selection in a row, searches for the selected text |
| `n` or F3 / `N` or Shift+F3 | Next match toward older / newer events |
| `f` / `F` | Edit the filter / clear it |
| `-` or `_` / `=` or `+` | Lower / raise the minimum level (any → Debug → Info → Warn → Error → Fatal), keeping the rest of the filter |
| Mouse | Wheel scrolls; a click selects (and pauses); dragging selects text within that row; a double-click opens the event's detail |
| `c` | Copy the selected text, or without a selection the whole event as its detail shows it with everything expanded, with the indentation the lines share removed |
| Ctrl+O | Open another log set from the recent configs (Esc returns, ^Q quits) |
| Esc | Clears a selection; without one, cancels a running scan |
| Ctrl+Q | Quit |

The `>` in the left column marks the selected event. A text selection stays in the row it was made in, and is cleared when that
row is no longer the selected one or scrolls out of view.

Any movement pauses Follow; scrolling back to the bottom stays paused until Space or End.

**Detail.** The fields of a text event as written, in pattern order, `message` as a multi-line value — or, for JSON, the expandable tree of the
event (preceded by the message only when logvu rendered it from a Serilog template, since it appears nowhere else). The status
bar shows the event's file name. .NET exceptions (including inner exceptions) and Go stack traces are
styled: exception types and messages, method names and `file:line` stand out, directories and framework frames are dimmed.
Runs of framework frames get a `… N framework frames` marker that folds them. Timestamps in JSON values show the date and time in colour, with fractions and offsets dimmed.
Text events use the same colours as JSON: field names as keys, values as strings, numbers or timestamps. Exception type names in text messages are red, and file paths and URLs are underlined. The keys of values shown as `columns` or `messageFields` are drawn in bright magenta.

| Detail | |
|---|---|
| ↑ / ↓ or `k` / `j`, PgUp / PgDn, Home / End | Move the cursor (the `>` in the left column marks its line) |
| Shift+↑ / Shift+↓, Shift+PgUp / Shift+PgDn | Select whole lines from the cursor |
| Mouse | A click moves the cursor; dragging selects text, and dragging past the top or bottom scrolls; a click on the ▾ or ▸ of a node, or on a `… N framework frames` marker, toggles it |
| `c` | Copy the selection, or the cursor line without one, with the indentation the lines share removed |
| Enter / Space | Expand or collapse the node or fold under the cursor |
| `z` | Fold or unfold all framework-frame runs |
| `r` | Raw text of the event |
| `f` | Filter the timeline to the value of the field under the cursor |
| `/`, `n`, `N` | Search within the event; `/` with a one-line selection searches for the selected text |
| Esc | Clears the selection; without one, back to the timeline, at the same position |
| ← or `h` | Back to the timeline, at the same position |

**Search** matches the full text of events (all lines, JSON names and values, stack frames, and Serilog's rendered message),
case-insensitively by default; the bar's toggles switch on case sensitivity, whole words and regular expressions. Only events
passing the filter are searched. A search does not wrap: the status bar says when there are no more matches.

**Filter** starts at the configured `minLevel` (Info by default) and combines a minimum level, a file name substring, text, a time range and field conditions, one per line as
`path operator value` with the operator `equals`, `prefix`, `suffix` or `contains` (ignoring case), e.g. `logger contains Engine`,
`message prefix Timeout`. Paths are those of `columns`.
(all must match). Incoming events go through the same filter. The status bar distinguishes a scan in progress from a scan that
found nothing.

## Rotation and live files

logvu tracks files by identity, not by name, so rotation (log4net renaming `SheetKraft.log` to `SheetKraft1.log`, lumberjack
moving to a timestamped backup, Serilog starting a new dated file) neither repeats nor loses events. The last events of a
rotated file are read before the first events of its successor. A file truncated or replaced in place is read as a new file.
Events that were deleted before logvu could read them are reported as a gap; opening the detail of an event whose file has
since been deleted shows **Source file expired**.

logvu never keeps log files open: each read opens, verifies and closes the file, with sharing that lets the writer rename and
delete it.
