# Review tracker

Remaining work from the 2026-11-13 review. **When an item is done, delete it here and add a row to [COMPLETED-FIXES.md](COMPLETED-FIXES.md)** naming the commit. Finding IDs refer to the area reports (`01-…` to `06-…`), which hold the triggers, evidence and suggested regression tests.

Work proceeds in the order of the sections below.

## 1. Windows (package E) — needs testing on Windows by the user

| ID | Pri | Decision / item |
|---|---|---|
| 03-01 | P2 | The child starts before job assignment, so early descendants escape the job. Create suspended (or associate at creation), assign, then resume. |
| 03-03 | P2 | A failed resolution falls through to the native search and may run a different executable. Fail instead. |
| 06-05 | P2 | Copy command line produces PowerShell syntax on Windows. The toast says the copied command is for PowerShell. |

## Validation gaps (not tied to one item)

- Native Windows: the fixed 03-02/03-04, plus everything in section 1 (Windows). Some Windows tests return early on Linux and are not evidence.
- Package C on native Windows: saving over an existing file with `File.Replace` (keeps its ACL), and log path comparison being case-insensitive.
- Package B on native Windows: a real job-query failure (02-01's only real trigger), pump shutdown on disposal of a detached run, and log files now opened with write sharing so writers of one file can take turns.
- No CI, `global.json` SDK pin or NuGet lock file.
- No physical-terminal or clipboard testing; no sustained memory/handle measurement. Package F was checked through a pty with pyte only: accents and tab stops render correctly, and Terminal.Gui sends ZWJ emoji as one 2-column grapheme. A terminal that doesn't join ZWJ sequences draws them wider and shifts the rest of that row.
