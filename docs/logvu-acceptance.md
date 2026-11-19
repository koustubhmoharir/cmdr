# logvu acceptance run (2026-11-19, Linux)

The acceptance list from [logvu-design.md](logvu-design.md#implementation-and-acceptance), with the evidence for each item.
"pty" means checked by driving the real executable in a pseudo-terminal (pyte screen dumps) against generated log4net sets:
a 220 KB demo set with three numbered files crossing midnight plus an unrelated `SheetKraftWeb.log` and a `.gz` backup, a live
generator rotating every 40 events, and a 253 MB set of six files (2.06 million events). Windows-only behaviour is listed in
[logvu-windows-testing.md](logvu-windows-testing.md) and is not covered here.

| Acceptance item | Evidence | Result |
|---|---|---|
| Open a config directly, reopen it from the picker, open the first config from an empty history | pty: `logvu <config>`; `logvu` with an empty store → empty state → `o` → file dialog → main window; relaunch lists it and Enter opens it. Tests: `RecentPickerTests`, `ProgramTests` | Pass |
| A similarly prefixed unrelated log and a compressed roll are excluded | pty: `SheetKraftWeb.log`, `SheetKraft6.log.gz` never shown. Tests: `FileSetTests` | Pass |
| Representative fixtures for all schemes, both directions | Tests: `EventFramerTests` (all six fixtures × LF/CRLF × BOM × block sizes × live/closed, backward == reverse(forward)), property tests | Pass |
| A multiline exception larger than a read block, an empty active file, a rotation boundary with equal timestamps; no skipped or repeated rows | Tests: `EventFramerTests` (300 KB event), `HistoryReaderTests` (5 MiB event, pending member, equal-time rotation, pages of 1/3/7), `ReconcilerTests` (empty active file), `FilterPipelineEquivalenceTests`. pty: paging back across both file boundaries to the first event | Pass |
| Three-row viewport: Up to the middle, Shift+Up keeps the event selected on the bottom row; again clamps; mirror; arrows at edges; single row; history boundaries | pty (100×5 terminal): exactly as specified, including the mirror. Tests: `TimelineModelTests` cases 1–5 | Pass |
| Both search keys, both next keys, both previous keys, the filter key | pty: `/` search found an event two files back, `n` → "No more matches above", `f` dialog. Tests: `SearchControllerTests` (/, Ctrl+F, n, F3, N, Shift+F3), `FilterControllerTests` (f, F) | Pass |
| A hit inside a folded frame in an older file | Tests: `SearchRenderingTests`/`DetailSearchTests` (hit inside a collapsed fold is revealed); `LogStoreTests` FindAsync across files. Not combined in one pty run | Pass (by tests) |
| Filter by a nested value; incoming matches respect the filter | pty: `f` on a detail field → `thread = 10` across all files. Tests: `FilterControllerTests` (f on `Properties.OrderId`, an array element), `DetailValueTests`, `FilteredArrivalTests` | Pass |
| Pause on an entry in SheetKraft1.log, rotate, open detail: now from SheetKraft2.log; resume Follow: final events of the former active file and first of the replacement exactly once | Tests: `LogStoreTests` (detail of a renamed member → new path, original text; deleted member → Source file expired), `ReconcilerTests` numbered-rotation scenario. pty: live generator, rows contiguous across rotations, no false errors | Pass (detail-after-rotation by tests; also in the Windows checklist) |
| Repeat with coalesced notifications, partial writes and expiry of the selected generation | Tests: `ReconcilerTests` (coalesced double rotation, unterminated lines, expiry and gaps, inode reuse, in-place rewrite), `LogStoreTests` concurrent reads during rotations | Pass |
| A set much larger than the ring: initial reads limited to discovery and page fill, bounded memory, cancellable scan, paused selection survives ring eviction | pty (253 MB, Release): rows within 1.5 s; RSS 67–83 MB while paging and searching; search scans ≈ 90 MB/s with progress and Esc to cancel. Tests: `TimelineModelTests` (window independent of the ring), `ScanThroughputTests` (opt-in) | Pass |

Not verified here: Windows (see the checklist), physical terminals other than the pty, and mouse input in real terminals.
