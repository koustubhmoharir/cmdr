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
  "groups": [
    {
      "name": "build",
      "description": "optional",
      "mode": "sequential",
      "commands": [
        { "name": "restore", "program": "dotnet", "args": ["restore"] },
        { "name": "build", "program": "dotnet", "args": ["build"] }
      ]
    },
    {
      "name": "servers",
      "mode": "parallel",
      "commands": [
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
- Group `mode`: `parallel` (default) or `sequential`. Group names unique (case-insensitive);
  command names unique within their group.
- `args` is an array, passed via `ProcessStartInfo.ArgumentList` (no quoting issues).
- `env`: values overlay the inherited environment; `null` removes a variable.
- Relative `cwd` and `log.path` resolve against the config file's directory.
  Default log path: `logs/<group>/<command>.log`.
- Default `stop.method` is `ctrlC`. Default `bufferLines` is 10000.
- Explicit save only (Ctrl+S). Unsaved changes are marked `*` in the title; quit prompts.

### Groups and sequences

- Selection (marks) is confined to one group; marking in another group clears the previous marks.
  Actions apply to the marked commands, else the focused command; on a focused group header they
  apply to the whole group.
- Sequential group, Enter: the selected commands run one after another in file order; each starts
  after the previous leader exits with code 0. Non-zero exit or failure to start cancels the rest
  (like `&&`). Waiting commands show `queued`. Leftover processes do not block the sequence.
- Stopping (Esc/Ctrl+C) any command of a running sequence stops the current command and cancels
  the queue. Only one sequence per group at a time. F5 stops running selected commands, then
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
Restart = graceful stop, wait for exit, start.

Tracked per run: PID, `StartedAt`, `ExitedAt`, `StopRequestedAt`, exit code.

## Output

- Ring buffer of lines (`bufferLines`), each line a sequence of styled spans, tagged stdout/stderr.
- ANSI parser: SGR (16/256/24-bit colors, bold, dim, italic, underline, inverse, reset) is
  rendered; all other escape sequences (CSI, OSC, etc.) are dropped.
- Bare `\r` (not followed by `\n`) resets the current line, so progress bars overwrite in place.
- Colors through pipes require the program to force color; configure via `env`
  (`FORCE_COLOR=1`, `CLICOLOR_FORCE=1`, `DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1`, …).

### Log file

- Enabled per command in config, or toggled at runtime (including while running).
- When enabled mid-run, the current in-memory buffer is written first, then output streams live.
- Appends; each run starts with `=== started <timestamp> ===`.
- ANSI escape codes are stripped (plain text).

## UI

```
┌ Commands ───────────────────────────┐┌ web ───────────────────────────────────┐
│ ● web       10:42:03   00:12:51     ││ Frontend dev server.                   │
│ ○ api       —          —            ││ Needs the api running first.           │
│ ✗ migrate   10:40:11   exit 1       │├────────────────────────────────────────┤
│                                     ││ (colored output, tail-follow)          │
└─────────────────────────────────────┘└────────────────────────────────────────┘
```

- Left: command list (multi-select), status glyph, start time, elapsed / stopping-for / exit code.
  Refreshed every second.
- Right: description strip + colored output view for the focused command. Tail-follows by
  default; scrolling up pauses following, End resumes.
- Output view is a custom view: mouse drag = character selection, Shift+↑/↓ = line selection.
- Copy uses OSC52 (`ESC ]52;c;<base64> BEL`) written to the terminal.

### Keys

Selection-based actions apply to all selected commands (or the focused one if none selected; on a group header, the whole group).

| Key | Action |
|---|---|
| Space / Shift+↑↓ / Ctrl+click | multi-select (within one group) |
| Ctrl+A | toggle select all in the focused group |
| Alt+↑ / Alt+↓ | move command within its group |
| Enter | per item: start if not running; force kill if stopping |
| Esc | graceful stop (with confirmation) |
| Ctrl+C | graceful stop (no confirmation) |
| F5 | restart (with confirmation) |
| L | toggle log file |
| c | copy: command line (list focused, platform-quoted) / selection or current line (output focused) |
| Ctrl+N | add command |
| F2 | edit command |
| Del | delete command (with confirmation) |
| Ctrl+S | save config |
| Ctrl+Q | quit |

Group dialog: name, description, mode. Groups can be added, renamed, deleted (with confirmation).
Command edit dialog: group (to move between groups), name, description (multi-line), program, args (multi-line, one per line), cwd,
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
