# cmdr — design

Implementation notes. What cmdr does for its users (configuration, behaviour, keys) is described in the
[README](../README.md); this document covers how it is built and why.

A cross-platform (Linux, Windows) TUI for managing a set of non-interactive process trees.
.NET 10, Terminal.Gui v2.

## Solution layout

```
Cmdr.slnx
Directory.Build.props   version (maintained by hand)
src/Cmdr.Core/          config, process supervision, output buffering/parsing, logging (no UI)
src/Cmdr/               Terminal.Gui app
tests/Cmdr.Core.Tests/  xUnit tests for Cmdr.Core
tests/Cmdr.Tests/       xUnit tests for the app's pure UI logic
```

UI logic that can be tested without a terminal lives in pure static classes (`RowLayout`,
`OutputWrap`, `OutputScroll`, `DetailsText`, `StatusColors`, `GroupOptions`,
`CommandForm`, `InputForm`, …), and the selection of pane text is `TextSelection` in Shared.Tui; views only draw and route input.

### Publishing

`Cmdr.csproj` publishes one framework-dependent single file (`PublishSingleFile`,
`SelfContained=false`). Native libraries of dependencies (only `libonigwrap`, pulled in by
Terminal.Gui) are bundled with `IncludeNativeLibrariesForSelfExtract` and extracted by the host on
first run. Both projects embed their symbols (`DebugType=embedded`) so nothing else lands next to the
executable, and `SatelliteResourceLanguages=en` drops Terminal.Gui's other resource languages. The
Ctrl+C helper relaunches the executable via `Environment.ProcessPath`, which works for the single-file
build (`Assembly.Location` would be empty there); see `CtrlCHelperCommand`.

`cmdr --version` prints the `AssemblyInformationalVersion` (from `Version` in `Directory.Build.props`,
without the source-revision suffix).

## CLI

`cmdr [--edit] <config.json>`, `cmdr --version` (parsed by `CommandLine`, pure). `cmdr --send-ctrl-c <pid>` is an
internal helper mode (Windows only, see Process supervision). A config opens in run mode, or in edit mode with `--edit`.
A missing file in run mode is not loaded before the TUI starts: `TuiHost.RunMissingConfig` asks whether to create it
(Yes loads it in edit mode, No always shows the picker). `TuiHost` owns the session on every path
and disposes it when the main window closes. In the main window Ctrl+E switches modes (`MainController.SwitchMode`);
`ApplyLayout` is the one place that lays the window out for the mode (edit mode hides the output header and output, and
the top row fills the window), the maximize state and the search bar. The hint bar (`HintsLine` in Shared.Tui) is built per mode
and is drawn in `BarColors`, the one bar style that cmdr and logvu share.

`cmdr` with no arguments shows the recent-configs picker, even when the list is empty (`RecentPicker<Session>` in Shared.Tui,
built by `TuiHost.CreatePicker` with `openFile: true`; rows from `RecentRows`, digit selection in `NumberTyping`). Ctrl+O shows
a file dialog for a config (run mode, recorded in the list). The picker and the main window run one
after the other on the same `IApplication` (`TuiHost.RunPicker`). Enter loads the selected config in run mode and F2 in edit
mode, through the picker's alternate open (`PickerOpen`). Enter on a missing file asks whether to create it (No stays in the
picker, Yes loads it in edit mode); F2 opens it without asking. A load error is shown there and the picker stays, and a load
the user cancels shows nothing. The picker records the file it opens and stops, and the main window runs next. The list's
type-to-search (`KeystrokeNavigator`) is off so digits reach the picker. Rows are data (`RecentRow`, from `RecentRows.Build`,
which takes the outermost repository of a directory from `RepoRoot.Outermost`). `RecentRowsSource` draws them: a two-column
gutter (`> ` on the selected row, which is bold), no focus background on any row, the number faint, the directory grey, and
only the name of the repository's own folder in the directory blue (`PickerColors`).

`RecentFiles` (Shared.Core) is the store: `<LocalApplicationData>/cmdr/recent.json`, `{ "files": [...] }`, most
recent first, at most 20, full paths compared like base-file paths. Every successful load is recorded
(`cmdr <path>` and the picker), including files that don't exist yet. Reading never fails (a bad store
reads as empty) and a failed save is ignored: the list is a convenience and must never stop cmdr from
starting. Concurrent instances: last writer wins.

## Config

- `ConfigStore` loads with System.Text.Json source generation (`ConfigJsonContext`): camelCase, comments
  and trailing commas allowed, enums as strings only (integers rejected). After deserializing,
  `Normalize` replaces JSON `null`s in non-nullable places with defaults and rejects null list entries.
  The legacy `commands` key is rejected with a message pointing to `items`.
- `ItemSpecJsonConverter`: an item with `program` is a `CommandSpec`, otherwise a `GroupSpec`.
- Defaults are not written on save (`expanded: true`, `leftovers: track`, `stdin: pipe`, null
  `ready`/`base`/`cwd`/`env`), so saving does not churn existing files.
- Loading does not validate; `CmdrConfig.Validate` returns messages and the UI decides (load errors are
  shown, save is refused while invalid). Names are checked per file, case-insensitively among siblings.
- Saving (`AtomicFile`, also used for the recent-files store) writes a temp file next to the target and
  moves it over (atomic replace). A symlink is followed to its final target (`PhysicalPath`, which resolves
  links component by component, so `..` after a directory link goes to the link target's parent), which is
  replaced in its own directory so the link stays; a dangling link or a loop is an `IOException`. On Unix the
  temp file gets the existing file's mode before any content is written; a new file gets the OS default. On
  Windows an existing file is replaced with `File.Replace` to keep its ACL (unverified on Windows). The final
  permissions never change: an existing file is overwritten in place instead (not atomic) when this process
  does not own it on Unix (a rename would change the owner), or when creating the temp file or replacing the
  file is denied (a read-only directory, a sticky directory, a Windows ACL allowing writes but not deletes).
  A temp file that cannot be deleted is left behind rather than hiding the save's outcome.
- Groups nest at most `CmdrConfig.MaxGroupDepth` (16) levels, a top-level group being level 1. Loading a
  deeper file is a `ConfigException`; `Validate` reports it, and `AddGroup`/`UpdateGroup` refuse to
  exceed it.

### Base files

`ConfigStore.LoadChain` follows `base` imports (relative to the importing file), deepest base first, the
opened file last. Cycles are detected on full paths (case-insensitive on Windows and macOS). A missing
opened file yields an empty config; a missing base file is an error. `ValidateChain` validates each file
on its own and prefixes base-file errors with the path relative to the opened file, then checks the log
paths of every command of the chain together (`LogPathErrors`).

In the session each top-level group records its `ConfigSource` (path, directory, `bufferLines`,
`logsDir`, `IsBase`); descendants share it. Launch specs, buffer sizes and log paths use the item's
source. Every edit goes through `EnsureEditable`, which throws
`'<name>' comes from <path>; edit that file.` (`ItemEntry.ReadOnlyMessage`) for base items or base
targets. Top-level moves of opened-file groups stop at the first opened-file group. `BuildConfig` and
`Save` include only the opened file's groups and keep its raw `base`.

### Environment and paths

- `LaunchSpec.From(ancestors, spec, configDir, fileLevels)` resolves `cwd` through the group chain and collects
  the levels outermost first: the command's file (`ConfigSource.EnvLevels`, see below), one `EnvLevel` per group
  with `env`, then the command's own. The file level is an import level (`EnvLevel.ImportFrom` set, from the base
  file's `EnvLevels`, its keys the `baseEnv` names with null values) when `baseEnv` is non-empty, then the file's
  top-level `env` level. `EnvExpansion.ApplyLevels` applies them outermost first; each level's values expand
  against the environment as it was before that level (so `${env:PATH}` is the parent's value). An import level
  applies the imported levels to a copy of the environment at that point, then sets each name that the copy has
  and removes each name it lacks; its own keys are not expanded. Each `ConfigSource` is built after its base's
  source, so the chain is resolved without a lookup. `${cwd}` is the effective cwd of the item that defines the
  value (the file's folder for the file level). Expansion is `Placeholders.Expand`, the one placeholder syntax
  (`${env:NAME}`, `${input:NAME}`, built-ins, escape `$${`).
- Expansion happens at launch, not when the spec is built. `Session.StartEntry` checks the input values
  the arguments and env values need (`Placeholders.CheckInputs`), then passes them as `LaunchSpec.Inputs`
  (a private copy, never in `ToString`). The platform's `Prepare` applies the env levels with those inputs,
  then expands each argument with `LaunchSpec.ExpandArgs` against the final environment (on Windows,
  case-insensitively), so `${env:NAME}` in an argument sees the command's own `env`. Each text is expanded in
  one pass: an input value is never scanned again. Display (the info pane's env lines) and the copy pass
  `inputs: null`, which keeps input placeholders as written; the copy also expands the arguments
  (`LaunchSpec.ExpandArgs`), the info pane shows them as written.
- Log path: `<logsDir>/<group>/<subgroup>/…/<command>.log` unless `log.path` is set. Two commands with
  the same log path, or whose log file would be another's folder, are a validation error, whether or not
  their logs are enabled (logs can be toggled at runtime). Paths are compared after resolving symbolic links
  (`PhysicalPath`), so two paths reaching one file through a link conflict too. `Session.Validate`/`Save` report only conflicts
  involving an opened-file command, so a conflict inside a base file does not block saving.

### Input placeholders

`Placeholders` (`Errors`, `Requests`, `Expand`, `CheckInputs`) scans text for `${input:NAME}` /
`${input:NAME?}` (escape `$${`). Malformed placeholders (including a bare `${NAME}`) are validation errors and
copied literally otherwise. The texts are the command's arguments and the env values of its ancestor groups and of its own
(`RequestsFor`, `LaunchTexts`); the values reach the launch as `LaunchSpec.Inputs` and are substituted there in the same single
pass as the other placeholders, so a missing or empty value fails the start before anything launches. `Session.InputsForActivate/
InputsForRestart` mirror the real start decisions (kill-instead-of-start, refused starts, startable
commands) and merge requests by name (required wins over `?`). For restart, the refusal check ignores the runs it
cancels and the commands it stops. Enter decides first: `Session.PrepareActivate`
returns a `PreparedActivation` with the decision (kill the stopping or leftover commands, or start the startable ones),
the commands that were startable then, and the requests for the dialog; `InputsForActivate` is its `Requests`.
`Activate(prepared, inputs)` carries out that decision and starts only the prepared commands that can still start,
so a command that became startable while the dialog was open is not started without its inputs.
`Activate`/`RestartAsync` take the values, freeze a copy and pass it through `StartCommands` / `StartGroup` / `SequenceRun` to every
`StartEntry`, which substitutes into a copy of the args. A missing or empty-but-required value makes
the start fail with a system line; values are never written to the output or log.

## Process supervision

Output via pipes (not a PTY): stdout/stderr redirected and read asynchronously as UTF-8. stdin is a
pipe kept open for the lifetime of the process; it is written to only by the `input` stop method (and
the batch-file answer below) and never closed by cmdr (some programs exit on stdin EOF).

| Action | Linux | Windows |
|---|---|---|
| Launch | `setsid <program> <args…>` → own session/process group, pgid == pid | `CreateNoWindow` (own hidden console), assigned to a Job Object with `KILL_ON_JOB_CLOSE` |
| Stop `ctrlC` | `kill(-pgid, SIGINT)` | spawn `cmdr --send-ctrl-c <pid>`: `FreeConsole` → `AttachConsole(pid)` → `SetConsoleCtrlHandler(null, true)` → `GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0)` |
| Stop `input` | write configured string to stdin | same |
| Force kill (user only, never automatic) | `kill(-pgid, SIGKILL)` | `TerminateJobObject` |
| Leftover detection | live processes in the pgid (`/proc/*/stat`, zombies excluded) after leader exit | job active process count > 0 |

The Ctrl+C helper exists because `GenerateConsoleCtrlEvent` only reaches processes on the caller's
console and a process can be attached to one console at a time: cmdr stays on the user's terminal, so a
short-lived copy of itself attaches to the command's hidden console to send the event.

States: `NotStarted | Running | Stopping(since) | Exited(code) | FailedToStart(message)`, plus
`LeftoverCount` after exit (polled while > 0). If a count fails (the Windows job query can throw), the
failure is contained at the exit/timer callback, the exit is still recorded, and `LeftoverCountUnknown`
is set: the status shows `? left`, a `leftover check failed: …, retrying` line is written once per failure
streak, and the command counts as having leftovers (`HasLeftovers`: no start, quit waits, Enter kills)
until a count succeeds. With `leftovers: detach`, detaching waits for that first successful count. Restart = graceful stop, wait for exit, start.
A pending restart is a `RestartOperation` tracked by the session: Stop/ForceKill of an overlapping command,
StopAll, ForceKillAll and Dispose revoke it, and so does the caller's token. A revoked restart starts nothing. Revocation
is checked under the tree lock before every launch, and once more after a batch; when it fails, what the batch started is
stopped again (a graceful stop is sent in the background, never waited for) and the restart throws. A restart that hands a sequence over to a run keeps
the caller's token in force until the sequence's first command launches: the run's own launch decision (taken under its
lock, right before the command starts) ends that watch, and the restart waits for that decision before it returns, so a
cancellation before the launch throws and starts nothing. After the first launch, stopping the sequence is Stop's or
ForceKill's job, and the caller's token no longer reaches it. F5 registers its restart on
the UI thread (`Session.PrepareRestart`) and only then runs it in the background (`RunRestartAsync`), so a Stop or quit
that comes first revokes it. F5 on commands a pending restart covers (`Session.PendingRestartsOf`) does not start
another restart: it asks "Keep waiting" (default) or "Force kill now", which kills them without revoking the restart
(`ForceKillForPendingRestart`). Each answer of `PendingRestartsOf` names the run the restart waits for, and the kill
applies only to that run while the restart is still pending, so a dialog left open never kills the replacement run.
A sequence's `Result` completes only after the run has left its group. Tracked per
run: PID, `StartedAt`, `ExitedAt`, `StopRequestedAt`, exit code, `StopRequested`, readiness.

- **Stopped vs failed.** `ProcessSnapshot.StopRequested` is set when a graceful stop or force kill was
  requested while the run was active (reset per run). An exited run with it shows `∎ stopped`, whatever
  the exit code (e.g. 255 from cmd.exe after "Terminate batch job"); leftovers still show `⚠`.
  `Session.Succeeded` = not stopped and exit code 0. A graceful stop cancels its sequence directly; a
  force kill, or a stopped parallel branch inside a run, writes `sequence cancelled: stopped`.
  `sequence cancelled: …` lines are written only for commands that are part of a run.
- **Detaching leftovers.** With `leftovers: detach`, at leader exit (unless a force kill was requested
  during the run, or after it while the detach still waits for a successful count) and with live leftovers, `IProcessGroup.Detach` releases them: on Linux the group is
  simply no longer tracked; on Windows `KILL_ON_JOB_CLOSE` is cleared (`SetInformationJobObject` with
  no limits) before the job handle is closed. The detach line is written before the exit marker.
- **`stdin: console` (Windows).** `RedirectStandardInput = false` alone is not enough: .NET's
  `Process.Start` then passes the *parent's* stdin (cmdr's terminal) to the child, which would steal
  cmdr's keyboard input. `WindowsProcessPlatform.Start` therefore nulls cmdr's `STD_INPUT_HANDLE`
  (under a lock) for the duration of `Process.Start` — .NET reads it there — so the child gets a null
  handle and Windows gives it its new console's input. Needed because `dotnet watch` ignores Ctrl+C when
  stdin is redirected on Windows (verified; on Linux SIGINT is unaffected). No effect on Linux.
- **Readiness.** `CommandOutput.ProcessLinesCompleted` reports completed stdout/stderr lines (no system
  or partial lines) after the buffer lock is released, under a separate notify lock that keeps
  notifications in buffer order. `ReadinessMatcher` (compiled regexes, 200 ms timeout) evaluates them on
  the pump thread outside `ManagedProcess`'s lock, then the verdict is applied under it (a run
  check drops lines of an earlier run). Lock order: output notify lock → `ManagedProcess`
  lock; the pumps are started with `Task.Run` so no pump runs on a thread holding the process lock.
- **Run pipes.** Each run owns its redirected pipes and pump tasks (`RunPipes`). Chunks are tagged with
  the output run that `CommandOutput.BeginRun` returned; `CommandOutput` drops chunks of an earlier run
  under the same lock as `BeginRun`, writing `discarding output from processes left over by the previous
  run` once per run. Output of a finished run is still shown (after its exit marker) until the next
  start. Old pumps keep draining rather than closing the pipe, like a terminal, so a detached process
  that writes to it is not killed by SIGPIPE. Starting the next run closes the old stdin; a pump closes
  its stream at EOF; `Dispose` closes every pipe of every run and output stops.

### Windows: launch

`WindowsProcessPlatform.Launch` calls `CreateProcessW` itself (not `Process.Start`) with a STARTUPINFOEX
attribute list: `PROC_THREAD_ATTRIBUTE_JOB_LIST` creates the child inside its kill-on-close job, so nothing it
spawns can escape the job (Windows 10 1607+), and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` limits inheritance to the
child's own pipe ends. Pipe creation until cmdr's client copies are closed runs under `InheritableHandleLock`,
which the Ctrl+C helper's `Process.Start` also takes, so the helper cannot inherit another launch's pipe ends
(third-party launchers in the process are not covered). Console stdin is a null stdin handle. The exit is a
thread-pool wait on the process handle (`WindowsLaunchedProcess`). Linux keeps `Process` (`ProcessLaunched`).
Because the child is created in the job, the host of its hidden console (`conhost.exe`) is in the job too. It
outlives the leader briefly, so `CountLiveProcesses` lists the job's process ids and leaves console hosts out;
otherwise every command would report a phantom leftover at exit.

### Windows: batch files (.cmd/.bat)

`pnpm`, `npm`, `yarn` etc. are `.cmd` shims that CreateProcess cannot run directly. On Windows the
program is resolved against the effective `PATH`/`PATHEXT` (cwd first; a path-like program only against
the cwd; a name with a non-PATHEXT extension is tried as is, last). A program that does not resolve fails
to start; it is never handed to CreateProcess's own search, which would look in cmdr's directory. A
drive-relative program (`C:tools\run`, relative to a per-drive current directory the child does not have) fails to
start with a message saying so; a drive-relative PATH entry is skipped. If it resolves to a
`.cmd`/`.bat`, it is launched as `%ComSpec% /d /v:off /s /c "<escaped command line>"` using cross-spawn's
escaping rules (`/v:off` keeps delayed expansion off whatever the registry enables). A batch file with an argument
containing CR or LF, or whose working directory is a UNC path, is refused with an `ArgumentException` (a "failed to
start" message), because cmd.exe cannot receive or use them. A ComSpec that is missing or not a full path is replaced by `cmd.exe` in the system directory, so the
interpreter is never found by CreateProcess's search (cmdr's directory and current directory come first). Ctrl+C on a batch file makes cmd.exe prompt "Terminate batch job (Y/N)?" on stdin, so
after a Ctrl+C stop of a batch-wrapped command cmdr also writes `Y\r\n` to stdin. Caveat: a child that
reads stdin could consume the answer first. With `stdin: console` the answer cannot be sent; the launch
returns a notice that is written to the output.

**Environment scripts (`EnvScriptRunner`).** An `envScript` (a file's or a group's) runs once through
`LaunchSpec.ForEnvScript`, like a command: its own group or job, stdin closed at once, stdout and stderr read
concurrently. Its stdout must be one JSON object of variables (string or null values, no duplicate names, valid
names); stdout is capped at 1 MiB and stderr keeps its last 16 KiB. The run ends on exit, on its timeout, on
cancellation, or when stdout is too large; each ends by killing the whole tree (a leftover child would hold the
pipes open), and the streams are then read for at most 2 s, so a pipe held by an escaped process cannot hang the
caller. A non-zero exit is a failure whatever the output. The outcome is an `EnvScriptResult`: the values, or a
one-line error that names no value (its stdout is never echoed). `${cwd}` in its arguments is the script's own
working directory.

**Environment scripts in the session (`Session.PrepareEnvAsync`).** The owners of a set of commands are the
scripts their environments reach: a file's `envScript` when the file's own levels reach the command (its own file,
or a base file that a `baseEnv` imports from, recursively; a base without `baseEnv` is not reached), and each
group above the command that has one. Owners run one at a time, base file first and then groups by depth, so
each script sees the values of the scripts that come before it in its chain. A file's script runs in the file's
`baseEnv` import levels only (its own `env` applies after it); a group's script runs in the file's levels with the
values so far and the groups above it (not its own `env`). Script output enters the chain as literal levels
(`EnvLevel.Literal`: set as written, never expanded), placed before the file's `env` and before each group's `env`, so
an `env` value can read `${env:NAME}` from a script and override it. `ScriptValues` maps each owner (by reference) to its
output. `EnvScriptCache` holds the results: a successful `once` run is reused for the same owner while its fingerprint
is unchanged (program, args, working directory, timeout, rerun, the environment it runs in with the inputs, and the
values of the inputs its arguments use); `eachStart` runs are never reused, failures are never reused, and concurrent
requests share one in-flight run. When the last waiter of an unfinished run leaves, the run is cancelled and the script
is killed. `RefreshEnvScripts` drops an item's owners' results; `LastEnvScriptRun` gives the last outcome for display.
A failing script stops the preparation: its label (`file 'cmdr.json'` or `group 'a › b'`), error and the last stderr
lines are written to each command that needs it. The arguments of the owners' scripts are part of the commands' inputs.
The values are prepared before an activation or restart and carried with the inputs (`LaunchValues`) to every launch, sequences
and branches included. A command whose owner has no values fails to start (`env script of ... has not run`) and launches nothing.

## Session tree (runtime)

- `GroupEntry` has `Parent` (null at top level) and ordered mixed `Items`; `Commands`/`Groups` are its
  direct children of each kind. `Session.Groups` is top level only; `AllEntries`/`BuildRows()` walk the
  tree depth-first in file order.
- Only sequential groups own a run (`SequenceRun`, at most one active per group). Its steps are
  commands or child groups: a sequential child runs as a sub-run (registered as that group's active
  run), a parallel child starts all its children; the step waits for the child's result. A parallel
  group started outside any run starts its commands and its sequential children's runs and tracks
  nothing itself.
- Failure (non-zero exit, failure to start, stop, cancellation) fails the step and cancels the
  remainder of every enclosing sequence. Parallel siblings are independent: a failed branch does not
  cancel its siblings; the parallel group itself fails as soon as any child fails.
- Enter on a group: kill-first over the whole subtree (stopping / leftovers); otherwise run it, skipping
  commands that are already running and dropping groups left with nothing to start. Refused with a
  message when the plan overlaps an active run. A sequential group takes no new start (run, command row, enclosing plan or sub-run) while a command below it is running or stopping, e.g. one a cancelled run left running.
- Ctrl+C on a group cancels every run at or below it and gracefully stops everything running below.
  On a command, it cancels the innermost run containing it (which then fails upward) and stops the
  command if it is running; on a queued command, whatever that run is currently running keeps going.
  A run cancelled while a command is being started stops that command only if the cancellation was aimed at it or at the whole run.
  F5 on a group stops everything below, waits for leaders, then runs the group.
- Two modes (`SessionMode`), set by `Session.Load(path, mode)`. Run mode: start, restart, log toggle; the tree
  cannot be edited. Edit mode: groups and commands can be added, changed, moved and removed; nothing can be started.
  Stop, ForceKill, StopAll, ForceKillAll, Save, Validate, BuildConfig, Dispose and queries work in both. Each operation
  checks the mode under `_sync` before it changes anything and throws `InvalidOperationException` otherwise.
  `SwitchToEdit` is refused, with a message and no change, while a command is running, stopping or has leftover processes,
  a command is queued, a sequence run is active, or a restart is pending. `SwitchToRun` is always allowed and keeps
  unsaved changes and `IsDirty`. Reparenting into itself or a descendant is still refused.

## Threading and ownership

Most defects found in review were a decision taken on state that changed before it was used (across a lock release, an
await, a modal dialog or a background dispatch), or state kept on a shared object instead of the run, operation or file it
belongs to. The rules below say who owns what and which identity must travel with delayed work.

| State | Owner and guard | Identity carried across delays |
|---|---|---|
| Views, dialogs, controller fields | UI thread only; background work reports back with `app.Invoke` | — |
| Session tree (items, groups, membership, editing) | `Session._sync`; `Mode` is read and changed under it | The tree is read under the lock at every launch; edits happen only in edit mode, when nothing runs |
| Pending restarts | `Session._sync` (`_restarts`); registered synchronously (`PrepareRestart`) before any background step | `RestartOperation`; `PendingRestart` also names the waited-for run |
| Enter decisions | Taken when Enter is pressed (`PrepareActivate`), before the input dialog | `PreparedActivation` (the start or kill set) |
| Sequence runs | Each run's own lock; registered on its group under `Session._sync` | `SequenceRun`; its `Result` completes only after it left its group |
| A process run (process, group, pipes, readiness, exit) | `ManagedProcess._sync`; disposal of OS objects outside it | Run id (`RunId`, output run): kills, diagnostics, chunks and exits of another run are ignored |
| Output buffer | `OutputBuffer` lock; a chunk's completed lines and new partial are one step | `BufferSnapshot` for an operation that validates then reads (copy) |
| Log files | `CommandOutput._sync`; each file has its own `LogDestination` (progress, running and waiting writer) | The destination; rules are re-applied after the file is opened outside the lock |

Lock order: `Session._sync` → sequence run → `ManagedProcess._sync`; output notify lock → `ManagedProcess._sync`. No lock is
held across an await, a modal dialog, process I/O, a file open, or an event raised to subscribers (subscriber exceptions are
contained and reported as `cmdr error:` lines). Work that outlives a lock either re-checks its identity under the lock before
acting, or captures a snapshot and acts only on that.

## Output

- Ring buffer of lines (`bufferLines`) with absolute line numbers, tagged stdout/stderr; partial lines are kept
  per stream. A line stores its text once (`PlainText`) plus style runs (`StyleRun`: start index and style); a
  line that is one default-styled run shares a static run array. `Spans` (a string per run) is built on demand
  and drawing doesn't use it. SGR parameters are parsed from spans, so color changes allocate nothing. Measured
  (2026-11-15, 10,000-line buffer): about 100 bytes per line plus 2 bytes per character for plain output, about
  410 bytes for a typical colored log line of 87 characters. The buffer is bounded in lines, not bytes: a line can
  reach the 65,536-unit forced-break limit.
- ANSI parser: SGR (16/256/24-bit colors, bold, dim, italic, underline, inverse, reset) is rendered;
  all other escape sequences (CSI, OSC, etc.) are dropped. Bare `\r` (not followed by `\n`) resets the
  current line, so progress bars overwrite in place. A tab pads to the next multiple of 8 display columns.
- Display width (`DisplayWidth`, Shared.Core): measured per grapheme cluster (`StringInfo` text element), with a
  pluggable per-grapheme measure. Core's default is a small table (zero-width marks/format characters,
  East Asian wide and emoji ranges); Shared.Tui's `DisplayWidthSetup` plugs in (its module initializer, which cmdr's `Program` runs first thing, so the measure does not depend on load order)
  Terminal.Gui's measure, so tab expansion, wrapping, truncation, selection and drawing all agree with what
  Terminal.Gui draws. A grapheme is never split by wrapping or selection. The parser's forced break at its
  line-length limit (65,536 UTF-16 units) moves the last grapheme to the next line when the next character
  continues it; only a grapheme that is the whole line (or longer than 64 units) is split, and never inside a
  surrogate pair. A high surrogate at the limit waits for its low surrogate, so the decision is made on the whole
  scalar (a ZWJ emoji part or a second flag half continues the grapheme). Zero-width graphemes (a lone combining
  mark, U+200B) take no column and are not drawn, in the output and the info pane, but stay in the text and in copies.
- Partial lines: the parser keeps the current line in its own arrays, and a published partial
  (`OutputLine.FromPartial`) shares them without copying; its text and runs are built on first read.
  Writes below a published length first move the parser to fresh arrays (copy-on-write), so publishing
  after every chunk is O(1) amortized. Each line has an `Identity`, shared by a partial's snapshots and its
  completed form; the output pane pins a selection end or paused cursor on a partial line by identity plus
  its text as a prefix (`PartialPins`), so a rewritten (`\r`) or evicted partial is dropped rather than matched
  to a later line with the same text. A pin is searched at its line, then one line before it, then after it.
  Copy resolves its pins and extracts the text from one `OutputBuffer.Capture` snapshot, so output that arrives
  meanwhile cannot shift the lines between the two.
- Starting a run clears the in-memory buffer (`OutputBuffer.Clear`); every appended line has already been
  queued for the log with its text, so the log keeps all runs.
- Log file: enabled per command or toggled at runtime; when enabled mid-run the current run's buffer is
  written first, then output streams live. Appends; each run starts with `=== started <timestamp> ===`;
  ANSI codes are stripped.
- Log writing happens on a background writer per log session (`CommandOutput.LogWriter`), so nothing
  waits for log I/O: not the pumps, not snapshots, not force kill. The file is opened on the caller's
  thread, outside the output lock, so an open failure is reported at once. Writers of the same file take
  turns and seek to the end before each batch. More than 10,000 live lines waiting (replayed lines are not
  counted) means the writer is stalled: it is abandoned with `log disabled: log writer stalled`. Each log
  file keeps its own flush mark, advanced only after a successful flush, so re-enabling a file writes the
  retained lines it never received, each once: after logging to A, then B, then A again, A gets the lines
  it missed while B was current (a failed batch may appear twice). `Session.Dispose` waits up to 2 s in
  total for log writes.

## UI

- **Command list** (`CommandListView` over `CommandListSource`): items are display lines
  (`DisplayLines`: one per file or group header, two per command); the selection only rests on main lines.
  The rows are a `FileRow` per config file (in chain order, before the file's groups and commands, see `Session.BuildRows`),
  then the groups and commands of each file. Files and groups share one set of collapsed keys (`TreeRows.KeyOf`: the
  `ConfigSource` or the `GroupEntry`), so a collapsed file hides everything below it, and the same focus rules apply.
  A file row has no action target (`Selection.Targets` gives null), so its command keys do nothing.
  `RowLayout` builds the text: a group or command whose group is at list depth d (a top-level group is at 1, one below its
  file row) has the mark (`→`) at column 0, the status glyph at 2d+2 (under a sibling group's `▾`/`▸`) and the name at 2d+4.
  `CommandListSource.Render`
  restores the attribute it found, because ListView fills the rows below the last item with the
  attribute it last set itself. Status glyph colors come from `StatusColors`: a bright and a dark
  variant per status, whichever has the higher WCAG contrast against the row's background (bright when
  the background is the terminal default, `Color.None`).
- **Info pane** (`DetailsView`): text from `DetailsText`, word-wrapped by `TextWidth`, with a row map
  back to logical lines (`DetailsText.WrapRows`) so a mouse selection is kept in logical coordinates
  (survives redraws) and copies wrapped lines as one line, restoring the spaces dropped at breaks. The frame
  title (read every 100 ms) uses the layout of the last draw instead of building one.
- **Output pane** (`OutputView`): cell wrap like a terminal (`OutputWrap`, one cell per grapheme, so
  display columns equal line columns; `RowCount` counts rows without building cells). The scroll position is (line, row within line) (`OutputScroll`); only the
  lines an operation reaches are wrapped. The cursor is a logical line; following shows the last page.
  Following resumes only with the viewport at the bottom and the cursor on the last line. The wheel scrolls
  the viewport; once it is at the bottom, wheel-down moves the cursor down (a cursor above the view first
  jumps to the top line in view), so the wheel alone reaches the tail.
- **Idle cost.** In Terminal.Gui 2.5 redrawing one view redraws the whole window, which allocates about 7 MB and
  costs 10–50 ms of CPU. So cmdr redraws only for a change: the 100 ms tick redraws the output pane when its buffer
  changed, and the 1-second tick redraws the list only while a visible command shows a growing duration (running,
  starting or stopping); everything else arrives as an event. The output pane keeps the prepared lines of the last two
  frames (`LineCache`), keyed by line object. Measured on Linux (WSL2, 2026-11-16): idle cmdr uses what a bare
  Terminal.Gui window does, about 5% of a core, nearly all from Terminal.Gui's polling loops (`Task.Delay(...).Wait()`
  every 20–40 ms through the thread pool, which spin-waits). The fork in the `Terminal.Gui` submodule (branch `cmdr-perf`)
  makes those two waits block instead; idle is then about 1%.
- **Streaming cost.** While a visible command writes 100 lines/s (160×50 terminal) cmdr draws ~9 frames/s and uses about
  10% of a core (was ~15%; main thread ~4 ms per frame, was ~10). The gains: `OutputView` skips Terminal.Gui's viewport
  clear (it paints every cell itself); the fork's `OutputBufferImpl.AddStr`/`FillRect` lock and snapshot the clip once per
  call instead of per cell, and `Cell.Grapheme`/`GetColumns` cache their per-char normalization and width answers; and
  thread-pool spin is off (`UnfairSemaphoreSpinLimit=0` in `Cmdr.csproj`), since every chunk a command writes wakes a pool
  worker for the async pipe read.
- **Terminal output while streaming.** The terminal's CPU counts too, so cmdr sends as little as it can:
  - The fork's `OutputBase` keeps a copy of what the terminal shows and skips dirty cells that equal it (short unchanged gaps
    are written rather than jumped over: a cursor move costs more). This makes `\r` progress lines, output arriving while
    scrolled up, borders and padding nearly free.
  - The output pane spans the full width (list and info on top, output below) so that its rows are whole terminal rows.
    `OutputView` then hints `IDriver.ScrollRowsUp` when its rows moved up since the last draw; the fork scrolls the band on the
    terminal (DECSTBM + SU), shifts its copy, and sends only the cells that differ, when its estimate says that is cheaper than
    plain comparison (near-identical consecutive lines are cheaper without). The hint never affects correctness: every cell of
    the band is compared. It is ignored on the legacy Windows console and in inline mode.
  - Measured (Linux, 160×50, varied text, ~9 frames/s): 100 lines/s ~8.4 KB/frame before, ~5.1 KB with the copy, ~2.1 KB
    with scrolling too; 10 lines/s ~4.7 KB with the copy alone, ~0.26 KB with scrolling. Windows Terminal is expected to behave the same (VT scroll regions) but is not yet verified.
- **Search**: `SearchPattern` compiles the bar's query (literal text is escaped; whole word wraps the pattern in
  `(?<!\w)(?:…)(?!\w)` rather than `\b`, so punctuation at the ends works). Literal text has no match timeout (it cannot
  backtrack badly); a regex uses `RegexOptions.NonBacktracking` (linear) when it can, otherwise the backtracking engine with a 1 s
  timeout, after which the pattern is abandoned (no matches, `TimedOut`, the bar says "pattern too slow") so a pathological regex
  costs one timeout, not one per line. A short timeout fired under CPU load on plain literals and silently gave partial counts. Each pane implements `ISearchTarget`
  (`SetSearch`, `Find(backward, inclusive)`, `SearchStatus`); the list's is `ListSearchTarget`, which searches every row
  (`Session.BuildRows`, collapsed groups included) and reveals a match through the controller, which remembers the files and groups it
  expanded and collapses them again on close unless they hold the focused row. Matches are UTF-16 ranges in the plain text; the
  output pane maps them to cells through `OutputWrap.Cell.Offset/Length`, the info pane through its logical lines (a match may
  cross a wrap). An active match is a position (line, start, length); it is highlighted, and counted, only while an identical match
  still exists, so changing text simply drops it. The output's match count (`OutputSearchIndex`) counts each completed line once
  and drops evicted lines, recounting only partial lines, so the 100 ms status refresh does not rescan the buffer while it streams;
  `Find` skips completed lines with no matches. Highlights are computed per drawn line and only while a search is set. The bar
  (`SearchBar`) keeps the focus in its `TextField`; a subclass takes Enter, Tab, Esc, F3, ↑/↓ and Alt+C/W/R before the field
  does, and the bar swallows everything the field declines except Ctrl+Q/Ctrl+S. The options are drawn by the bar, not
  `CheckBox`es: a CheckBox under the mouse takes the scheme's Highlight role, which is italic and dim. While the field has the
  focus the status line shows `SearchHint` instead of the pane hints. `SearchHistory` (pure) holds the session's terms and the
  ↑/↓ navigation with its draft; a text change not made by the navigation resets it. Seeding from a selection
  (`SelectionSeed` on the info and output panes, one line, ≤ 200 UTF-16 units) sets the pane's active match to the selection
  before an inclusive Find, so the selected occurrence comes first; when it is not a match the Find just continues from there. The target follows the focused pane (checked each
  tick); retargeting sets the pattern without moving.
- **Dialogs**: `Confirm.Ask`/`Choose` make the action the user initiated the default — Terminal.Gui 2.5
  makes the last button of a MessageBox the default, so choices are passed reversed. Dialog buttons take
  the bottom ~4 content rows: a view at content row r needs `Height ≥ r + 7`.
- The command line shown in the info pane and copied with `c` is POSIX sh syntax, or PowerShell syntax on
  Windows (`CommandLineFormatter`; a program that is quoted, or that PowerShell would read as a keyword, a number
  or an operator, gets the `&` call operator). PowerShell passes batch-file arguments in its legacy mode, so a command
  whose program resolves to a `.cmd`/`.bat` and has an argument that mode would mangle (`WindowsBatchCommandLine`:
  empty, a quote, or `& | < > ^ % ! ( )` and backtick) is copied as cmdr's own cmd.exe command held in `$env:CMDR_CMD`
  and run as `cmd /d /v:off /c '%CMDR_CMD%'` inside a script block that saves `$env:CMDR_CMD` and restores it in a `finally`
  (an absent variable stays absent): cmd expands the variable before parsing, so `WindowsCmdEscaping` applies as is.
- Copy uses OSC52 (`ESC ]52;c;<base64> BEL`) written via `app.Driver.WriteRaw(...)`.

## Implementation notes (from investigation)

- Terminal.Gui **2.x**, built from the fork in the `Terminal.Gui` submodule (`develop` plus the `cmdr-perf` fixes). cmdr builds it with
  `TerminalGuiBuildProperties` (root `Directory.Build.props`): GitVersion off with an explicit version, no package. To run
  the fork's own tests, pass the same: `dotnet test --project Tests/UnitTestsParallelizable -c Release
  -p:DisableGitVersionTask=true -p:Version=2.6.0 -p:GeneratePackageOnBuild=false`. Without the version the assembly is
  1.0.0.0 and UICatalog's `Terminal.Gui.Editor` package (built against 2.4.8) fails to load; without the last flag a
  Release build packs and pushes into `~/.nuget/packages`. Instance API: `Application.Create()`, `app.Init()`, `app.Run(win)`.
  Default quit key is Esc; adding Ctrl+Q does not remove it — the Esc binding must be removed
  (or Esc intercepted in `OnKeyDown`) since Esc must not quit here. The main window swallows unhandled Esc.
- NuGet sources: the root `nuget.config` is self-contained (`<clear />`, one feed keyed `nuget`, every package mapped to
  it), so restore ignores user and machine configs. The key must stay `nuget`, the name the submodule's `nuget.config`
  gives the same nuget.org feed. Package source mapping matches feeds by key, not by URL. Without a root config, a
  restore of `Cmdr.slnx` (or `./publish.ps1`) picked up the user config's feed named `nuget.org`, the submodule's
  mapping found no feed named `nuget`, and restore failed with `NU1100: Unable to resolve 'Microsoft.SourceLink.GitHub
  ...' PackageSourceMapping is enabled, the following source(s) were not considered`. Restoring `Terminal.Gui.csproj`
  on its own worked, because then only the submodule's config applied. cmdr does not need the submodule's
  `LocalPackages` feed, since Terminal.Gui is a `ProjectReference`. The fork is left unchanged to keep upstream merges
  clean; if upstream renames its feed, rename the root key to match.
- Ctrl+C arrives as a normal key (ISIG disabled), so it can be bound.
- Never write to `Console.Out` while the TUI runs.
- Custom view: `OnDrawingContent`, `Move/AddStr/SetAttribute`; `Attribute`/`Color` support 24-bit RGB
  and `TextStyle` flags.
- UI-thread marshalling: `app.Invoke(Action)`; periodic refresh: `app.AddTimeout`.
- `setsid` execs in place (pid == pgid == sid). Non-interactive `sh` makes background jobs
  ignore SIGINT — expected; that is what force kill is for.
- **Leader exit vs. pipe EOF:** with async output reading, `Process.WaitForExit()` (no timeout)
  blocks until stdout/stderr EOF, which never comes while an orphaned child holds the pipe.
  Use the `Exited` event / `HasExited` for leader exit; track output EOF separately.
- `dotnet watch` reads Ctrl+C only through `Console.CancelKeyPress` (it never reads `0x03` from stdin
  in production) and prints `Shutdown requested` on the first Ctrl+C.
