# cmdr

A terminal UI for running and watching the commands of a project: dev servers, watchers, builds and
one-off tools. Commands are defined in a JSON file, grouped, and run in parallel or in sequence. Each
command's output is kept and shown in color, and every command's whole process tree is supervised, so
stopping a command stops everything it started.

Runs on Linux and Windows.

```
╭┤cmdr.json├───────────────────╮╭┤servers / web├─────────────────────────────╮
│▾ build  [sequential]         ││$ pnpm start                                │
│  ✓ restore                   ││cwd: /home/me/project/web                   │
│    10:40:02  exit 0 · 0:04   ││FORCE_COLOR=1                               │
│  ✗ build                     ││                                            │
│    10:40:06  exit 1 · 0:11   ││                                            │
│▾ servers  [parallel]         ││                                            │
│  ● web                       ││                                            │
╰──────────────────────────────╯╰────────────────────────────────────────────╯
── Output — following ────────────────────────────────────────────────────────
> web@1.0.0 start
Frontend server listening on :8090
Enter Start  ^C Stop  F5 Restart  …  + Max  ^Q Quit
```

- [Install](#install)
- [Usage](#usage)
- [Configuration](#configuration)
- [Running commands](#running-commands)
- [Screen and keys](#screen-and-keys)
- [Platform notes](#platform-notes)
- [License](#license)

## Install

cmdr is built from source. It needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build,
and the .NET 10 runtime to run.

Terminal.Gui is built from source from a fork in the `Terminal.Gui` submodule, so clone with
`git clone --recurse-submodules`, or run `git submodule update --init` in an existing clone (and again after a pull
that moves the submodule). `./publish.ps1` does this itself.

Build and run from the repository:

```sh
dotnet build
dotnet run --project src/Cmdr -- path/to/cmdr.json
```

Publish a single executable (framework-dependent: the .NET 10 runtime must be installed):

```sh
dotnet publish src/Cmdr -c Release -r win-x64      # Windows → Cmdr.exe
dotnet publish src/Cmdr -c Release -r linux-x64    # Linux   → Cmdr
```

The executable lands in `src/Cmdr/bin/Release/net10.0/<rid>/publish/`; add `-o <dir>` to choose the
folder. `./publish.ps1` (PowerShell) publishes both to `publish/win-x64/` and `publish/linux-x64/`. On first run it unpacks one small native library of a dependency to `%TEMP%\.net\Cmdr\` on
Windows or `~/.net/Cmdr/` on Linux (override with `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

Run the tests with `dotnet test`.

The version is set by hand in `Directory.Build.props`.

## Usage

```sh
cmdr <config.json>
cmdr
cmdr --version
```

If the config file does not exist, cmdr starts with an empty list and the first save (Ctrl+S) creates it.

Every config opened is added to a list of recently used files (the last 20), kept in
`%LOCALAPPDATA%\cmdr\recent.json` on Windows and `~/.local/share/cmdr/recent.json` on Linux
(`$XDG_DATA_HOME/cmdr/recent.json` when set). `cmdr` with no arguments shows that list, numbered: type a
number to select an entry, Enter to open it, Del to remove it from the list, Esc or Ctrl+Q to quit. Files
that no longer exist are marked `(not found)`; opening one starts with an empty list, as above. With no
recent files, `cmdr` prints the usage. Commands and groups can be added and edited in the UI or in the file.
Changes made in the UI are saved only on Ctrl+S; unsaved changes are marked with `*` in the title.
In the edit dialogs, arguments are one per line and environment variables `KEY=VALUE` lines; when an
argument or a value contains a line break, that field is read-only (edit it in the file) and is kept as it is.

## Configuration

The config is a JSON file (comments and trailing commas are allowed). Relative paths in it resolve
against the directory of the file.

```json
{
  "bufferLines": 10000,
  "logsDir": "logs",
  "groups": [
    {
      "name": "build",
      "mode": "sequential",
      "items": [
        { "name": "restore", "program": "dotnet", "args": ["restore"] },
        { "name": "build", "program": "dotnet", "args": ["build", "--no-restore"] }
      ]
    },
    {
      "name": "servers",
      "description": "Dev servers.",
      "items": [
        {
          "name": "web",
          "description": "Frontend dev server.\nNeeds the api running first.",
          "program": "pnpm",
          "args": ["start"],
          "cwd": "../web",
          "env": { "FORCE_COLOR": "1", "SOME_VAR": null },
          "ready": { "pattern": "listening on", "busy": "build started" }
        },
        {
          "name": "tool",
          "program": "/opt/tool/run",
          "args": ["--db=${input:db-name}"],
          "stop": { "method": "input", "input": "q\n" },
          "log": { "enabled": true }
        }
      ]
    }
  ]
}
```

### Top level

| Setting | Default | Meaning |
|---|---|---|
| `groups` | | The top-level groups, in display order. |
| `bufferLines` | `10000` | Output lines kept in memory per command. |
| `logsDir` | `logs` | Directory for log files. |
| `base` | | Path of a base config file to import (see [Base files](#base-files)). |

### Groups

| Setting | Default | Meaning |
|---|---|---|
| `name` | | Required. Unique (case-insensitive) among its siblings. |
| `description` | | Shown in the info pane. |
| `mode` | `parallel` | `parallel`: Enter starts all items at once. `sequential`: items run one after another (see [Sequences](#sequences)). |
| `expanded` | `true` | Whether the group starts expanded when the config is loaded. Collapsing it in the UI doesn't change the setting. |
| `cwd` | | Working directory for everything inside; relative to the parent group's (top level: the config file's directory). |
| `env` | | Environment variables for everything inside (see [Environment](#environment)). |
| `items` | | Commands and nested groups, in display order. An item with `program` is a command; an item with `items` is a group. Groups nest at most 16 levels deep. |

### Commands

| Setting | Default | Meaning |
|---|---|---|
| `name` | | Required. Unique (case-insensitive) among its siblings. |
| `description` | | Shown in the info pane. |
| `program` | | Required. The executable. On Windows, `.cmd`/`.bat` shims such as `pnpm` work; a program not found in the working directory or on the command's `PATH` fails to start. |
| `args` | `[]` | Arguments, one per array element (no shell quoting). May contain [input placeholders](#input-placeholders). |
| `cwd` | | Working directory; relative to the group's. |
| `env` | | Environment variables (see [Environment](#environment)). |
| `stop` | `{ "method": "ctrlC" }` | How to stop gracefully: `ctrlC` (SIGINT on Linux, Ctrl+C on Windows) or `input`, which writes `stop.input` to the command's input (e.g. `"q\n"`). |
| `log` | `{ "enabled": false }` | `enabled`: also write the output to a log file. `path`: the file, relative to `logsDir`; defaults to `<logsDir>/<group>/…/<command>.log`. No two commands may resolve to the same log file (e.g. `a b` and `a_b`), across base files too. |
| `leftovers` | `track` | What to do with processes still running after the command's main process exits (see [Leftover processes](#leftover-processes)). |
| `ready` | | Output patterns that tell when the command is ready (see [Readiness](#readiness)). |
| `stdin` | `pipe` | Windows only: `console` gives the command its own console as input (see [Windows](#windows)). |

The strings passed to the process (`program`, `args`, `cwd`, `env` names and values, `stop.input`, `log.path`,
and the `cwd` and `env` of groups) may not contain NUL characters; such a configuration is reported as an error.

### Environment

`env` values are added to the environment cmdr was started with; `null` removes a variable. Groups'
`env` apply to everything inside them, outermost first, then the command's own.

Values can refer to other values:

| Write | Means |
|---|---|
| `${NAME}` | The variable's value from the level above (empty if unset), e.g. `"PATH": "/my/bin${pathsep}${PATH}"`. |
| `${cwd}` | The working directory of the group or command that defines the value. |
| `${configDir}` | The directory of the config file. |
| `${pathsep}` | `:` on Linux, `;` on Windows. |
| `$$` | A literal `$`. |

Programs usually turn color off when their output isn't a terminal. To keep colors, set the program's
variable, e.g. `FORCE_COLOR=1`, `CLICOLOR_FORCE=1` or
`DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1`.

### Input placeholders

An argument can ask for a value when the command starts: `${input:NAME}` needs a non-empty value,
`${input:NAME?}` allows an empty one. Placeholders can sit anywhere inside an argument
(`--db=${input:db-name}`). NAME may use letters, digits, `-`, `_` and `.`. Write `$${input:` for the
literal text `${input:`.

Before Enter or F5 starts anything that needs inputs, one dialog asks for every distinct name. A name
used by several commands of the same start gets one value. Enter starts; Esc cancels and starts nothing.
Values are not remembered for the next run and are not written to the output or log.

### Base files

`"base": "shared/cmdr.json"` (top level) imports another config file, which may import another in turn.
The groups of the deepest base file are shown first, then each importing file's. Groups are not merged:
the same group name may appear in different files.

Each file's settings (`bufferLines`, `logsDir`, and the directory relative paths resolve against) apply
to its own groups. Groups from base files are read-only in the UI: they can be run, stopped and logged,
but not edited, and their header shows the file they come from. Ctrl+S saves only the opened file.
Saving keeps the file's permissions and group, and a config opened through a symbolic link is saved to the link's target.
On Linux, saving a file owned by another user (that you can write) replaces it with a file owned by you; set
`CMDR_KEEP_OWNER=1` to write such files in place instead, which keeps their owner but is not crash-safe.
When the folder doesn't allow replacing the file (for example it is read-only, but the file is writable), the file is
overwritten in place. When cmdr runs as root, files and folders it creates (logs, a new config) are owned by the
user who ran sudo, or by the folder's owner without sudo.

## Running commands

### Status

Each command takes two lines: its status glyph and name, and below it the start time and state.

| Glyph | State |
|---|---|
| `·` | Not started. |
| `…` | Queued in a sequence. |
| `⧗` | Running; with `ready`, starting or busy. |
| `●` | Ready (see [Readiness](#readiness)). |
| `◌` | Stopping. |
| `✓` | Exited with code 0. |
| `✗` | Failed: non-zero exit, or failed to start. |
| `∎` | Stopped by you (any exit code). |
| `⚠` | Exited, but processes it started are still running. |

Starting a command again clears its previous output (the log file keeps everything).

### Sequences

In a sequential group, the commands run one after another in file order; each starts when the previous
one exits with code 0. A failure (non-zero exit, failure to start, or being stopped) cancels the rest.
Nested groups take part as single steps: a nested parallel group starts all its items and counts as done
when they have all succeeded. Only one sequence runs per group at a time.

Stopping the running command of a sequence cancels the sequence. Stopping a queued command cancels the
waiting commands; the running one keeps going.

### Stopping

Ctrl+C stops gracefully, without asking. Esc does not stop anything. A graceful stop
uses the command's `stop` method; the command shows `◌` until it exits. If it doesn't, Enter on it
force-kills it (and anything it left over). cmdr never force-kills on its own.

### Leftover processes

When a command's main process exits but processes it started are still running, the command shows `⚠`
and can't start again until they are gone; Enter force-kills them. If cmdr can't count them, it shows
`? left` and treats them as still running until a count succeeds.

Output that such processes write after the command is started again is not shown; cmdr writes
`discarding output from processes left over by the previous run` once instead.

Some tools leave processes running on purpose, such as `dotnet build` (MSBuild nodes) or Gradle (its
daemon). For those, set `"leftovers": "detach"`: when the main process exits on its own or after a
graceful stop, cmdr writes `N leftover process(es) detached` and lets them be. The command can start
again at once, and quitting cmdr leaves them running. A force kill still kills everything, and also cancels
a detach that is still waiting.

### Readiness

Long-running commands such as dev servers or `dotnet watch` can tell cmdr when they are ready:

```json
"ready": { "pattern": "Application started|changes applied", "busy": "File updated" }
```

Both are regular expressions matched against each output line (without colors). A command starts as
starting (`⧗`), becomes ready (`●`) when a line matches `pattern`, and goes back to starting when a line
matches `busy`, e.g. on a rebuild. `busy` is optional.

## Screen and keys

The top row has the groups and commands on the left and the focused item's info (command line, working
directory, environment, description) on the right. Its height follows the list, up to 60% of the window,
and is at least five rows. Below it, across the full width, is the output of the focused command. The info
pane and the output pane can be scrolled, and text in them can be selected with the mouse.

`+` or `=` maximizes the output pane over the whole screen and hides the list and the info pane. The same
key brings the layout back.

Commands act on the marked commands, or the focused command if none is marked. On a group header they
act on everything in the group. Marks are limited to one group. In the info and output panes, Enter, Ctrl+C,
F5, L, F2 and Del act on the command the panes show (the focused command) alone, ignoring marks; on a group
header they do nothing there.

| Key | Action |
|---|---|
| Enter | Start (or force-kill what is stopping or left over) |
| Ctrl+C | Stop gracefully |
| F5 | Restart, with confirmation. Pressed again while that restart still waits for the command to stop, it asks: keep waiting (Enter), or force kill now so the restart goes ahead |
| Space, Shift+↑/↓, Ctrl+click | Mark commands |
| Ctrl+A | Mark / unmark all in the group |
| ← / → | Collapse / expand a group |
| Alt+↑ / Alt+↓ | Move a command or group among its siblings |
| Tab / Shift+Tab | Move focus: list → info → output |
| + / = | Maximize the output pane, or restore the layout (hides the list and the info pane while maximized) |
| c | Copy: the command line, with its directory and environment (list; PowerShell syntax on Windows — see below); the selection, else the command line (info); the selection, else the current line (output). The status line names what c copies |
| L | Turn the log file on or off for the focused command |
| / or Ctrl+F | Search the focused pane (see below) |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+N | Add a command |
| Ctrl+G | Add a group |
| F2 | Edit the focused command or group |
| Del | Delete the focused command or group, with confirmation |
| Ctrl+S | Save |
| Ctrl+Q | Quit |

In the output pane, ↑/↓, PgUp/PgDn and Home move the cursor line, and End goes back to following new
output. The mouse wheel scrolls; at the bottom it moves the cursor down, and following resumes when the
cursor reaches the last line; Shift+↑/↓ selects lines and Esc clears the selection. Copying uses the terminal's clipboard
escape (OSC 52); Windows Terminal, iTerm2, kitty and most modern terminals support it (in tmux, enable
`set-clipboard`).

### Search

`/` or Ctrl+F opens a search bar above the status line. It searches the pane that has the focus: the names of the groups
and commands in the list, the info pane's text, or the output (every retained line, partial lines included). Search only
highlights; it does not filter. All matches are highlighted in yellow, and the current match in cyan, scrolled into view.
In the output pane, moving to a match pauses following (End resumes it). In the list it focuses that row, expanding
collapsed groups when the match is inside one.

The bar shows the query, three options and the match count (`3 of 17`):

| Key in the bar | Action |
|---|---|
| typing | Search as you type, from the current match (or the top of the pane) |
| Alt+C / Alt+W / Alt+R | Toggle case sensitive (`Aa`), whole word (`Word`) and regular expression (`.*`). The options can also be clicked |
| F3 / Shift+F3 | Next / previous match; the search wraps at the end and the status line says so |
| Enter, Tab | Back to the pane; the bar and the highlights stay, and F3 / Shift+F3 keep working there |
| Esc | Close the search |

While the bar has the focus it takes every key except Ctrl+Q and Ctrl+S. The mouse wheel still scrolls the panes, and a
click moves the focus to a pane. Moving the focus to another pane moves the search there. Esc in a pane clears its
selection first and then closes the search. Closing removes the highlights and collapses again the groups the search
expanded, except the ones that hold the focused row. F3 with the bar closed brings back the last search.

Whole word means no letter, digit or `_` right before or after the match, so a query like `-v` works too. Regular
expressions use .NET syntax.

The copied command line also sets the command's working directory and the environment variables cmdr sets for it (the
values the info pane shows), for that command only: pasting it leaves the shell's directory and variables as they were. On
Linux it runs in a subshell, `(cd '/dir' && env -u REMOVED 'NAME=value' program args)`; without environment entries the
`env` part is left out.

On Windows the copied command line is for PowerShell. It saves the variables it changes, moves to the directory, sets the
variables and runs the command, then restores the variables and the directory in a `finally` block:
`& { $saved = @{}; foreach ($name in @('NAME')) { ... }; Push-Location -LiteralPath 'C:\dir'; try { [Environment]::SetEnvironmentVariable('NAME', 'value'); program args } finally { ... } }`.
PowerShell passes the arguments of batch files (`.cmd`/`.bat`, such as `npm.cmd`) without quoting them for cmd.exe, so
when a batch-file command has an argument with `& | < > ^ % ! ( )`, a quote, or nothing in it, the copy runs it through
cmd.exe instead, exactly as cmdr does: the cmd.exe command goes in the variable `CMDR_CMD` (restored afterwards like the
others) and the command is `cmd /d /v:off /c '%CMDR_CMD%'`. An argument that is a literal `--%` is copied as an array
after the program, `& program @('...', '--%')`, because PowerShell would otherwise stop parsing its arguments there.

Quitting with commands still running offers to stop them and wait, to force-kill them, or to cancel.

## Platform notes

### Linux

Each command runs in its own session and process group, without a terminal. Programs that insist on a
terminal (password prompts from `sudo` or `ssh`, for example) fail with an error in the output.

### Windows

Each command runs in its own hidden console, inside a job object from the moment it is created, so all of its
processes (even ones it starts immediately) are tracked and are killed if cmdr itself dies. Requires Windows 10
version 1607 or later.

- `.cmd`/`.bat` programs (`pnpm`, `npm`, `yarn`, …) run through `cmd.exe`. After Ctrl+C, cmd asks
  "Terminate batch job (Y/N)?"; cmdr answers it.
- A batch-file command with an argument that contains a line break does not start: cmd.exe cannot pass a line break on
  to the batch file, so cmdr reports the argument's number and refuses.
- A batch-file command whose working directory is a UNC path (`\\server\share`) does not start: cmd.exe would silently
  run it in `C:\Windows`. Map a drive letter to the share and set `cwd` to that path.
- Ctrl+C reaches only the processes attached to the command's console; a child that opens its own console window
  doesn't get it. Force kill still ends the whole process tree.
- `dotnet watch` ignores Ctrl+C when its input is redirected, which it is by default. Set
  `"stdin": "console"` on such commands. Nothing can type into that console, so also stop `dotnet watch`
  from asking questions: add `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1` to its `env`, or pass
  `--non-interactive`. `stdin: console` can't be combined with `stop.method: input`.

## License

[MIT](LICENSE)
