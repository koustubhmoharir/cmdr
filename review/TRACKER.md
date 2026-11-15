# Review tracker

Remaining work from the 2026-11-13 review. **When an item is done, delete it here and add a row to [COMPLETED-FIXES.md](COMPLETED-FIXES.md)** naming the commit. Finding IDs refer to the area reports (`01-…` to `06-…`), which hold the triggers, evidence and suggested regression tests.

Work proceeds in the order of the sections below.

## 1. Windows (package E) — needs testing on Windows by the user

All items (03-01, 03-03, 06-05) are implemented; what remains is the Windows test run listed under validation gaps.

## 2. Open from the review of the fixes (2026-11-15)

- **PowerShell copy of a batch-file program (06-05): verify on Windows.** A batch-file command with an argument PowerShell's legacy passing would mangle is copied as `$env:CMDR_CMD = '<cmdr's cmd command>'; cmd /d /c '%CMDR_CMD%'; Remove-Item Env:CMDR_CMD`. Check in PowerShell 7 and 5.1 with `npm.cmd`/a `node_modules\.bin` shim and arguments `a&ver`, `x|y`, `%TEMP%`, `"q"`, an empty one, and that plain commands (`pnpm run dev --port=3000`) keep the plain form.
- **Windows verification** of 515e30c, c0799b3 (`File.Replace` fallback, `PhysicalPath` on Windows), d0ccf63 (conhost path, duplicated exit-wait handle, UNC), 9b3ae25, ce2c06d (drive-relative programs and PATH entries), f6dcc28 (ComSpec fallback), d718d50 (containment test cleanup) and 91f3d5f (File.Replace error 1176 recovery; only reachable by fault, so verify by review). Step-by-step: [WINDOWS-TESTING.md](WINDOWS-TESTING.md).

## 3. Open from the third review (2026-11-15)

All 29 findings (5 P2, 24 P3) and the roadmap steps S0–S11 are in [ROUND3-REPORT.md](ROUND3-REPORT.md). All findings are fixed (S0–S10, see COMPLETED-FIXES.md). S11's LaunchSpec part is done; the rest (per-run owner in ManagedProcess, Windows group leases and helper deadline) is deferred, planned in [S11-PLAN.md](S11-PLAN.md). The Windows parts are in the Windows checklist. The user has answered the report's questions Q1–Q11; the decisions are recorded there.

## Validation gaps (not tied to one item)

- Native Windows, 03-01: run `dotnet test tests/Cmdr.Core.Tests --filter "FullyQualifiedName~Windows"`. `WindowsJobContainmentTests` exercises the job list, the handle list, the inheritable-handle lock with the Ctrl+C helper, pipe EOF, exit tracking and console stdin; its failures print a diagnostic dump. Also check by hand that Ctrl+C still stops commands and that `stdin: console` (e.g. `dotnet watch`) still works.
- Native Windows: the fixed 03-02/03-04/03-03 (a missing program fails to start; a non-PATHEXT extension like `tool.bin` still runs) and 06-05 (PowerShell copy; Windows PowerShell 5.1 drops empty arguments), plus everything in section 1 (Windows). Some Windows tests return early on Linux and are not evidence.
- Package C on native Windows: saving over an existing file with `File.Replace` (keeps its ACL), and log path comparison being case-insensitive.
- Package B on native Windows: a real job-query failure (02-01's only real trigger), pump shutdown on disposal of a detached run, and log files now opened with write sharing so writers of one file can take turns.
- No CI, `global.json` SDK pin or NuGet lock file.
- No physical-terminal or clipboard testing; no sustained memory/handle measurement. Package F was checked through a pty with pyte only: accents and tab stops render correctly, and Terminal.Gui sends ZWJ emoji as one 2-column grapheme. A terminal that doesn't join ZWJ sequences draws them wider and shifts the rest of that row.
