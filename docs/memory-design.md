# Memory footprint of Cmdr, Logvu and Terminal.Gui

Design proposal, 19 November 2026. The Cmdr design point is **up to 20 configured commands**. Measurements use the applications at `4020b8c` and the Terminal.Gui fork at `6195f7e1`.

## Decision

**Cmdr:** prioritize retained output in busy sessions. Twenty full histories of short plain lines added 45.8 MiB of live managed memory in the output pipeline alone. One real command emitting unusually wide lines took Cmdr to 249 MiB RSS. Allocating empty rings on demand is an easy secondary improvement worth at most 1.53 MiB at 20 commands with default settings.

**Logvu:** keep reading history from disk. Opening 100,000 existing JSON events took the same live heap as opening 2,000. Appending to a running viewer filled a different structure, the recent-row cache. Reducing its capacity from 20,000 to 2,000 saved 6.5 MiB live at 40,000 appended events. Optimize publication and row construction for users who need the larger cache.

**Both applications:** investigate deployment for the shared idle cost. An empty Terminal.Gui window occupied 64.5 MiB RSS with 1.79 MiB live on the managed heap. A smaller cell type or fewer settings objects can save part of that footprint. Neither result would explain tens of MiB of savings at ordinary terminal sizes. Compare actual application publishes before committing to a fork-wide renderer or configuration rewrite.

## Measurements

Ubuntu 24.04 x64, .NET SDK 10.0.107, runtime 10.0.7, Release, framework-dependent. Sizes below are MiB (1,048,576 bytes). Build each app with `dotnet build src/Cmdr -c Release` and `dotnet build src/Logvu -c Release`. Launch the executable under its `bin/Release/net10.0` directory in an xterm-256color PTY. Counts cover the application process alone. Child commands and a terminal emulator have their own memory costs.

### Shared idle footprint

Fresh process per case, no custom theme, 120×40. A small .NET 10 harness ran these stages in separate non-inlined methods against the same fork DLL as the applications. The runtime control slept. The create control held an `Application.Create()` instance. The window called `Create().Init()` and `Run(new Window())`. A startup-hook thread sampled total allocation and then called `GC.GetTotalMemory(true)` at 4.5 seconds. RSS and private dirty pages came from `/proc/<pid>/smaps_rollup` at 4 seconds, before collection.

| Case | RSS | Private dirty | Live managed | Allocated since launch |
|---|---:|---:|---:|---:|
| Runtime control | 25.7 | 4.3 | 0.05 | 0.05 |
| Application created | 49.5 | 12.7 | 1.17 | 2.36 |
| Empty window | 64.5 | 23.4 | 1.79 | 4.05 |
| Cmdr, one unstarted command | 69.3 | 26.9 | 2.21 | 4.88 |
| Logvu, empty active JSON file | 72.9 | 30.7 | 2.29 | 6.94 |

RSS includes shared file-backed code, runtime pages and GC space. Private dirty is one subset of RSS. Live managed bytes estimate reachable data after collection. Allocated bytes include objects already collected. These columns cannot be added. A single timing sample includes startup and tiered compilation and gives an order of magnitude. The minimal window is a control, so subtracting it from an app gives an incremental footprint under this setup, not precise component attribution.

Two isolated fork experiments narrow the choices:

* **Screen area.** At 80×24, 120×40 and 240×80 the bare window retained 1.60, 1.79 and 2.76 MiB managed. On x64 `Unsafe.SizeOf<Cell>()` returned 32 bytes. The driver contents and the front buffer together account for 0.12, 0.29 and 1.17 MiB of inline cells respectively. The front buffer's reference to the contents aliases the existing array. Preserve the front buffer's frame differencing and scrolling. At 120×40, eliminating even the entire front array would save only 150 KiB of cell slots. [OutputBufferImpl](../Terminal.Gui/Terminal.Gui/Drivers/Output/OutputBufferImpl.cs) · [OutputBase](../Terminal.Gui/Terminal.Gui/Drivers/Output/OutputBase.cs)
* **Configuration.** In a private DLL copy, replacing the body of `ModuleInitializers.InitializeConfiguration` with a return reduced the create-only case from 49.5 to 35.2 MiB RSS. Once a window was running, RSS went from 64.5 to 62.0 MiB, private dirty from 23.4 to 22.2 MiB and live heap from 1.79 to 1.30 MiB. This diagnostic changes defaults and configuration semantics. The create-only delta substantially overstates the benefit observable in the window. The real initializer builds a configuration tree, merges source JSON for schemes and key bindings, then binds root settings and active theme overlays. [Initializer](../Terminal.Gui/Terminal.Gui/ModuleInitializers.cs) · [Configuration builder](../Terminal.Gui/Terminal.Gui/Configuration/Settings/TuiConfigurationBuilder.cs)

### Cmdr output ownership

The session constructs a ManagedProcess and CommandOutput per configured command. Each OutputBuffer immediately allocates 10,000 reference slots by default. A control changed only `bufferLines` from 10,000 to 1 for 500 unstarted commands. The measured live heap fell from 41.90 to 3.74 MiB, matching the 38.14 MiB predicted from eight-byte slots. At 20 commands the analogous saving is about 1.53 MiB. The larger fixture isolates the slope and is outside the product workload. [Session](../src/Cmdr.Core/Session/Session.cs) · [OutputBuffer](../src/Cmdr.Core/Output/OutputBuffer.cs)

A separate Release process held 20 real CommandOutput instances at capacity 10,000. Per active command, it called WriteStdOut 100 times with a chunk of 100 newline-terminated 80-character ASCII lines. The colored case changed from default to red halfway through each line and reset at the end. The probe sampled `GC.GetTotalMemory(true)` and kept all commands alive across the reads. The fixture reuses its input chunk. The parser creates a separate output string for each completed line. Values include the probe and all 20 output pipelines, with no UI or children.

| Commands with full histories | Retained lines | Live MiB, plain | Live MiB, two style runs |
|---:|---:|---:|---:|
| 0 | 0 | 1.67 | 1.70 |
| 1 | 10,000 | 3.95 | 4.67 |
| 5 | 50,000 | 13.11 | 16.59 |
| 20 | 200,000 | 47.45 | 61.22 |

The plain slope is roughly 240 bytes per 80-character line beyond the preallocated slots. Completed output already stores one string and shares the default style array. The colored fixture adds about 72 bytes per line for run storage. Partial lines follow a separate copy-on-write path. [OutputLine](../src/Cmdr.Core/Output/OutputLine.cs) · [AnsiLineParser](../src/Cmdr.Core/Output/AnsiLineParser.cs)

In a separate 160×50 PTY trial, one child wrote 100,000 newline-terminated lines, each an eight-digit number, a space and 80 ASCII characters. Four seconds after the final marker rendered, Cmdr RSS was 89.1 MiB at the default 10,000-line limit and 107.4 MiB with a 100,000-line limit. With 10,000 lines of 8,201 characters at the default limit, RSS was 249.2 MiB. Logging was off. Line width matters even if only one command runs.

### Logvu storage and allocation

The same instrumented 120×40 startup test opened a single active slog-json file with repeated short INFO records and default settings. At 0, 2,000 and 100,000 pre-existing events, live heap was 2.29, 3.07 and 3.07 MiB and RSS was 72.9, 77.9 and 77.7 MiB. LogStore warms at most 2,000 rows. Older history stays on disk. [LogStore](../src/Logvu.Core/Store/LogStore.cs)

To fill the cache, launch on an empty file and append two bursts of 20,000 JSON lines each. Each record had a fixed ISO timestamp, INFO level and a numbered short message. Wait for the last marker in each burst to render, settle for one second, read VmRSS, then collect as above in the same process. The cumulative allocation column includes startup, readers, parsing, reconciliation and rendering.

| Recent capacity | Total appended | RSS | Live managed | Cumulative allocated |
|---:|---:|---:|---:|---:|
| 20,000 | 0 | 75.3 | 2.29 | 6.8 |
| 20,000 | 20,000 | 92.6 | 9.38 | 92.4 |
| 20,000 | 40,000 | 106.0 | 9.47 | 217.0 |
| 2,000 | 0 | 73.3 | 2.15 | 6.3 |
| 2,000 | 20,000 | 85.7 | 2.93 | 53.5 |
| 2,000 | 40,000 | 90.7 | 3.00 | 176.7 |

The second burst at default capacity added 0.09 MiB live while allocating about 125 MiB. A rising RSS across those bursts is compatible with bounded cache retention and GC high-water behavior. The absolute allocation totals depend on how writes are batched.

An independent 120×30 PTY test used log4net text with an exception and stack frame every sixteenth event. Three warm-filesystem launches on 100 records gave idle RSS of 76.0–78.0 MiB and PSS of 43.0–43.9 MiB. Three launches on six rotated files totaling 1.5 million events (198.6 MiB) gave 81.2–81.4 MiB RSS and 44.5–46.1 MiB PSS. A backward literal search miss through the large set took 3.88–7.02 s with process peak RSS 88.7–94.0 MiB. PSS apportions shared pages across processes and varies with other workloads. These tests exercised real rendering, following and search with eight append-to-visible samples per launch.

In isolation, a full RowRing with 20,000 rows allocated about 160,100 bytes each time its last row was replaced and Snapshot called. At 2,000 rows this fell to about 16,100 bytes. This is the average thread allocation across 100 warmed replacements using the same row object. Snapshot copies references once per changed publication. Repeated reads of an unchanged ring reuse the result. At default capacity the full copy is a large object. [RowRing](../src/Logvu.Core/Store/RowRing.cs) · [StoreState](../src/Logvu.Core/Store/StoreState.cs)

### Cmdr at the 20-command design point

In a 160×50 PTY, enter a group containing exactly 20 commands. Each populated command writes 10,000 lines of 89 displayed ASCII characters. The remaining commands run `true`. The colored workload makes 20 characters red between a default-colored numbered prefix and suffix. Drain the PTY throughout and sample Cmdr VmRSS six seconds after the writers finish. The process used 72.6 MiB before running the group. The zero-history case controls for starting children, opening pipes and warming runtime code. Each table entry is a separate launch.

| Full command histories | Cmdr RSS, plain | Cmdr RSS, colored | Extra managed heap, plain | Extra managed heap, colored |
|---:|---:|---:|---:|---:|
| 0 | 87.4 | 86.5 | baseline | baseline |
| 1 | 91.3 | 91.5 | 2.43 | 3.36 |
| 5 | 102.3 | 106.4 | 12.19 | 16.78 |
| 20 | 138.4 | 157.3 | 48.82 | 67.15 |

The last two columns are independent Cmdr.Core measurements on the same text. Warm a separate parser, create 20 CommandOutput instances, measure the full-collection baseline, and feed 4,096-character spans from the generated input to each active buffer. Collect and sample after the first, fifth and twentieth buffer. The three-run colored lines here cost more than the two-run lines in the 80-character fixture above.

## Implementation design

### 1. Give Cmdr a predictable history budget

For installations that can accept shorter history today, use the existing per-config-file `bufferLines` setting. For example, 2,000 lines would retain roughly one fifth of the measured full-history payload for lines of the same distribution. Logging already enabled for a run can keep older output on disk. Switching logging on later can replay only retained output.

For variable-width workloads, add an **optional per-command retained-history byte budget** to CmdrConfig, inherited through ConfigSource alongside bufferLines. Default to the current line-only policy. For a concrete deployment trial, 2 MiB per command gives at most 40 MiB of charged completed-history storage across 20 commands. This is a budget for history, not a process RSS guarantee. The normal 89-character fixture would lose some scrollback at that setting. Document the trade-off and expose the oldest retained line through the existing absolute numbering.

* Compute a conservative retained-size estimate once for each completed line from UTF-16 length, object and string overhead, aligned private run-array storage and a reference slot. Charge the shared default style once per process or exclude it from the per-line charge. Keep a running total under the OutputBuffer lock. Evict oldest completed lines until both limits hold. Advance FirstLineNumber for every dropped line, including one dropped immediately. Define a one-oversize-line exception or immediate eviction explicitly before exposing the setting. Prefer immediate eviction for a strict budget and show a bounded omission notice in the UI, outside the numbered log stream.
* Preserve the existing atomic append-plus-partial update, distinct stdout/stderr partials, identity of a partial through completion, asynchronous writer ordering, readiness notifications and restart numbering. Send the original completed batch to an active logger even if memory eviction removes it immediately. Account separately for renderer/search snapshots, parser scratch and queued log output. A stalled writer already has a line-count bound of its own. If slow-disk traces show retained lines dominated by that queue, add a byte limit with the existing stalled-writer notification. [CommandOutput](../src/Cmdr.Core/Output/CommandOutput.cs)
* Separate physical from configured ring capacity. Start with empty storage, grow geometrically under the existing lock up to bufferLines, and release oversized storage on clear/restart. Copy in logical order and reset the head when growing. Measure restart churn on frequently restarted commands before choosing a small reusable capacity. Keep Capacity reporting the configured maximum. This step preserves historical coverage and can ship independently.
* If the product requires all 10,000 lines in memory, trial **lossless packing of sealed blocks** as a separate design choice. Encode Latin-1-compatible completed text as one byte per code unit; store other lines losslessly as UTF-16, with line offsets, source, identity and exact style runs. Keep partials and the open block on the current path. Resolve immutable snapshots by retaining the underlying blocks and materialize strings only for consumers that need them. Bound any decoded viewport cache. At 200,000 × 89 ASCII characters, text narrowing alone has a theoretical payload saving of 17.0 MiB before metadata and decoded copies. Benchmark full-history search, selection and log replay before adopting this higher-complexity option.

### 2. Keep Logvu’s large cache affordable under live publication

Use `recentCapacity=2000` as the first low-memory profile for users who usually follow the tail. The measured saving at 40,000 arrivals was 6.5 MiB live and 15.3 MiB RSS in the paired runs. Older pages may take disk reads, and rotated-away files can no longer supply evicted rows. Keep the existing default until paging latency and retention needs are established.

For sustained tailing at 20,000 rows, keep the writer-owned key lookup and publish immutable chunks. Prototype 256 references per chunk. A snapshot holds an immutable directory of chunk references plus its own head, count and order. Share untouched chunks. On the worker's first write to a published chunk in each batch, copy that chunk, including replacement of a provisional row. Freeze the new directory only once per changed batch. With 20,000 slots, a single changed chunk needs about 2 KiB of references and the directory about 0.6 KiB. On dense updates, build a fresh set of touched chunks once for the batch. Preserve negative-sequence warm-up, non-power-of-two capacities, wraparound and rotation/reset. Never recycle published storage while a concurrent reader may still hold it. Keep the existing indexed read interface used by StoreReader.

Measure allocation stacks during the JSON burst before changing parsers. The existing JSON path parses a document, materializes top-level fields and clones the JSON root even when the caller retains only EventRow. Prototype a row-only projection for unconditional cache construction. Extract preview, time, level and configured columns while the document is alive, and share the existing rendering logic for Serilog templates, slog cause chains and raw fallback. Retain the full parse for detail and predicates that need it. In RowBuilder.FirstLine return the original string when there is no cut. For a cut, copy only the prefix so the row cannot retain the entire message. Benchmark separately on JSON and multiline text. [RowBuilder](../src/Logvu.Core/Store/RowBuilder.cs) · [JSON schemes](../src/Logvu.Core/Parsing/JsonSchemes.cs)

### 3. Run a deployment experiment for the idle floor

Compare the current framework-dependent single-file publish with an experimental Native AOT publish per supported RID, first with Cmdr and then with Logvu. Use actual app binaries, the same terminal, empty and 20-command workloads, and Linux and Windows. A fork smoke project already exercises Native AOT startup and theme switching. Its warning suppressions and results do not establish app compatibility. Examine app-specific trim warnings, configuration binding, source-generated JSON, regex, Unicode widths, native process launch and shutdown, watcher rotation, themes, custom key bindings and picker dialogs. Record disk size separately from resident and private memory. Promote the deployment variant only with a meaningful paired win at the main screen and a viable per-platform build. [Fork smoke project](../Terminal.Gui/Tests/NativeAotSmoke/NativeAotSmoke.csproj) · [Publish script](../publish.ps1)

If that experiment leaves startup allocation material, prototype generated typed defaults for the embedded configuration in the fork. Apply user files, environment and runtime overrides through the existing merge rules. Keep array replacement for schemes/key bindings, theme switch notifications, reload and malformed-source fallback. Benchmark after rendering the main screen. The initializer-bypass measurement gives little reason to sacrifice these behaviors. No renderer change is proposed for ordinary screen sizes.

## Verification and rollout

Suggested acceptance thresholds are design targets. Only the baselines and diagnostic controls above were measured.

1. Compare builds in alternating order over at least five fresh launches per case. Fix terminal size, runtime and config. Record private dirty, PSS, RSS, peak RSS, time to first frame, total allocation and live heap in separate instrumented runs. On Windows sample private working set and private bytes and label them separately. Compare the normal published builds without hooks as well. PTYs here had no capability replies and omit the terminal emulator. Windows, AOT and long-duration leak behavior are unmeasured.
2. For Cmdr run the 0/1/5/20 populated histories, 89-character plain/ANSI and 8,201-character lines. Exercise simultaneous stderr, CR progress, wide/combining text, restart, selection, wrap, search, readiness and slow logging. Check absolute numbering, snapshot immutability, exact log contents, enabled byte limit and default retention. For packed storage require at least 25% lower full-history live heap on the 20-command plain workload and measure search and streaming CPU with the existing CPU harness. For the configurable budget, report the actual number of retained lines alongside the memory saving.
3. For Logvu run sparse appends and bulk appends with a full 20,000-row ring, plus backward search, detail, filter, cancellation, rotation, truncation and in-flight readers. For one replaced row target under 8 KiB allocated per changed publication, down from ~160 KiB. Verify old snapshots against the pre-change sequence, including held snapshots across multiple wraps. Require matching visible rows, search results and row identities. Measure total burst allocation and latency separately for row-only parsing. The 1.5-million-event text search is a regression workload for disk access.
4. For any deployment change, target at least 10 MiB lower median private working set per app at ordinary size before accepting the distribution cost. For app changes benchmark foreground input, append-to-frame latency and throughput. Investigate repeatable regressions above 10% with paired runs. Land changes independently so a regression or a missed saving can be attributed to one change.
