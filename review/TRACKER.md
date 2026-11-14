# Review tracker

Remaining work from the 2026-11-13 review. **When an item is done, delete it here and add a row to [COMPLETED-FIXES.md](COMPLETED-FIXES.md)** naming the commit. Finding IDs refer to the area reports (`01-…` to `06-…`), which hold the triggers, evidence and suggested regression tests.

Work proceeds in the order of the sections below.

## 1. Configuration persistence (package C)

| ID | Pri | Decision / item |
|---|---|---|
| 01-01 | P2 | Save preserves the existing file's Unix mode, applied to the temp file before any content is written. New files get the OS default. On Windows, replace an existing file with `File.Replace` so its ACL is kept (Windows unverified). |
| 01-04 | P2 | Save follows a symlink: resolve the final target and write and replace beside it. A dangling link is a save error. |
| 01-02 | P2 | Two commands resolving to the same log path, whether default or explicit, is a validation error. Existing default paths don't change. |
| 01-10 | P3 | One nesting limit of 16 group levels, enforced on load, validation and editing (adding/moving groups). |

## 2. Editing (package D)

| ID | Pri | Decision / item |
|---|---|---|
| 06-01 / 06-02 | P2 | If an argument or env value contains a line break, make that field read-only in the command dialog, with a note to edit it in the JSON. Accepting the dialog keeps the original values. |

## 3. Output and terminal text (package F)

| ID | Pri | Item |
|---|---|---|
| 04-04 / 04-05 | P2 | Zero-width text is discarded, and tabs are expanded by UTF-16 length. Use one display-width model in Core and the renderer. |
| 04-07 | P2 | Follow-to-tail ignores the wrapped rows of the last line. |
| 04-08 | P2 | Partial lines have unstable identity, so a selection can jump to other text (or stdout → stderr). |
| 04-10 | P2 | A growing partial line is copied quadratically. |
| 04-11 | P3 | Row counting allocates every drawable cell (measured recommendation). |

## 4. Windows (package E) — needs testing on Windows by the user

| ID | Pri | Decision / item |
|---|---|---|
| 03-01 | P2 | The child starts before job assignment, so early descendants escape the job. Create suspended (or associate at creation), assign, then resume. |
| 03-03 | P2 | A failed resolution falls through to the native search and may run a different executable. Fail instead. |
| 06-05 | P2 | Copy command line produces PowerShell syntax on Windows. The toast says the copied command is for PowerShell. |

## Validation gaps (not tied to one item)

- Native Windows: the fixed 03-02/03-04, plus everything in section 4 (Windows). Some Windows tests return early on Linux and are not evidence.
- Package B on native Windows: a real job-query failure (02-01's only real trigger), pump shutdown on disposal of a detached run, and log files now opened with write sharing so writers of one file can take turns.
- No CI, `global.json` SDK pin or NuGet lock file.
- No physical-terminal or clipboard testing; no sustained memory/handle measurement.
