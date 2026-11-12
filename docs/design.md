# cmdr — design

A cross-platform (Linux, Windows) TUI for managing a set of non-interactive process trees.
.NET 10, Terminal.Gui v2.

## Solution layout

```
Cmdr.slnx
src/Cmdr.Core/          config, process supervision, output buffering/parsing, logging (no UI)
src/Cmdr/               Terminal.Gui app
tests/Cmdr.Core.Tests/  xUnit tests for Cmdr.Core
```

## CLI

`cmdr <config.json>` — path is required. If the file does not exist, cmdr starts with an
empty list; the first save creates it. `cmdr --send-ctrl-c <pid>` is an internal helper mode
(Windows only, see below).

## Config

```json
{
  "bufferLines": 10000,
  "logsDir": "logs",
  "groups": [
    {
      "name": "build",
      "description": "optional",
      "mode": "sequential",
      "items": [
        { "name": "restore", "program": "dotnet", "args": ["restore"] },
        { "name": "build", "program": "dotnet", "args": ["build"] }
      ]
    },
    {
      "name": "servers",
      "mode": "parallel",
      "items": [
        {
          "name": "web",
          "description": "Frontend dev server.\nNeeds the api running first.",
          "program": "pnpm",
          "args": ["start"],
          "cwd": "../web",
          "env": { "FORCE_COLOR": "1", "SOME_VAR": null },
          "stop": { "method": "ctrlC" },
          "log": { "enabled": false, "path": "logs/web.log" }
        },
        {
          "name": "tool",
          "program": "/opt/tool/run",
          "cwd": ".",
          "stop": { "method": "input", "input": "q\n" }
        }
      ]
    }
  ]
}
```

- Every command belongs to a group; groups and commands are shown in file order.
- Group `expanded` (default `true`, only `false` is written): whether the group starts expanded when
  the config is loaded. Not kept in sync with collapsing/expanding in the UI (that never marks the file
  dirty); editing it in the group dialog takes effect on the next load.
- Group `mode`: `parallel` (default) or `sequential`. Group names unique (case-insensitive) within a file;
  command names unique within their group.
- `args` is an array, passed via `ProcessStartInfo.ArgumentList` (no quoting issues).
- Input placeholders in `args`: `${input:NAME}` (value must not be empty) or `${input:NAME?}` (empty
  allowed), anywhere inside an argument; NAME is `[A-Za-z0-9_.-]+`. `$${input:` is a literal `${input:`.
  No other expansion happens in args. Malformed placeholders are validation errors.
  Before Enter/F5 starts anything that needs inputs, one dialog asks for every distinct NAME of that action
  (one value per name; required if any use lacks `?`); Enter = OK, Esc = cancel, and cancelling starts
  nothing. Values are fixed for the whole action (later sequence steps use them), are not remembered for
  the next run, and are not written to the output or log. The info strip and `c` show the placeholders.
- `env`: values overlay the inherited environment; `null` removes a variable.
- Relative `cwd` and `logsDir` resolve against the config file's directory; `logsDir` defaults to `logs`.
- Relative `log.path` resolves against `logsDir`. Default log path: `<logsDir>/<group>/<command>.log`.
- Default `stop.method` is `ctrlC`. Default `bufferLines` is 10000.
- `leftovers`: `track` (default, not written) or `detach`. See Process supervision.
- Explicit save only (Ctrl+S). Unsaved changes are marked `*` in the title; quit prompts.

### Base file

- `"base": "<path>"` (top level only, at most one) imports another config file; a relative path
  resolves against the importing file's directory. A base may have its own `base` (a chain). A cycle,
  a missing base file or a malformed one is a load error naming the files.
- No merging: the groups of the deepest base come first, then each importing file's, the opened file's last.
- Each file keeps its own settings for its own groups: `bufferLines`, `logsDir`, and the directory that
  relative `cwd` and `${configDir}` resolve against.
- Validation is per file: group names must be unique within a file; the same name in different files is
  allowed. Errors from a base file are prefixed with its path.
- Base files are read-only. Their commands can be run, stopped, restarted and logged, but editing,
  adding into, removing or moving their items (or moving items into them) is refused with
  `'<name>' comes from <path>; edit that file.` The opened file's top-level groups stay below the base
  groups. A top-level base group's header shows its file name (dimmed); the info strip shows
  `from: <path> (read-only)`.
- Ctrl+S writes only the opened file (with its `base`); base files are never written.

### Groups and sequences

- Selection (marks) is confined to one group; marking in another group clears the previous marks.
  Actions apply to the marked commands, else the focused command; on a focused group header they
  apply to the whole group.
- Sequential group, Enter: the selected commands run one after another in file order; each starts
  after the previous leader exits with code 0. Non-zero exit or failure to start cancels the rest
  (like `&&`). Waiting commands show `queued`. Leftover processes do not block the sequence.
- Stopping (Esc/Ctrl+C) the running command of a sequence stops it and cancels the queue. On a
  queued command, it cancels the queue only; the running command keeps going.
  Only one sequence per group at a time. F5 stops running selected commands, then
  reruns the selection in sequence.

## Process supervision

Output via pipes (not a PTY): stdout/stderr redirected and read asynchronously as UTF-8.
stdin is a pipe kept open for the lifetime of the process; it is written to only by the
`input` stop method and never closed by cmdr (some programs exit on stdin EOF).

| Action | Linux | Windows |
|---|---|---|
| Launch | `setsid <program> <args…>` → own session/process group, pgid == pid | `CreateNoWindow` (own hidden console), assigned to a Job Object with `KILL_ON_JOB_CLOSE` |
| Stop `ctrlC` | `kill(-pgid, SIGINT)` | spawn `cmdr --send-ctrl-c <pid>`: `FreeConsole` → `AttachConsole(pid)` → `SetConsoleCtrlHandler(null, true)` → `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` |
| Stop `input` | write configured string to stdin | same |
| Force kill (user only, never automatic) | `kill(-pgid, SIGKILL)` | `TerminateJobObject` |
| Leftover detection | `kill(-pgid, 0)` succeeds after leader exit | job active process count > 0 |

States: `Stopped | Running | Stopping(since) | Exited(code) | FailedToStart(message)`.
If the leader has exited but the group/job still has live processes, state shows
`Exited – N still running` and force kill remains available.
With `"leftovers": "detach"`, when the leader exits on its own (no stop or force kill during the run)
and processes remain, cmdr writes `N leftover process(es) detached`, stops supervising them and shows
a plain exit; the command can start again at once and quitting does not touch them. For tools that keep
build servers alive on purpose (MSBuild nodes, Gradle daemon). Linux: the process group is simply no
longer tracked. Windows: `KILL_ON_JOB_CLOSE` is cleared on the job before its handle is closed.
A run that exits after the user asked it to stop (graceful stop or force kill) is shown as stopped
(`∎`, `stopped · exit N · runtime`), not as a failure, whatever its exit code (a stopped program's code
is arbitrary, e.g. 255 from cmd.exe after "Terminate batch job"). Leftovers still show `⚠`. A stopped
command does not count as success, so it still ends its sequence (a graceful stop cancels the run
directly; a force kill, or a stopped parallel branch inside a run, writes `sequence cancelled: stopped`).
`sequence cancelled: …` lines are written only for commands that are part of a run; a parallel group
started outside any sequence writes none.
Restart = graceful stop, wait for exit, start.

Tracked per run: PID, `StartedAt`, `ExitedAt`, `StopRequestedAt`, exit code.

## Output

- Ring buffer of lines (`bufferLines`), each line a sequence of styled spans, tagged stdout/stderr.
- ANSI parser: SGR (16/256/24-bit colors, bold, dim, italic, underline, inverse, reset) is
  rendered; all other escape sequences (CSI, OSC, etc.) are dropped.
- Bare `\r` (not followed by `\n`) resets the current line, so progress bars overwrite in place.
- Colors through pipes require the program to force color; configure via `env`
  (`FORCE_COLOR=1`, `CLICOLOR_FORCE=1`, `DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1`, …).
- Starting a run clears the in-memory output of previous runs; the log file keeps them.

### Log file

- Enabled per command in config, or toggled at runtime (including while running).
- When enabled mid-run, the current in-memory buffer is written first, then output streams live.
- Appends; each run starts with `=== started <timestamp> ===`.
- ANSI escape codes are stripped (plain text).

## UI

```
┌ Commands ───────────────────────────┐┌ web ───────────────────────────────────┐
│ ↻ web       10:42:03   00:12:51     ││ Frontend dev server.                   │
│ · api       —          —            ││ Needs the api running first.           │
│ ✗ migrate   10:40:11   exit 1       │├────────────────────────────────────────┤
│                                     ││ (colored output, tail-follow)          │
└─────────────────────────────────────┘└────────────────────────────────────────┘
```

- Left: command list (multi-select), titled with the config file name (`*` when modified). Each command
  takes two lines: mark, status glyph and the full name; below it (dimmed) start time and elapsed /
  stopping-for / exit code / leftovers. Groups collapse with ←/→ (UI state only, not saved); a collapsed
  header shows a summary such as `2 running · 1 failed`. Refreshed every second.
- Right: details strip (4 lines high) above a colored output view for the focused row. A command's strip shows
  the command line (as `c` copies it, with a `$`/`>` prompt), its cwd, the environment cmdr sets for it (keys
  mentioned by the group chain and the command, expanded, `KEY (unset)` when removed), the pid and log, then the
  whole description. A group's strip shows mode, cwd, env and description. Lines wrap to the width. When they do
  not fit, the strip scrolls (↑↓, PgUp/PgDn, Home/End, wheel; a fresh row starts at the top) and the frame title
  shows the range, e.g. `go-apps / nav / run  (1–4 of 11 ↓)`, with ↑/↓/↕ for content out of view.
- The output view tail-follows by default; scrolling up pauses following, End resumes. Lines wrap to the view width, by cell like a terminal (nothing is dropped; copy gives the original line), and the wheel scrolls by screen rows.
- Output view is a custom view: mouse drag = character selection, Shift+↑/↓ = line selection.
- Info strip: mouse drag = character selection (kept across refreshes of the same row; cleared by Esc
  or another row). `c` there copies the selection, else the command line. A line wrapped over several
  rows is copied as one line (spaces dropped at the breaks are restored).
- Copy uses OSC52 (`ESC ]52;c;<base64> BEL`) written to the terminal.

### Keys

Selection-based actions apply to all selected commands (or the focused one if none selected; on a group header, the whole group).

| Key | Action |
|---|---|
| Space / Shift+↑↓ / Ctrl+click | multi-select (within one group) |
| Ctrl+A | toggle select all in the focused group |
| Alt+↑ / Alt+↓ | move a command or group (with its subtree) among its siblings |
| ← / → | collapse / expand group (← on a command collapses its group and focuses the header; ← on a collapsed header focuses its parent header) |
| Tab / Shift+Tab | move focus forward / backward: list → info strip → output → list |
| Enter | if any target is stopping or has leftovers: force kill those (start nothing); else start the startable targets |
| Esc | graceful stop (with confirmation) |
| Ctrl+C | graceful stop (no confirmation) |
| F5 | restart (with confirmation) |
| L | toggle log file |
| c | copy: command line (list focused, platform-quoted) / selection, else command line (info focused) / selection or current line (output focused) |
| Ctrl+N | add command |
| F2 | edit command |
| Del | delete command (with confirmation) |
| Ctrl+S | save config |
| Ctrl+Q | quit |

Group dialog: parent (top level or any group by path, excluding itself and its descendants), name, description,
mode, cwd, env. Ctrl+G defaults the parent to the focused group's parent. Groups can be added, renamed, moved,
deleted (with confirmation). Nested rows are indented 2 columns per level.
Command edit dialog: group (any group, by path; to move between groups), name, description (multi-line), program, args (multi-line, one per line), cwd,
env (multi-line `KEY=VALUE`), stop method + input, log enabled + path.
Edits to a running command apply on next start.

Quit with running commands offers: stop gracefully and wait (force kill still available),
force kill all, or cancel. The Windows Job Object kills children if cmdr dies regardless.

## Implementation notes (from investigation)

- Terminal.Gui **2.5.0** (stable). Instance API: `Application.Create()`, `app.Init()`, `app.Run(win)`.
  Default quit key is Esc; adding Ctrl+Q does not remove it — the Esc binding must be removed
  (or Esc intercepted in `OnKeyDown`) since Esc means "stop" here.
- Ctrl+C arrives as a normal key (ISIG disabled), so it can be bound.
- OSC52 is written via `app.Driver.WriteRaw(...)`; never `Console.Out` directly.
- Custom view: `OnDrawingContent`, `Move/AddStr/SetAttribute`; `Attribute`/`Color` support 24-bit RGB
  and `TextStyle` flags. Scrolling via `SetContentSize` + `Viewport`.
- UI-thread marshalling: `app.Invoke(Action)`; periodic refresh: `app.AddTimeout`.
- `setsid` execs in place (pid == pgid == sid). Non-interactive `sh` makes background jobs
  ignore SIGINT — expected; that is what force kill is for.
- **Leader exit vs. pipe EOF:** with async output reading, `Process.WaitForExit()` (no timeout)
  blocks until stdout/stderr EOF, which never comes while an orphaned child holds the pipe.
  Use the `Exited` event / `HasExited` for leader exit; track output EOF separately.

### Windows: batch files (.cmd/.bat)

`pnpm`, `npm`, `yarn` etc. are `.cmd` shims that CreateProcess cannot run directly. On Windows the
program is resolved against the effective `PATH`/`PATHEXT` (cwd first). If it resolves to a
`.cmd`/`.bat`, it is launched as `%ComSpec% /d /s /c "<escaped command line>"` using cross-spawn's
escaping rules. Ctrl+C on a batch file makes cmd.exe prompt "Terminate batch job (Y/N)?" on stdin,
so after a Ctrl+C stop of a batch-wrapped command cmdr also writes `Y\r\n` to stdin.
Caveat: a child that reads stdin could consume the answer first.

## Nested groups (tree)

A group's `items` array holds commands and groups in file order (an item with `program` is a command,
an item with `items` is a group). The top level remains `groups` (groups only). `items` replaces
`commands`.

```json
{
  "name": "nav", "mode": "sequential",
  "cwd": "../SheetKraft/CodeKraft/golang",
  "env": {
    "TMP": "${cwd}/nav/temp",
    "PATH": "/srv/devshare/artifacts/SheetKraft/bin/XldrHelper/debug${pathsep}${PATH}"
  },
  "items": [
    { "name": "meta",  "program": "go", "args": ["run", "./ckiecho/cmd/ckbuild", "--meta", "--incremental", "./nav"] },
    { "name": "build", "program": "go", "args": ["build", "-tags=debug", "-o", "nav/__debug_bin", "./nav"] },
    { "name": "run",   "program": "nav/__debug_bin", "args": ["--allow-upgrade"] }
  ]
}
```

- Groups may set `cwd` and `env`; descendants inherit them. A relative `cwd` resolves against the
  parent's effective cwd (top-level groups: the config directory).
- Environment is built outermost first: inherited process env → each ancestor group's `env` → the
  command's `env`. Each level's values are expanded against the environment as it was *before* that
  level (so `${PATH}` refers to the parent's value). `null` removes a variable.
- Expansion in env values: `${NAME}` (empty if undefined), `${cwd}` (effective cwd of the item that
  defines the env), `${configDir}`, `${pathsep}` (`:` / `;`), `$$` → `$`.
- Names are unique (case-insensitive) among siblings. Log path: `<logsDir>/<group>/<subgroup>/…/<command>.log`.
- Running: a parallel group starts all children at once; a sequential group runs children in order. A
  command child succeeds on exit code 0; a group child succeeds when all of its runs finished
  successfully. Any failure cancels the remainder of the enclosing sequence(s). Esc/Ctrl+C/F5 on a
  group act on everything below it; Enter on a group kills if anything below is stopping/has leftovers,
  else runs the group.
- List: nested groups are indented and collapsible; collapsed headers summarize all descendants. Marks
  are limited to sibling commands of one group.

### Session tree (runtime)

- `GroupEntry` has `Parent` (null at top level) and ordered mixed `Items`; `Commands`/`Groups` are its
  direct children of each kind. `Session.Groups` is top level only; `AllEntries`/`BuildRows()` walk the
  tree depth-first in file order.
- Only sequential groups own a run (`SequenceRun`, at most one active per group). Its steps are
  commands or child groups: a sequential child runs as a sub-run (registered as that group's active
  run), a parallel child starts all its children; the step waits for the child's result. A parallel
  group started outside any run starts its commands and its sequential children's runs and tracks
  nothing itself.
- Failure (non-zero exit, failure to start, cancellation) fails the step and cancels the remainder of
  every enclosing sequence. Parallel siblings are independent: a failed branch does not cancel its
  siblings; the parallel group itself fails as soon as any child fails.
- Enter on a group: kill-first over the whole subtree (stopping / leftovers); otherwise run it, skipping
  commands that are already running and dropping groups left with nothing to start. Refused with a
  message when the plan overlaps an active run.
- Esc/Ctrl+C on a group cancels every run at or below it and gracefully stops everything running below.
  On a command, it cancels the innermost run containing it (which then fails upward) and stops the
  command if it is running; on a queued command, whatever that run is currently running keeps going. F5 on a group
  stops everything below, waits for leaders, then runs the group.
- Edits on a group (mode change, move, reparent, remove) and moves/removal of items are refused while
  the affected item or anything below it is part of a run. Reparenting into itself or a descendant is
  refused.
