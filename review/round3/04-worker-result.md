# Worker 04 result: output pipeline review

Reviewed HEAD: `a27a7ce65b3e3338a18596d700f82dcc5f982e7c`, branch supplied as `review-fixes`. Environment: Linux, .NET SDK `10.0.107`. Repository status was clean before and after the work.

The worker request is explicitly read-only. No implementation or repository test changes were made, and no new design phase was started. Investigation and confirmation probes are complete. All scratch projects, generated build artifacts, logs, and test results are under `/tmp/cmdr-claude-run-review2/probe-04/`.

Read every file in `src/Cmdr.Core/Output/*.cs` and `tests/Cmdr.Core.Tests/Output/*.cs` completely. Read README.md, docs/design.md, review/REPORT.md, review/04-output-terminal.md, review/COMPLETED-FIXES.md (including its review-of-fixes section), and review/TRACKER.md. Followed the relevant ManagedProcess run, notification, pumping, and exit paths; UnixOwnership creation/ownership helpers and LinuxNative declarations; and OutputView geometry, buffer readback, partial pins, and width setup. No accepted tracker limitation is counted as a new finding.

## 1. Findings (A), most severe first

Four P2 findings were established. No P1 finding was established. These are four independent defects; descriptions below do not count their test gaps or architectural causes as additional defects.

### 04-01 — P2: Reusing a waiting writer can apply another file's flush cursor and lose retained output

**Locations at HEAD:** `src/Cmdr.Core/Output/CommandOutput.cs:293`, `:465`, `:530`.

**Defect:** `ReuseWaitingWriterLocked` changes `_lastLoggedPath` without restoring that path's flush cursor, so `StartWriterLocked` can discard A's replay based on progress made only to B.

**Concrete failure scenario:**

1. Begin a run and enable file A. Give its first writer 2,000 lines of 200 characters, so a FIFO sink blocks in an actual write.
2. Disable A, append `MISSED-A\n`, enable A again, then disable it. A's second writer waits behind the first and captures a deferred replay containing `MISSED-A`.
3. Enable regular file B, append `B-LIVE\n`, and let B flush. Its cursor is now 2,003, covering the start marker, 2,000 original lines, `MISSED-A`, and `B-LIVE`.
4. Enable A again: the waiting A writer is reused. `_lastLoggedPath` becomes A, but `_nextUnloggedLine` retains B's value. Disable A and drain its FIFO.
5. The first A writer finishes. Its smaller commit cannot lower the cursor because commits use `Math.Max`. Starting A's waiting writer skips the entire captured replay, including `MISSED-A`, although that line never reached A. The separately queued `B-LIVE` does reach A.

**Evidence:** The source-linked `Probe.csproj` uses the actual in-scope code, actual UnixOwnership/LinuxNative helpers, a real Linux FIFO for A, and a regular file for B. Reflection only observes the flush cursor to gate progression; it does not change it. Output:

```text
A->B->reuse A: MISSED-A count=0, B-LIVE count=1
```

The probe waits for B's successful flush, closes logging, drains A, and waits for every writer to finish. This is deterministic loss, not the documented possible duplication of a failed batch. A has no write or flush failure in this probe.

**Confidence:** High.

**Suggested fix:** Keep successfully flushed progress with the corresponding destination state, shared by its successive writers, rather than in a global path/cursor pair. Reuse must restore the entire destination state atomically. Merely resetting the cursor on reuse can avoid this omission but can duplicate already flushed replay; test both loss and duplication. Add this exact A → waiting A → B → reuse A sequence, including a run change or eviction while A waits.

### 04-02 — P2: Concurrent enables bypass the per-file writer bound

**Locations at HEAD:** `src/Cmdr.Core/Output/CommandOutput.cs:211`, `:217`, `:254`, `:271`.

**Defect:** The abandoned/waiting-writer checks happen only before opening outside `_sync`, while the post-open commit checks only disposal and the current active path, allowing concurrent calls to create multiple waiting writers for one file.

**Concrete failure scenario:** Three calls to `EnableLog(A)` pass the initial checks while no writer is registered, then stall in directory creation/opening. Release the first call, let its writer block on the sink, and disable logging. Release the second call; it creates a waiting writer, then disable logging again. Release the third call. Since `_log` is null, it creates a second waiting writer instead of reusing the existing one. All three writers remain unfinished. Any number of calls can pass the initial checks in the same way. A similar interleaving can evade the refusal to queue behind a writer abandoned while the call was opening.

**Evidence:** `Concurrency.csproj` compiles the unchanged actual `Output/*.cs` sources against a small UnixOwnership test double. The double gates only `CreateDirectory`, representing a delayed external I/O operation between the two locking regions; ownership handling itself is not under test. A real, unchanged FIFO path and real FileStreams are used for all writers. Three dedicated enable threads reach the I/O gate before any is released. After the sequence above:

```text
Concurrent enables: unfinished same-path writers=3 (documented maximum 2)
```

All writers were subsequently closed, the FIFO drained, and thread joins/completions checked. The excess-writer result does not depend on timing sleeps. The first writer's blocked state is confirmed by a bounded failed wait for log completion.

**Impact:** Repeated concurrent enable/disable operations against a stalled destination retain more file handles and captured replay/queues than the explicit bound permits. This violates CommandOutput's thread-safe public contract even though normal UI log toggles are serial. It does not establish that the current TUI routinely triggers this schedule.

**Confidence:** High; controlled I/O-seam reproduction, not a claim about the frequency of real filesystem stalls.

**Suggested fix:** Revalidate abandoned/waiting/current-writer state under `_sync` after opening, and discard or reuse the extra opened stream as appropriate. Prefer a per-destination reservation/token so pre-open decisions cannot commit against newer state. Keep actual opening outside the output lock. Cover concurrent opens, disable during open, abandonment during open, and disposal during open with explicit gates.

### 04-03 — P2: Replay permanently increases the live log queue limit

**Locations at HEAD:** `src/Cmdr.Core/Output/CommandOutput.LogWriter.cs:78`, `:109`, `:186`.

**Defect:** `PrependLocked` adds replay length to `_limit`, but taking or flushing that replay never removes the allowance, so the writer subsequently accepts more than 10,000 live queued lines.

**Concrete failure scenario:** Begin a run, retain 12,000 process lines plus its marker, enable logging, and let the complete replay flush. The writer's effective queue capacity remains 22,001 rather than returning to 10,000. If that writer later stalls, 10,001 live lines can wait without disabling logging. With a large retained buffer, the excess live allowance is correspondingly large.

**Evidence:** Actual-source `Probe.csproj`, after successfully waiting for the replay to become idle:

```text
queue limit after replay flushed: 22001 (expected 10000)
10001 live lines pending with writer scheduling gated: IsLogging=True
```

Reflection reads `_limit` and obtains the owner's synchronization object. The second check deliberately holds that object while making a public write, preventing the writer from dequeuing until the observed pending state is checked. This is a controlled scheduling probe, not a filesystem throughput benchmark. No production field is modified. The writer is then released, closed, and awaited.

**Impact:** The documented fixed live backlog bound is weakened by historical replay size. This is distinct from the accepted general observation that a line-count budget is not a byte budget. It does not make a single writer's queue unbounded independently of the replay/buffer size.

**Confidence:** High.

**Suggested fix:** Account for replay entries and live queued entries separately, or reduce replay allowance when those replay entries leave the pending queue. Enforce `pending live lines <= LogQueueCapacity` regardless of historical replay. Add assertions before replay, with replay in flight, after replay flush, and after a later stall.

### 04-04 — P2: Forced line breaking splits short emoji graphemes when the next scalar starts at the boundary

**Locations at HEAD:** `src/Cmdr.Core/Output/AnsiLineParser.cs:644`, `:678`, `:686`, `:696`.

**Defect:** Grapheme continuation is decided using only the next UTF-16 code unit, so an incoming high surrogate is classified as a new grapheme before its low surrogate reveals a supplementary continuation such as a ZWJ emoji component or second regional indicator.

**Concrete failure scenarios and evidence:** Actual-source parser probes produce:

```text
maxLineLength=5, input "ab👩‍💻X\n"
actual:   ["ab👩‍", "💻X"]
expected: ["ab", "👩‍💻X"]

maxLineLength=4, input "ab🇮🇳X\n"
actual:   ["ab🇮", "🇳X"]
expected: ["ab", "🇮🇳", "X"]
```

For the second case the flag occupies the entire four-unit second line; the next `X` starts a third line. At the default limit, `new string('a', 65533) + "👩‍💻X\n"` yields a 65,536-unit first line ending in `👩‍` and a second line `💻X`. The first line should contain just the 65,533 ASCII characters, carrying the whole joined emoji to the next line.

The final probe output records the escaped UTF-16 values and the default-limit line's length and tail. All these examples use short graphemes with a movable prefix; neither the documented whole-line exception nor the 64-unit grapheme-tail exception applies. The defect occurs even in a single Feed call, independently of chunk boundaries. Each resulting line is scalar-valid in these examples, but the grapheme is permanently divided, changing display width/rendering and placing a synthetic newline inside the text in logs/copies.

The existing joined-emoji test has a six-unit limit, where the decision happens on the incoming *low* surrogate. It does not exercise the preceding high-surrogate decision at a five-unit limit.

**Confidence:** High.

**Suggested fix:** Resolve an incoming supplementary scalar before deciding whether it continues the trailing grapheme; keep a pending high surrogate across chunks when needed. Preserve style information and published snapshot immutability. Test all forced-boundary offsets through ZWJ sequences and flags, both unsplit and one-unit chunks, including the default limit. Preserve the existing explicitly documented long-grapheme exceptions.

## 2. Organization and pattern proposals (B), ranked

These are concrete improvement sketches requested by the review, not a new implementation/design phase. Costs are rough implementation-plus-focused-test estimates.

### B1. Give each log destination its own progress and bounded writer state

- **Priority:** 1.
- **Bug classes prevented:** Cross-destination cursor confusion and invalid writer combinations: current 04-01 and 04-02; related past 04-09, 04-R1 and 04-R3.
- **Files:** CommandOutput.cs, CommandOutput.LogWriter.cs, CommandOutputLogWriterTests.cs, CommandOutputTests.cs.
- **Sketch:** Replace `_lastLoggedPath`/`_nextUnloggedLine` and repeated list searches with a destination state that owns its committed position, running writer, optional waiting writer, and optional open reservation. Make the active log reference that state. Validate transitions in one locked place; give external open results a reservation identity. Keep states only for destinations still needed by the supported replay policy, with an explicit retirement rule.
- **Cost/risk:** M; medium migration risk because replay, re-enable, failure, and disposal intersect. Add the failing probes as deterministic tests before changing ownership.

### B2. Separate the log scheduler's pure decisions from sink I/O

- **Priority:** 2.
- **Bug classes prevented:** Queue-budget drift (04-03), replay/liveness interactions (04-01/02), and regression of past 02-05/04-09 when changing writer scheduling.
- **Files:** CommandOutput.LogWriter.cs, CommandOutput.cs, CommandOutputLogWriterTests.cs; a small internal queue/state helper and sink interface if extracted.
- **Sketch:** Represent replay and live entries distinctly, with explicit pending-live count, in-flight batch, successful-flush acknowledgement, close, and abandon transitions. Test sequences of those transitions without running a real writer thread. Keep a small dedicated-thread I/O adapter for open/seek/write/flush/close. Inject a gated/failing sink for deterministic integration tests; retain a small number of real FIFO and `/dev/full` checks.
- **Cost/risk:** M; medium. Maintain commit-after-flush and the documented failed-batch duplication policy.

### B3. Make scalar completeness explicit at parser mutation boundaries

- **Priority:** 3.
- **Bug classes prevented:** Current 04-04 and earlier 04-02/04-03; UTF-16-unit, scalar, and grapheme confusion recurs at breaks and edits.
- **Files:** AnsiLineParser.cs, AnsiLineParserTests.cs; OutputLine.cs only if the snapshot contract needs an explicit tail representation.
- **Sketch:** Assemble a complete scalar before forced-break decisions, with explicit pending-high-surrogate handling across Feed calls. Apply one bounded trailing-grapheme rule to that scalar. Keep UTF-16 storage and the established style/CR behavior, but distinguish storage lengths from grapheme boundaries. Add a table-driven boundary matrix across every unit of accents, supplementary scalars, flags, and ZWJ clusters, with explicit expected lines rather than only a same-parser baseline.
- **Cost/risk:** M; medium because partial visibility, SGR changes between code units, Flush, and copy-on-write must stay coherent. No requirement to build a full terminal emulator.

### B4. Publish coherent buffer snapshots and make completion/partial replacement one buffer transaction

- **Priority:** 4.
- **Bug classes prevented:** Number/content/version mismatches during concurrent reads; related past 04-08/F06-01. This is preventive hardening, not another independently reproduced current UI defect.
- **Files:** OutputBuffer.cs, CommandOutput.cs, OutputBufferTests.cs; consumer adaptation in OutputView.cs and its tests belongs to that worker's scope.
- **Sketch:** Add a versioned range snapshot containing first number, completed/partial boundary, lines, and version under one buffer lock. Add one operation to append completed lines and replace the relevant partial atomically. Readers can then reason about a single state rather than combining separately locked properties and retries. Retain immutable OutputLine references and stable identities.
- **Cost/risk:** M; medium cross-layer adoption risk. Avoid copying the entire retained buffer for a small viewport read.

### B5. Make output-test fixtures own and settle all threads/files

- **Priority:** 5.
- **Bug classes prevented:** Host-killing worker exceptions, timeout leaks, silent temp-directory leakage, and timing-dependent coverage; see section 3. Related history: dedicated writer threads and prior flaky log tests in COMPLETED-FIXES.md.
- **Files:** OutputTestHelpers.cs, CommandOutputLogWriterTests.cs, CommandOutputTests.cs; shared TempDir helper only by coordination with its owner.
- **Sketch:** A disposable output/log fixture closes producers, releases any artificial sink stall, observes action exceptions, joins workers and waits for writer completion, then deletes the temporary directory. Most tests should use gated sinks. Where genuine blocking kernel I/O is essential, isolate it in a subprocess with a parent watchdog rather than leaving a timed-out thread in the test host.
- **Cost/risk:** S–M; low production risk, medium test-lifecycle work. Assert cleanup failure instead of silently treating it as success.

### B6. Enforce the published-line and width-policy contracts at their boundaries

- **Priority:** 6.
- **Bug classes prevented:** Mutation of supposedly immutable published spans; divergent width/cache assumptions when future measures are introduced. The backing-array exposure was already a prior review recommendation, not a new application defect here.
- **Files:** OutputLine.cs, DisplayWidth.cs, OutputTestHelpers.cs, DisplayWidthTests.cs, AnsiLineParserTests.cs.
- **Sketch:** Expose spans through a non-castable immutable/read-only representation. Keep the deferred copy-on-write optimization internal. Express the width measure's startup-only/pure-function and ASCII-width assumptions in one policy contract; test cache reset on policy replacement and zero-width clamping without mutating global width policy concurrently with other tests. Add a dedicated isolated test collection/process for tests changing the global measure.
- **Cost/risk:** S; low if it avoids per-read copying. Width-policy behavior changes require agreement first; do not silently expand or restrict pluggability.

## 3. Test-quality issues

1. **Reset tests can pass when the reset is a no-op.** `AnsiLineParserTests.cs:364` starts from default style for ESC[m, ESC[0m, and ESC[00m. Prefix a nondefault style before every reset spelling and assert the post-reset text and complete style. `ZeroResetsEverything` separately covers ordinary `0`, but does not make the other cases meaningful.
2. **Some malformed/unknown-SGR tests assert much less than their names imply.** `AnsiLineParserTests.cs:415` contains `ESC[21;38x;99;108mx`; `x` is already a CSI final byte, so `;99;108mx` is ordinary text rather than additional SGR parameters. `StyleOf` inspects only the first span's style. Likewise the `38;5;abcmx` case at `:385` terminates at `a`. Use syntactically valid SGR for unknown-code tests, and assert expected text as well as all styles in malformed-sequence tests. No production defect is inferred from the established malformed-input policy.
3. **Boundary coverage misses the decision immediately before the existing surrogate case.** The forced-break ZWJ test at `AnsiLineParserTests.cs:181` uses max length 6; the max-5 case in finding 04-04 fails. Existing split-invariance tests are useful for chunk state, but cannot prove grapheme preservation against an unsplit baseline produced by the same wrong code. Add explicit boundary-offset oracles. The focused 13-test run passed despite the confirmed defect.
4. **No output test combines a waiting writer, destination switch, and reuse, or measures the live budget after replay flush.** This leaves findings 04-01 and 04-03 uncovered. Serial same-file toggling tests also do not establish concurrent EnableLog's post-open invariant (04-02). Prefer event/gate-driven tests to short sleeps.
5. **`CompletesWithin` can terminate the test host or leave work behind.** `CommandOutputLogWriterTests.cs:25` invokes an Action on a background Thread without capturing exceptions. An exception escapes that thread, and Join timeout leaves the action running against objects the test may dispose. Capture exception/completion results and release/join through an owning fixture, or use subprocess isolation for a true hang probe.
6. **FIFO timeout and cleanup are not fully bounded or verified.** At `CommandOutputLogWriterTests.cs:321`, the reader open is queued to the pool while EnableLog synchronously opens the writing side. If the reader never opens, the subsequent five-second reader assertion is never reached. `Drain` uses `_reader.Result` at `:335`, and Dispose ignores a false ten-second drain wait at `:354`; it also does not dispose `_captured`. A stalled drain may outlive the fixture. Gate reader startup or isolate this real-I/O test, and check completion/cleanup explicitly.
7. **The general fixture deletes directories before log closure is known.** `CommandOutputTests.cs:15` calls the nonblocking CommandOutput.Dispose and immediately TempDir.Dispose. Tests leaving a log active, such as EnableLogCreatesParentDirectories, can race the writer's file close. TempDir swallows IOException, hiding failed cleanup. Close and wait for writers before directory deletion. This is static lifecycle evidence, not a native Windows validation claim.
8. **The complex partial snapshot test retains only its last snapshot.** `AnsiLineParserTests.cs:901` takes snapshots after every character but overwrites `last`; it does not validate all retained intermediate styled snapshots after later edits. The other immutability tests do check specific old snapshots and are useful. Retain a representative set across style changes, backspaces, and forced carries to exercise the array/run publication invariants. The concurrent test exercises one reader concurrent with one append-only writer; it does not establish concurrent materialization by multiple readers while rewriting.
9. **Platform guards report passes without execution.** The FIFO tests early-return outside Linux. Their passing count on Windows would not validate log lifecycle there. Use visible skips and retain a platform-independent sink seam. No Windows-only test run was performed here.

## 4. Commands and outcomes

Read-only investigation used `cat`, `sed`, `rg`, `ls`, `wc -l`, and numbered source reads (`nl -ba`) over the files listed above; read-only git checks were `git rev-parse HEAD`, `git status --short`, and `git diff --stat`. HEAD matched the request. Initial/final status and diff output were empty. The initial `rg --files -g AGENTS.md ...` search found no applicable AGENTS.md. `dotnet --version` returned `10.0.107`.

Scratch files were created with shell heredocs and edited using Python scripts, exclusively under the permitted probe directory. Each FIFO was created by `mkfifo` invoked from the scratch program. FIFO recreation/deletion was confined to that directory.

### Actual-source confirmation harness

```sh
dotnet run --project /tmp/cmdr-claude-run-review2/probe-04/Probe.csproj -c Release
```

Exit 0. The project directly includes all actual `Output/*.cs`, UnixOwnership.cs, and LinuxNative.cs, with no repository project reference and no repository build output. Initial iterations confirmed Unicode splitting and queue allowance, then added the cross-path replay scenario and scheduling check. The final run was repeated after adding the second scratch project to verify the project exclusions and reproducibility. It confirmed all three defects again. Final captured output:

```text
ZWJ boundary max5: ["ab\uD83D\uDC69\u200D","\uD83D\uDCBBX"]
flag boundary max4: ["ab\uD83C\uDDEE","\uD83C\uDDF3X"]
ZWJ default boundary: ["length=65536 tail=aaaaa\uD83D\uDC69\u200D","\uD83D\uDCBBX"]
CR in OSC: ["a","VISIBLE"]
CR in DCS: ["a","VISIBLE"]
queue limit after replay flushed: 22001 (expected 10000)
10001 live lines pending with writer scheduling gated: IsLogging=True
A->B->reuse A: MISSED-A count=0, B-LIVE count=1
```

The CR/string probes showed expected recovery and are not findings. Captures: `probe-output.txt`, `probe-output-2.txt`, `probe-output-3.txt`, and authoritative `probe-output-final.txt`.

### Concurrent-open confirmation harness

```sh
dotnet run --project /tmp/cmdr-claude-run-review2/probe-04/Concurrency.csproj -c Release
```

Exit 0; three unfinished same-path writers observed, followed by successful drain/completion. Source: Concurrency.cs and OwnershipGate.cs. Output: concurrency-output.txt. Build emitted MSB3539 about the scratch project's BaseIntermediateOutputPath being assigned after use. All relevant project/restore/output locations remain within the probe directory; this warning did not prevent compilation/execution. This harness replaces only the external ownership/directory seam to gate I/O and does not validate actual root ownership behavior.

### Existing tests, narrowly filtered

```sh
dotnet test tests/Cmdr.Core.Tests --no-build --no-restore \
  --filter 'FullyQualifiedName~ForcedLineBreak|FullyQualifiedName~TabStopIsMeasuredInDisplayColumns|FullyQualifiedName~WriterWaitingBehindABlockedWriterReplaysTheLinesRetainedWhenLoggingWasEnabled|FullyQualifiedName~TogglingBehindABlockedWriterKeepsOneWaitingWriterAndLosesNoLine' \
  --results-directory /tmp/cmdr-claude-run-review2/probe-04/TestResults \
  --logger 'trx;LogFileName=focused.trx'
```

Exit 0: **13 passed, 0 failed, 0 skipped**, reported duration 157 ms. Results: `TestResults/focused.trx`. This deliberately used existing Debug test binaries to avoid modifying repository bin/obj. It establishes existing coverage behavior, not a fresh compilation of the entire repository. The confirmation projects independently compile the current reviewed output sources.

No full suite, native Windows tests, physical-terminal test, retained-memory benchmark, or ownership integration test was run. No repository builds, implementation edits, commits, branch changes, or stash operations were performed.

## 5. Open questions and design ambiguities

- **Switching back to a previously used destination after intervening destinations:** should an already completed A → B → A sequence replay the whole current run to A or only A's missing retained lines? The public prose about retained lines not having reached “this file” suggests per-file progress, while new-path handling currently resets a single cursor. I have not counted ordinary A → B → A duplication as a defect without a clear policy. Finding 04-01 is different: it proves omission of a captured, never-written line on the explicitly supported waiting-writer reuse path.
- **Width measure contract:** printable ASCII is hardcoded to width 1 and never passed to the pluggable delegate; cache correctness also assumes a pure, stable measure. Comments describe startup-only replacement and ASCII agreement, but the public delegate does not enforce those conditions. Clarify these before changing that API; no defect is asserted solely because an arbitrary delegate could violate them.
- **Backspace semantics for combining/ZWJ clusters:** the current contract/tests implement scalar deletion (a pair for supplementary scalars), while display and wrapping operate on graphemes. Decide whether removing a whole grapheme is desired before altering behavior. Existing CR whole-line replacement and malformed-string newline recovery are intentional and were not reported as terminal-emulation defects.
- **Root file creation and file identity:** the `File.Exists` then OpenOrCreate check is not an exclusive creation result; a concurrently created destination could be treated as newly created for ownership purposes. This merits a coordinated creation-result seam with UnixOwnership's owner, but no ownership race was injected here, and it is not counted as a confirmed additional finding. Existing ownership items are documented as best effort.

Remaining problems are the four confirmed defects and the listed test gaps. No fix is included because the request prohibits repository writes. Recommended next step: add the four minimal regressions, repair replay progress and post-open state checks first, then fix queue accounting and scalar-aware forced breaks in narrowly scoped changes. Resolve the ordinary cross-destination replay policy question before a broader destination-state refactor. Preserve the existing accepted Unicode exceptions and failed-batch duplicate policy. Coordinate any OutputView or ownership-helper changes with the workers owning those files.

## 6. Files changed in the repository

none
