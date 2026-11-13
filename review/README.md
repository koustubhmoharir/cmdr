# Code review — 2026-11-13

Reviewed baseline: `a06d405e01095309617069e43c5d4be9200c0ebc` (initial working tree clean).

This directory contains six independent reviews by GPT 6.1 Sol (`gpt-6.1-sol`, high reasoning), followed by a consolidated report and implementation plan. Production code is unchanged. The consolidated result is 40 distinct findings (2 P1, 32 P2, 6 P3) plus one measured performance recommendation; duplicate evidence is reconciled in the report.

## Reading order

1. [Consolidated report and fix plan](REPORT.md)
2. [Configuration and persistence](01-configuration.md)
3. [Process lifecycle and threading](02-process-lifecycle.md)
4. [Linux/Windows process integration](03-platform-processes.md)
5. [Output, escape codes, Unicode, and memory](04-output-terminal.md)
6. [Session tree and execution orchestration](05-session-orchestration.md)
7. [Application UI and usability](06-ui-application.md)
8. [Validation evidence and limitations](VALIDATION.md)

## What the application does

cmdr is a .NET 10 terminal UI for supervising project commands on Linux and Windows. JSON configuration defines nested parallel/sequential groups, environment and working-directory inheritance, readiness/busy patterns, stop behavior, optional logs, and per-run input prompts. Base files contribute read-only groups. Users can start, stop, restart, rearrange, and edit commands; save configuration; inspect colored output; and reopen recent configurations.

`Cmdr.Core` owns configuration, session planning, managed process state, platform process groups, and output parsing/buffering. `Cmdr` uses Terminal.Gui 2.5.0 for the application, dialogs, navigation, details, output rendering, and clipboard integration. Processes use pipes rather than a PTY. Linux supervision uses sessions/process groups; Windows uses Job Objects and a separate Ctrl+C helper. Escape handling supports styled line output rather than full terminal emulation.

## Review boundaries and method

Each area reviewer owns one report and inspects relevant implementation and tests. Shared boundaries are reviewed from both sides when needed: output notifications/process locks, native groups/process lifecycle, session tasks/UI marshaling, and parser/display behavior. The coordinator runs the baseline suite, checks reported evidence, reconciles overlap, and orders fixes by impact and dependencies.

P1 means high-impact correctness/resource failure to address first; P2 means an actionable defect under a specific supported scenario; P3 means lower-impact behavior or maintainability work. These priorities do not imply security exploitability. Recommendations and unverified platform risks are identified separately from defects. Source links and line numbers refer to the baseline above.
