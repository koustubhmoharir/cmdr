# cmdr

A terminal UI for running and watching the commands of a project: dev servers, watchers, builds and
one-off tools. Commands are defined in a JSON file, grouped, and run in parallel or in sequence. Each
command's output is kept and shown in color, and every command's whole process tree is supervised, so
stopping a command stops everything it started.

Runs on Linux and Windows.

This repository also contains **logvu**, a terminal viewer for rotated application logs (see [docs/logvu.md](docs/logvu.md)),
and **cfgedit**, a tree editor for XML, JSON, `.env`, TOML and YAML config files that changes only what you edit (see [docs/cfgedit.md](docs/cfgedit.md)).

```
╭┤cmdr.json├───────────────────╮╭┤servers / web├─────────────────────────────╮
│▾ cmdr.json                   ││                                            │
│  ▾ build  [sequential]       ││$ pnpm start                                │
│    ✓ restore                 ││cwd: /home/me/project/web                   │
│      10:40:02  exit 0 · 0:04 ││FORCE_COLOR=1                               │
│    ✗ build                   ││                                            │
│      10:40:06  exit 1 · 0:11 ││                                            │
│  ▾ servers  [parallel]       ││                                            │
│    ● web                     ││                                            │
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
folder. `./publish.ps1` (PowerShell) publishes cmdr, logvu and cfgedit for the current platform to `publish/<rid>/` and installs
them: on Windows it adds that folder to the user PATH, on Linux it links `~/.local/bin/cmdr`, `~/.local/bin/logvu` and
`~/.local/bin/cfgedit` to
the executables. `-All` also publishes for `win-x64` and `linux-x64`; `-NoInstall` skips the install. On first run it unpacks one small native library of a dependency to `%TEMP%\.net\Cmdr\` on
Windows or `~/.net/Cmdr/` on Linux (override with `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).

Run the tests with `dotnet test`.

The version is set by hand in `Directory.Build.props`.

## Usage

```sh
cmdr [--edit] <config.json>
cmdr
cmdr --version
```

`cmdr <config.json>` opens the config in run mode: commands can be started, stopped and restarted, and the tree
cannot be edited. `cmdr --edit <config.json>` (or `cmdr <config.json> --edit`) opens it in edit mode: groups and
commands can be added, changed, moved and removed, and nothing can be started. Ctrl+E switches between the two modes
in the window. Switching to edit mode is refused, with a message, while commands are running or have leftover
processes, while a sequence runs, while a restart is pending, or while environment scripts run before a start; stop
them (Ctrl+C) and try again.

If the config file does not exist, `cmdr <config.json>` asks whether to create it. Yes opens an empty list in edit
mode, and the first save (Ctrl+S) creates the file. No shows the list of recent files. `cmdr --edit` with a missing
file starts with an empty list, without asking.

Every config opened is added to a list of recently used files (the last 20), kept in
`%LOCALAPPDATA%\cmdr\recent.json` on Windows and `~/.local/share/cmdr/recent.json` on Linux
(`$XDG_DATA_HOME/cmdr/recent.json` when set). `cmdr` with no arguments shows that list, numbered: type a
number to select an entry, Enter to open it in run mode, F2 to open it in edit mode, Del to remove it from the
list, Ctrl+O to open a config through a file dialog (it opens in run mode and is added to the list; its last row lists
Alt+←/→ back/forward, Ctrl+N new folder, Ctrl+R rename and Del delete), Esc or Ctrl+Q to quit. The selected row
has a `>` in front and is bold; the directory is grey, and the name of the git
repository the file is in is blue. Files that no longer exist are marked `(not found)`. Enter on one asks whether to
create it, as above; F2 opens it in edit mode with an empty list. With no recent files, `cmdr` shows the empty picker,
which says that Ctrl+O opens a config.
Commands and groups can be added and edited in the UI (in edit mode) or in the file.
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
| `env` | | Environment variables for every group of this file (see [Environment](#environment)); `${cwd}` is the file's folder. |
| `baseEnv` | | Names to take from the base file's top-level environment (its `env` and `envScript`); needs `base` (see [Base files](#base-files)). |
| `envScript` | | A script whose output adds environment variables for every group of this file (see [Environment scripts](#environment-scripts)). |
| `secret` | | Names of variables whose values are never shown or copied (see [Secrets](#secrets)). |

### Groups

| Setting | Default | Meaning |
|---|---|---|
| `name` | | Required. Unique (case-insensitive) among its siblings. |
| `description` | | Shown in the info pane. |
| `mode` | `parallel` | `parallel`: Enter starts all items at once. `sequential`: items run one after another (see [Sequences](#sequences)). |
| `expanded` | `true` | Whether the group starts expanded when the config is loaded. Collapsing it in the UI doesn't change the setting. |
| `cwd` | | Working directory for everything inside; relative to the parent group's (top level: the config file's directory). May use placeholders, but not `${cwd}` or inputs (see [Environment](#environment)). |
| `env` | | Environment variables for everything inside (see [Environment](#environment)). |
| `envScript` | | A script whose output adds environment variables for everything inside (see [Environment scripts](#environment-scripts)). |
| `secret` | | Names of variables whose values are never shown or copied, for everything inside (see [Secrets](#secrets)). |
| `items` | | Commands and nested groups, in display order. An item with `program` is a command; an item with `items` is a group. Groups nest at most 16 levels deep. |

### Commands

| Setting | Default | Meaning |
|---|---|---|
| `name` | | Required. Unique (case-insensitive) among its siblings. |
| `description` | | Shown in the info pane. |
| `program` | | Required. The executable. On Windows, `.cmd`/`.bat` shims such as `pnpm` work; a program not found in the working directory or on the command's `PATH` fails to start. |
| `args` | `[]` | Arguments, one per array element (no shell quoting). May contain [input placeholders](#input-placeholders) and `${env:NAME}`, `${cwd}`, `${configDir}`, `${pathsep}` and `$${` (see [Environment](#environment)). |
| `cwd` | | Working directory; relative to the group's. May use placeholders, but not `${cwd}` or inputs (see [Environment](#environment)). |
| `env` | | Environment variables (see [Environment](#environment)). |
| `secret` | | Names of variables whose values are never shown or copied (see [Secrets](#secrets)). |
| `stop` | `{ "method": "ctrlC" }` | How to stop gracefully: `ctrlC` (SIGINT on Linux, Ctrl+C on Windows) or `input`, which writes `stop.input` to the command's input (e.g. `"q\n"`). |
| `log` | `{ "enabled": false }` | `enabled`: also write the output to a log file. `path`: the file, relative to `logsDir`; defaults to `<logsDir>/<group>/…/<command>.log`. No two commands may resolve to the same log file (e.g. `a b` and `a_b`), across base files too. |
| `leftovers` | `track` | What to do with processes still running after the command's main process exits (see [Leftover processes](#leftover-processes)). |
| `ready` | | Output patterns that tell when the command is ready (see [Readiness](#readiness)). |
| `stdin` | `pipe` | Windows only: `console` gives the command its own console as input (see [Windows](#windows)). |

The strings passed to the process (`program`, `args`, `cwd`, `env` names and values, `stop.input`, `log.path`,
and the `cwd` and `env` of groups) may not contain NUL characters; such a configuration is reported as an error.

### Environment

`env` values are added to the environment cmdr was started with; `null` removes a variable. The levels apply in
this order: the file's own level (the names of its `baseEnv`, then the output of its `envScript`, then its top-level
`env`), then for each group, outermost first, the output of its `envScript` and then its `env`, and finally the
command's own `env`. Each level applies to everything inside it.

Values can refer to other values:

| Write | Means |
|---|---|
| `${env:NAME}` | The variable's value from the level above (empty if unset), e.g. `"PATH": "/my/bin${pathsep}${env:PATH}"`. `NAME` may contain any characters except `}`, `=` and NUL, e.g. `${env:ProgramFiles(x86)}`. |
| `${cwd}` | The working directory of the group or command that defines the value. |
| `${configDir}` | The directory of the config file. |
| `${pathsep}` | `:` on Linux, `;` on Windows. |
| `$${` | A literal `${`. |

Any other `$` is literal. A bare `${NAME}`, such as `${PATH}`, is reported as an error (write
`${env:NAME}`), and so is any other `${...}` that is not in the table above or is malformed (for example
`${env:}` or an unclosed `${`). Such text is used as written. Env values may also contain
[input placeholders](#input-placeholders).

A `cwd` (of a group, a command or an environment script) takes the same placeholders except `${cwd}` and inputs, which
are config errors. Its `${env:NAME}` sees the environment where the item is defined, never the item's own level: a
group's cwd sees the file's levels and the groups above it, not its own `env` or script output; a command's cwd sees
its groups' levels, not its own `env`; an environment script's cwd sees what the script sees. A variable that is not
set is an error when the item starts: the command shows `failed to start`, and a script fails like any failing script
(the info pane shows the cwd as written until then).

Arguments take the same placeholders, expanded once when the command starts. `${env:NAME}` in an argument is the
variable's value in the command's final environment, after all the `env` levels, including its own. `${cwd}` is the
command's working directory. A value that an input supplies is not expanded again: an input value `${env:X}` is passed as written.
The info pane shows the arguments as written; the copy (`c`) shows them expanded, with input placeholders as written.

Programs usually turn color off when their output isn't a terminal. To keep colors, set the program's
variable, e.g. `FORCE_COLOR=1`, `CLICOLOR_FORCE=1` or
`DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1`.

### Environment scripts

An `envScript` (at the top level of a file, or on a group) runs a program that collects values, such as a
token or a service address, and prints them as one JSON object. Its variables apply to everything inside, like
an `env` written there. PowerShell 7 works on both platforms:

```json
"envScript": {
  "program": "pwsh",
  "args": ["-NoProfile", "-NonInteractive", "-File", "${configDir}/scripts/env.ps1"],
  "rerun": "eachStart"
}
```

```powershell
# scripts/env.ps1
$token = (az account get-access-token --query accessToken -o tsv)
@{ API_TOKEN = $token; API_URL = "https://api.example.test" } | ConvertTo-Json -Compress
```

| Setting | Default | Meaning |
|---|---|---|
| `program` | | Required. The executable, found as a command's would be. |
| `args` | `[]` | Arguments, with the same placeholders as a command's; `${cwd}` is the script's working directory. |
| `cwd` | | Working directory; relative to the group's (top level: the file's folder). May use placeholders, but not `${cwd}` or inputs (see [Environment](#environment)). |
| `timeoutSeconds` | `30` | The script is killed, and the start fails, when it runs longer. |
| `rerun` | `once` | `once`: the result is kept for the session and reused. `eachStart`: the script runs for every start (Enter or F5). |

The output must be a single JSON object whose values are strings, or `null` to remove a variable. Anything else
(other text, a number, an array) fails the script. What the script writes to stderr is kept for the error message.

The script runs when a start needs it: Enter or F5 runs the scripts of everything it is about to start, then
starts. While they run, the commands show `preparing env…`, and Ctrl+C on them cancels the start. F5 runs the
scripts before it stops anything, so a failing script leaves running commands as they are. One run serves every
command of that start, including the commands a sequence starts later. A script sees the environment of its
position: a group's script sees the variables of the file and of the groups above it (including their scripts),
but not its own group's `env`, which can in turn use the script's values with `${env:NAME}`.

A `once` result is reused while nothing it depends on changes: its settings, its environment and the input
values its arguments use. When one of them changes, the script runs again at the next start. Ctrl+R on a group or
command makes the scripts it depends on run again at the next start. A failed run is never reused. When a
script fails, nothing of that start is started, and the error and the last lines of its stderr are written to the
output of the commands that needed it.

The info pane shows a script, when it last ran, how long it took and the variables it produced (or its error),
and a command's environment includes the variables of the scripts that have run. The values a script produces
are used as they are: they are not expanded.

### Secrets

`secret` lists names of variables whose values cmdr never shows or copies, for example `"secret": ["API_TOKEN"]`.
It can be set at the top level of a file, on a group and on a command, and applies to everything inside. A name
taken from a base file with `baseEnv` stays secret when the base file marks it secret.

The info pane shows a secret value as `••••`. The copy (`c`) leaves out the assignments of secret variables (the
pasted command uses the value in your shell, if any) and copies an argument that uses a secret variable as written,
without expanding it; the status line says so. cmdr cannot hide what a program itself prints, so secret values can
still appear in its output and log file.

### Input placeholders

An argument can ask for a value when the command starts: `${input:NAME}` needs a non-empty value,
`${input:NAME?}` allows an empty one. Placeholders can sit anywhere inside an argument
(`--db=${input:db-name}`) or inside an env value (`"DB_NAME": "${input:db-name}"`). NAME may use letters, digits, `-`, `_` and `.`. Write `$${` for the
literal text `${`, e.g. `$${input:db-name}`.

Before Enter or F5 starts anything that needs inputs, one dialog asks for every distinct name, in the arguments and
the env values of the commands and their groups. A name used by several commands of the same start gets one value. Enter starts; Esc cancels and starts nothing.
Values are not remembered for the next run and are not written to the output or log.

### Base files

`"base": "shared/cmdr.json"` (top level) imports another config file, which may import another in turn.
The groups of the deepest base file are shown first, then each importing file's. Groups are not merged:
the same group name may appear in different files.

A base file's top-level `env` and `envScript` apply only to its own groups. To give the importing file's commands
some of their variables, list the names in `baseEnv`, e.g. `"baseEnv": ["SHARED_HOME"]`. Each listed name takes the
value it has in the base file's top-level environment: cmdr's environment with that file's own imports, script
output and `env` applied. A name the base file removes is removed; a name it does not touch keeps the value cmdr
was started with. Names that are not listed are not imported. When an imported name comes from the base file's
`envScript`, that script runs before the importing file's commands start.

Each file's settings (`bufferLines`, `logsDir`, and the directory relative paths resolve against) apply
to its own groups. Groups from base files are read-only in the UI: they can be run, stopped and logged,
but not edited, and their file's row in the list is marked `(base)`. Ctrl+S saves only the opened file.
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

The list starts with a file row for each config file of the chain, in chain order: the base files first, the opened
file last. A file row shows the file's path (`▾`, or `▸` when collapsed), and a base file's row ends with a faint
`(base)`. The file's groups and commands are listed below its row, one level deeper. A file row collapses and expands
like a group header (← / →, or a click on its `▾` / `▸`); a file without groups still has its row. Its info pane shows the
file's `base`, `bufferLines`, `logsDir`, `baseEnv`, `envScript` and `secret` as written, and the environment it sets.

The top row has the groups and commands on the left and the focused item's info (command line, working
directory, environment, description) on the right. In run mode its height follows the list, up to 60% of the window,
and is at least five rows, and below it, across the full width, is the output of the focused command. Edit mode has
no output pane: the list and the info pane fill the window, and the list's title shows `[edit]`. The info pane and the
output pane can be scrolled, and text in them can be selected with the mouse.

`+` or `=` maximizes the output pane over the whole screen and hides the list and the info pane. The same
key brings the layout back.

In run mode, commands act on the marked commands, or the focused command if none is marked. On a group header they
act on everything in the group. Marks are limited to one group, and switching to edit mode clears them. In the info and
output panes, Enter, Ctrl+C, F5, L and Del act on the command the panes show (the focused command) alone, ignoring marks; on a
group header they do nothing there. In edit mode, Enter, F2 and Del in the info pane act on the command it shows.
On a file row the list's command keys (Ctrl+C, F5, L, Del, Ctrl+R, Space, Ctrl+A, c and Alt+↑↓) do nothing, without a message.
Enter and F2 do nothing in run mode; in edit mode on the opened file's row they edit its top-level settings, and on a base
file's row they say to edit that file. Ctrl+N and Ctrl+G add a command or a top-level group to the opened file, as they do
with no row focused.

A key of the other mode does nothing but say which mode it needs (Ctrl+E switches).

The key labels on the bottom row are buttons: a click on one performs its key, as if it were typed, and a click elsewhere on the row does nothing. The bottom row shows only the keys that do something in the focused pane, for the current mode: in run mode Space and L only on a command row of the list, Ctrl+A only in the list on a group or command row, the command keys in the info or output pane only when a command is shown there, and Ctrl+S only when there are unsaved changes.

Both modes:

| Key | Action |
|---|---|
| Ctrl+E | Switch between run mode and edit mode. Switching to edit mode is refused while commands are running or have leftover processes, a sequence runs, a restart is pending, or environment scripts run before a start |
| ← / → | Collapse / expand a file or group |
| Click the ▾ / ▸ of a file or group header | Collapse / expand it |
| Tab / Shift+Tab | Move focus: list → info → output (edit mode: list → info) |
| c | Copy: the command line, with its directory and environment (list; PowerShell syntax on Windows — see below); the selection, else the command line (info); the selection, else the current line (output). The status line names what c copies |
| / or Ctrl+F | Search the focused pane (see below) |
| F3 / Shift+F3 | Next / previous match |
| Ctrl+S | Save |
| Ctrl+Q | Quit |

Run mode:

| Key | Action |
|---|---|
| Enter | Start (or force-kill what is stopping or left over) |
| Ctrl+C | Stop gracefully (cancels a start that is still running environment scripts) |
| F5 | Restart, with confirmation. Pressed again while that restart still waits for the command to stop, it asks: keep waiting (Enter), or force kill now so the restart goes ahead |
| Space, Shift+↑/↓, Ctrl+click | Mark commands |
| Ctrl+A | Mark / unmark all in the group |
| + / = | Maximize the output pane, or restore the layout (hides the list and the info pane while maximized) |
| L | Turn the log file on or off for the focused command |
| Ctrl+R | Run the environment scripts again at the next start (see [Environment scripts](#environment-scripts)) |
| Del | Clear the output (the log file is not affected) |

Edit mode:

| Key | Action |
|---|---|
| Enter, F2 | Edit the focused command or group. On the opened file's row, edit its top-level settings (`env`, `baseEnv`, `secret` and `envScript`) |
| Ctrl+N | Add a command |
| Ctrl+G | Add a group |
| Del | Delete the focused command or group, with confirmation |
| Alt+↑ / Alt+↓ | Move a command or group among its siblings |

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
collapsed files and groups when the match is inside one. A file row matches on its path.

The bar shows the query, three options and the match count (`3 of 17`). An option that is on is highlighted in cyan.
While the query field has the focus the status line lists the keys of the search; once the focus is back in a pane, the
bar shows `F3 next · Shift+F3 prev` in place of the options.

With a selection within one line in the info or output pane, `/` or Ctrl+F puts the selected text in the field (escaped
when `.*` is on) and starts at the selected occurrence; the selection stays. Otherwise the field shows the last query.

| Key in the bar | Action |
|---|---|
| typing | Search as you type, from the current match (or the top of the pane) |
| Alt+C / Alt+W / Alt+R | Toggle case sensitive (`Aa`), whole word (`Word`) and regular expression (`.*`). The options can also be clicked |
| F3 / Shift+F3 | Next / previous match; the search wraps at the end and the status line says so |
| ↑ / ↓ | Previous / next search term of this session; ↓ past the newest brings back the text being typed |
| Enter, Tab | Back to the pane; the bar and the highlights stay, and F3 / Shift+F3 keep working there |
| Esc | Clear the query; with an empty query, close the search |

A term is kept in the history when you leave the field (Enter, Tab), press F3 / Shift+F3, clear it with Esc, or close
the search (the last 50, without duplicates).

While the bar has the focus it takes every key except Ctrl+Q and Ctrl+S. The mouse wheel still scrolls the panes, and a
click moves the focus to a pane. Moving the focus to another pane moves the search there. Esc in a pane clears its
selection first and then closes the search. Closing removes the highlights and collapses again the files and groups the search
expanded, except the ones that hold the focused row. F3 with the bar closed brings back the last search (the last
non-empty query, even after Esc cleared it).

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

[MIT](LICENSE). The published executables include third-party components under their own licences; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
