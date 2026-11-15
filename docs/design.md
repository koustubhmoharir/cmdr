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
`OutputWrap`, `OutputScroll`, `DetailsText`, `DetailsSelection`, `StatusColors`, `GroupOptions`,
`CommandForm`, `InputForm`, …); views only draw and route input.

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

`cmdr <config.json>`, `cmdr --version`. `cmdr --send-ctrl-c <pid>` is an internal helper mode
(Windows only, see Process supervision).

`cmdr` with no arguments shows the recent-configs picker (`RecentPicker`, rows from `RecentRows`, digit
selection in `NumberTyping`). The picker and the main window run one after the other on the same
`IApplication` (`TuiHost.RunPicker`): the picker loads the chosen session itself, so a load error is
shown there and the picker stays; on success it calls `RequestStop` and the main window runs next. The
list's type-to-search (`KeystrokeNavigator`) is off so digits reach the picker.

`RecentFiles` (Core) is the store: `<LocalApplicationData>/cmdr/recent.json`, `{ "files": [...] }`, most
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

- `LaunchSpec.From(ancestors, spec, configDir)` resolves `cwd` through the group chain and collects one
  `EnvLevel` per group with `env` plus the command's. `EnvExpansion.ApplyLevels` applies them outermost
  first; each level's values expand against the environment as it was before that level (so `${PATH}`
  is the parent's value). `${cwd}` is the effective cwd of the item that defines the value.
- Log path: `<logsDir>/<group>/<subgroup>/…/<command>.log` unless `log.path` is set. Two commands with
  the same log path, or whose log file would be another's folder, are a validation error, whether or not
  their logs are enabled (logs can be toggled at runtime). Paths are compared after resolving symbolic links
  (`PhysicalPath`), so two paths reaching one file through a link conflict too. `Session.Validate`/`Save` report only conflicts
  involving an opened-file command, so a conflict inside a base file does not block saving.

### Input placeholders

`InputPlaceholders` scans args for `${input:NAME}` / `${input:NAME?}` (escape `$${input:`). Malformed
placeholders are validation errors and copied literally otherwise. `Session.InputsForActivate/
InputsForRestart` mirror the real start decisions (kill-instead-of-start, refused starts, startable
commands) and merge requests by name (required wins over `?`). `Activate`/`RestartAsync` take the
values, freeze a copy and pass it through `StartCommands` / `StartGroup` / `SequenceRun` to every
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
StopAll, ForceKillAll and Dispose revoke it, and so does the caller's token. Revocation is checked before every
start, and once more after a batch, when whatever was started is stopped again (a graceful stop is sent in the
background, never waited for). Where it registers or starts, the restart also checks under the tree lock that its
group is still in the tree and, for a restart of commands, that every target is still in that group; otherwise it
is cancelled. A group restart starts only the commands the group had when it was called. Tracked per
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
to start; it is never handed to CreateProcess's own search, which would look in cmdr's directory. If it resolves to a
`.cmd`/`.bat`, it is launched as `%ComSpec% /d /s /c "<escaped command line>"` using cross-spawn's
escaping rules. Ctrl+C on a batch file makes cmd.exe prompt "Terminate batch job (Y/N)?" on stdin, so
after a Ctrl+C stop of a batch-wrapped command cmdr also writes `Y\r\n` to stdin. Caveat: a child that
reads stdin could consume the answer first. With `stdin: console` the answer cannot be sent; the launch
returns a notice that is written to the output.

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
  message when the plan overlaps an active run.
- Esc/Ctrl+C on a group cancels every run at or below it and gracefully stops everything running below.
  On a command, it cancels the innermost run containing it (which then fails upward) and stops the
  command if it is running; on a queued command, whatever that run is currently running keeps going.
  F5 on a group stops everything below, waits for leaders, then runs the group.
- Edits on a group (mode change, move, reparent, remove) and moves/removal of items are refused while
  the affected item or anything below it is part of a run. Reparenting into itself or a descendant is
  refused.

## Output

- Ring buffer of lines (`bufferLines`) with absolute line numbers, each line a sequence of styled
  spans, tagged stdout/stderr; partial lines are kept per stream.
- ANSI parser: SGR (16/256/24-bit colors, bold, dim, italic, underline, inverse, reset) is rendered;
  all other escape sequences (CSI, OSC, etc.) are dropped. Bare `\r` (not followed by `\n`) resets the
  current line, so progress bars overwrite in place. A tab pads to the next multiple of 8 display columns.
- Display width (`DisplayWidth`, Core): measured per grapheme cluster (`StringInfo` text element), with a
  pluggable per-grapheme measure. Core's default is a small table (zero-width marks/format characters,
  East Asian wide and emoji ranges); the UI assembly's module initializer (`DisplayWidthSetup`) plugs in
  Terminal.Gui's measure, so tab expansion, wrapping, truncation, selection and drawing all agree with what
  Terminal.Gui draws. A grapheme is never split. Zero-width graphemes (a lone combining mark) take no
  column and are not drawn, but stay in the text and in copies.
- Partial lines: the parser keeps the current line in its own arrays, and a published partial
  (`OutputLine.FromPartial`) shares them without copying; spans and text are built on first read.
  Writes below a published length first move the parser to fresh arrays (copy-on-write), so publishing
  after every chunk is O(1) amortized. Each line has an `Identity`, shared by a partial's snapshots and its
  completed form; the output pane pins a selection end or paused cursor on a partial line by identity plus
  its text as a prefix (`PartialPins`), so a rewritten (`\r`) or evicted partial is dropped rather than matched
  to a later line with the same text.
- Starting a run clears the in-memory buffer (`OutputBuffer.Clear`); every appended line has already been
  queued for the log with its text, so the log keeps all runs.
- Log file: enabled per command or toggled at runtime; when enabled mid-run the current run's buffer is
  written first, then output streams live. Appends; each run starts with `=== started <timestamp> ===`;
  ANSI codes are stripped.
- Log writing happens on a background writer per log session (`CommandOutput.LogWriter`), so nothing
  waits for log I/O: not the pumps, not snapshots, not force kill. The file is opened on the caller's
  thread, outside the output lock, so an open failure is reported at once. Writers of the same file take
  turns and seek to the end before each batch. More than 10,000 lines waiting (beyond the replayed ones)
  means the writer is stalled: it is abandoned with `log disabled: log writer stalled`. The "already
  logged" mark advances only after a successful flush, so re-enabling the same file after a failure
  replays what never reached it (a failed batch may appear twice). `Session.Dispose` waits up to 2 s in
  total for log writes.

## UI

- **Command list** (`CommandListView` over `CommandListSource`): items are display lines
  (`DisplayLines`: one per group header, two per command); the selection only rests on main lines.
  `RowLayout` builds the text: a command whose group is at depth d has the mark (`→`) at column 0, the
  status glyph at 2d+2 (under a sibling group's `▾`/`▸`) and the name at 2d+4. `CommandListSource.Render`
  restores the attribute it found, because ListView fills the rows below the last item with the
  attribute it last set itself. Status glyph colors come from `StatusColors`: a bright and a dark
  variant per status, whichever has the higher WCAG contrast against the row's background (bright when
  the background is the terminal default, `Color.None`).
- **Info pane** (`DetailsView`): text from `DetailsText`, word-wrapped by `TextWidth`, with a row map
  back to logical lines (`DetailsText.WrapRows`) so a mouse selection is kept in logical coordinates
  (survives the once-a-second refresh) and copies wrapped lines as one line, restoring the spaces
  dropped at breaks.
- **Output pane** (`OutputView`): cell wrap like a terminal (`OutputWrap`, one cell per grapheme, so
  display columns equal line columns; `RowCount` counts rows without building cells). The scroll position is (line, row within line) (`OutputScroll`); only the
  lines an operation reaches are wrapped. The cursor is a logical line; following shows the last page.
  Following resumes only with the viewport at the bottom and the cursor on the last line. The wheel scrolls
  the viewport; once it is at the bottom, wheel-down moves the cursor down (a cursor above the view first
  jumps to the top line in view), so the wheel alone reaches the tail.
- **Dialogs**: `Confirm.Ask`/`Choose` make the action the user initiated the default — Terminal.Gui 2.5
  makes the last button of a MessageBox the default, so choices are passed reversed. Dialog buttons take
  the bottom ~4 content rows: a view at content row r needs `Height ≥ r + 7`.
- The command line shown in the info pane and copied with `c` is POSIX sh syntax, or PowerShell syntax on
  Windows (`CommandLineFormatter`; a quoted program gets the `&` call operator).
- Copy uses OSC52 (`ESC ]52;c;<base64> BEL`) written via `app.Driver.WriteRaw(...)`.

## Implementation notes (from investigation)

- Terminal.Gui **2.5.0** (stable). Instance API: `Application.Create()`, `app.Init()`, `app.Run(win)`.
  Default quit key is Esc; adding Ctrl+Q does not remove it — the Esc binding must be removed
  (or Esc intercepted in `OnKeyDown`) since Esc means "stop" here. The main window swallows unhandled Esc.
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
