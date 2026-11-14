# Review tracker

Remaining work from the 2026-11-13 review. **When an item is done, delete it here and add a row to [COMPLETED-FIXES.md](COMPLETED-FIXES.md)** naming the commit. Finding IDs refer to the area reports (`01-…` to `06-…`), which hold the triggers, evidence and suggested regression tests.

Work proceeds in the order of the sections below.

## 1. Windows (package E) — needs testing on Windows by the user

| ID | Pri | Decision / item |
|---|---|---|
| 03-01 | P2 | The child starts before job assignment, so early descendants escape the job. Decided: own `CreateProcessW` with STARTUPINFOEX, `PROC_THREAD_ATTRIBUTE_JOB_LIST` (job from creation, Windows 10 1607+) and `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`. Windows tests must record enough to diagnose failures. Step done: `IProcessPlatform.Launch` / `ILaunchedProcess` abstraction and the pure `WindowsCommandLine` / `WindowsEnvironmentBlock` helpers. |

## Validation gaps (not tied to one item)

- Native Windows: the fixed 03-02/03-04/03-03 (a missing program fails to start; a non-PATHEXT extension like `tool.bin` still runs) and 06-05 (PowerShell copy; Windows PowerShell 5.1 drops empty arguments), plus everything in section 1 (Windows). Some Windows tests return early on Linux and are not evidence.
- Package C on native Windows: saving over an existing file with `File.Replace` (keeps its ACL), and log path comparison being case-insensitive.
- Package B on native Windows: a real job-query failure (02-01's only real trigger), pump shutdown on disposal of a detached run, and log files now opened with write sharing so writers of one file can take turns.
- No CI, `global.json` SDK pin or NuGet lock file.
- No physical-terminal or clipboard testing; no sustained memory/handle measurement. Package F was checked through a pty with pyte only: accents and tab stops render correctly, and Terminal.Gui sends ZWJ emoji as one 2-column grapheme. A terminal that doesn't join ZWJ sequences draws them wider and shifts the rest of that row.
