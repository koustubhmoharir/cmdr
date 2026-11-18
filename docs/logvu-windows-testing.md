# logvu on Windows — test checklist

Everything below was built and tested on Linux only. These are the behaviours that depend on Windows and can only be confirmed
there. Run them with a Release build (`./publish.ps1`, then `publish\win-x64\logvu.exe`) in Windows Terminal, and note the
result of each step. A failure is most useful with the exact status-line text and, for rotation problems, the file listing
(`dir C:\temp\Logs`) at that moment.

## Setup

1. Create `C:\temp\logvu.json`:
   ```json
   { "logsDirectory": "Logs", "filePrefix": "SheetKraft", "fileNamePattern": "^SheetKraft(?:[0-9]+)?[.]log$",
     "activeFile": "SheetKraft.log", "format": "log4net-core" }
   ```
2. In a second terminal, start the writer (keeps the active file open the way log4net does, rotates every 40 events):
   `pwsh tools\logvu\log4net-writer.ps1 -Directory C:\temp\Logs`

## 1. Tailing alongside a writer that holds the file open

- [ ] `logvu.exe C:\temp\logvu.json` shows rows, and new events appear while Follow is on.
- [ ] The writer never reports an error, in particular around `rotated after step …` (logvu's brief opens must not block
      log4net's delete/rename of backups).
- [ ] Across at least five rotations, the step numbers in the timeline are contiguous: pause (Space), page up through the
      rotation points (PgUp) and check that no step is missing or repeated.
- [ ] The status line never keeps showing an error such as `not found` after a rotation.

## 2. Identity and rotation details

- [ ] Pause on an event in `SheetKraft1.log`, wait for one rotation, then press Enter: the detail shows the same event, now
      with path `SheetKraft2.log`.
- [ ] Pause on an event in `SheetKraft5.log`, wait for the next rotation (which deletes it), press Enter: **Source file
      expired**. Esc, then PgUp still pages through the surviving files.
- [ ] Stop the writer, delete `SheetKraft.log` and create a new one with different content of a larger size (e.g. copy
      `SheetKraft3.log` over it): logvu shows the new content as new events and does not mix it with the old one.

## 3. Names, paths and the recent list

- [ ] Rename `SheetKraft1.log` to `sheetkraft1.LOG` (writer stopped): it is still a member (names compare case-insensitively).
- [ ] A file `SheetKraft6.log.gz` and `SheetKraftWeb.log` in the directory are not shown.
- [ ] `logvu.exe` with no arguments lists `C:\temp\logvu.json`; `o` opens the file dialog, which navigates Windows paths
      (drive letters, UNC if available) and opens a config.
- [ ] After opening, `%LOCALAPPDATA%\logvu\recent.json` exists and lists the config. If `recent.json` is made read-only, opening
      a config still works (saving the list fails silently). (This exercises the shared atomic-save code — copied from cmdr —
      including its `File.Replace` path.)

## 4. Detail and display

- [ ] Enter on an ERROR row: exception types/messages, method names and `file:line` are coloured, `C:\src\Engine\` is dimmed,
      and `… 2 framework frames` folds with Enter on the marker.
- [ ] Times with `"timeZone": "local"` match the times written in the file (Windows local time zone).
- [ ] Resizing the terminal keeps the selection and redraws cleanly.

## 5. JSON logs (optional, if a Serilog or Go service is at hand)

- [ ] Point a config at a Serilog `JsonFormatter` file or a Go `slog` file (`"format": "auto"`): rows show the rendered message
      and logger; Enter shows the JSON tree with the exception or error stack expanded.
