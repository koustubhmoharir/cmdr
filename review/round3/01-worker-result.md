# Worker 01 result — configuration and persistence

Reviewed `/srv/devshare/projects/cmdr`, branch `review-fixes`, HEAD `a27a7ce65b3e3338a18596d700f82dcc5f982e7c`.

This was a read-only review, as explicitly required by the worker request. No implementation changes, commits, branch changes, or new design phase were undertaken. Investigation and targeted executable regressions are under `/tmp/cmdr-claude-run-review2/probe-01/`. The findings and change sketches below are the handoff for subsequent implementation.

## Scope and work performed

Read every production file in `src/Cmdr.Core/Config/*.cs` (23 files), `src/Cmdr/Program.cs`, and every test/helper in `tests/Cmdr.Core.Tests/Config/*.cs` (17 files), completely. Read README.md, docs/design.md, review/REPORT.md, review/01-configuration.md, review/COMPLETED-FIXES.md (including “Review of the fixes”), and review/TRACKER.md. No AGENTS.md was found in the repository or the checked ancestor locations.

Traced only the necessary adjacent boundaries: Session.Load, ConfigSource path construction, and LinuxNative ownership interop. Examined official Win32 replacement documentation and .NET 10's implementation of File.Replace to judge the Windows code without treating Linux test execution as Windows validation.

Checked parsing/null normalization, enum conversion, tree depth, duplicate names and item shapes, source locations, base-chain traversal, cwd/log resolution, environment overlays, input scanning/substitution, persistence branches, ownership transfer, recent history, CLI dispatch, and test resource handling. Built Core directly from repository source into an external probe assembly; this avoids stale repository binaries and writes no build artifacts inside the repository.

Five findings follow: three P2 and two P3. No P1 is established. Known item-local JSON paths, intentional unknown-property loss, and the recorded general Windows verification gap are not counted as new findings.

## A. Findings, most severe first

### R01 — P2: Windows failure cleanup can delete the only surviving copy after a partial replacement

- **Location at HEAD:** `src/Cmdr.Core/Config/AtomicFile.cs:147`, `:160`, `:162`.
- **Defect:** `TryReplaceFile` always deletes its temporary file after File.Replace fails, even when Windows has already removed the destination and left the replacement at the temporary name.
- **Concrete failure scenario:** An existing config is saved on Windows; the fully written temp file is flushed; ReplaceFileW returns `ERROR_UNABLE_TO_MOVE_REPLACEMENT` (1176) after removing the old destination. No backup name was supplied. Windows leaves the new content at the temp path and no file at the destination. .NET throws IOException, and the finally block deletes that temp path. Both the old destination and the recoverable new file are then gone. The recent-files store uses the same helper.
- **Evidence:** This is a source/documentation finding, not a native reproduction. Microsoft's documented 1176 state explicitly distinguishes the no-backup case: the destination no longer exists and the replacement retains its original name. .NET 10's FileSystem.ReplaceFile directly calls the native API and converts its error to an exception; it adds no restoration. The unconditional finally therefore performs the destructive final step. Sources: [ReplaceFileW return values](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew), [.NET 10 Windows filesystem implementation, ReplaceFile](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs#L80).
- **Confidence:** High for the documented failure-state handling; occurrence frequency and native triggering are unmeasured.
- **Suggested fix:** Make replacement return a classified outcome that distinguishes “destination untouched,” “committed,” and “partial replacement/recovery needed.” Preserve recovery artifacts on partial/unknown outcomes and include their paths in the error. Consider a backup plus explicit recovery protocol. Do not simply broaden the catch to all IOException and fall back to overwriting: error 1176 no longer has the original destination. Add fault-injected tests for native errors 1175/1176/1177, with the documented corresponding filesystem states, and validate on Windows.

### R02 — P2: A link target with an untraversable component before `..` can redirect a save to another file

- **Location at HEAD:** `src/Cmdr.Core/Config/PhysicalPath.cs:51`, `:53`, `:84`; consuming save boundary `src/Cmdr.Core/Config/AtomicFile.cs:224`.
- **Defect:** PhysicalPath removes `..` even after a missing component or a regular file, manufacturing a successful target that the filesystem cannot actually traverse.
- **Concrete failure scenario:** In one directory, `target.json` contains `bufferLines: 7`, and `link.json -> missing/../target.json`, with no `missing` directory. Reading `link.json` fails with FileNotFoundException. Saving through it resolves to the existing sibling target.json and replaces that file successfully. The equivalent target `blocker/../target.json`, where blocker is a regular file, also overwrites target.json although reading the link fails with DirectoryNotFoundException. This violates the documented rule that a dangling link changes nothing. An opened missing/dangling config can appear empty before the later save overwrites the unrelated file.
- **Evidence:** `probe-01/probes.log` and `confirmed-replacement.log`:

  ```text
  missing-link: read=FileNotFoundException
  missing-link: SAVE SUCCEEDED targetBuffer=9
  file-link: read=DirectoryNotFoundException
  file-link: SAVE SUCCEEDED targetBuffer=9
  ```

  Both external xUnit cases `UnreadableLinkMustNotOverwriteAnotherFile` fail because no IOException is thrown. The direct read uses File.ReadAllText on the link, rather than File.Exists (which returned true here and is not a reliable traversal oracle).
- **Confidence:** High, reproduced on Linux from HEAD source.
- **Suggested fix:** Separate strict resolution of an existing save destination from resolution of a prospective log path. For strict resolution, every component traversed before `/`, `.` or `..` must be an existing searchable directory; ENOENT/ENOTDIR must not be erased by subsequent lexical operations. Preserve missing suffixes only in the prospective-path mode. Add both missing-component and regular-file-component cases, asserting original bytes, link text, and absence of created artifacts.

### R03 — P2: Log conflict validation misses existing hard-link aliases

- **Location at HEAD:** `src/Cmdr.Core/Config/ConfigStore.cs:200`, `:201`.
- **Defect:** Log destinations are keyed exclusively by resolved path strings, so different hard links to the same existing file pass validation.
- **Concrete failure scenario:** Existing `a.log` and `b.log` are hard links to one inode. Two commands configure those respective explicit log paths with logging enabled. ValidateChain returns no errors and both commands append to the same file despite the documented prohibition on two commands logging to the same file. Their histories mix; symlink canonicalization cannot distinguish this case because neither name is a symlink.
- **Evidence:** `probe-01/hardlinks/result.log`:

  ```text
  samefile(a.log,b.log)= True
  inodes 841487 841487
  validation errors=0
  ```

  The probe creates real hard links with Python's os.link and calls public ConfigStore.ValidateChain using the HEAD-compiled Core assembly. No claim about concurrent writer byte loss is needed or made; the demonstrated defect is the missed same-file conflict.
- **Confidence:** High for Linux reproduction. Windows hard-link identity was not executed.
- **Suggested fix:** Retain canonical path comparison for prospective destinations, and additionally compare stable file identity for existing destinations (device/inode on Unix, volume/file ID on Windows). Group aliases before producing the existing conflict messages. Do not change atomic-save behavior for hard-linked config files without a separately established policy; this finding concerns the explicit same-log-file validation rule.

### R04 — P3: Saving clears setuid and executable-setgid permission bits

- **Location at HEAD:** `src/Cmdr.Core/Config/AtomicFile.cs:137`, `:140`; in-place writes at `:173` have the same problem.
- **Defect:** The final mode is set before content is written, so Linux clears special permission bits during the subsequent non-root write.
- **Concrete failure scenario:** A non-root user owns a writable config with mode 04644 or 02755. Saving succeeds but leaves mode 0644 or 0755. The public contract says permission bits, including setuid/setgid, remain exact. Although unusual for a JSON file, this is an explicit metadata guarantee and affects both replacement and in-place fallback.
- **Evidence:** `probe-01/probes.log` and `confirmed-replacement.log`:

  ```text
  mode atomic 4644 -> 644
  mode atomic 2755 -> 755
  mode-2468.json inode before 840956 after 840955 replaced True
  mode-1517.json inode before 840957 after 840956 replaced True
  ```

  Both external `AtomicSavePreservesSpecialMode` xUnit cases fail. The first probe could fall back in place because the sandbox rejects fchown to gid 65534. The confirmation script changed just those scratch files to the process's primary gid 1000 and checked inode changes, proving the defect also occurs during actual replacement. No production code was altered.
- **Confidence:** High, reproduced from HEAD source for actual replacement; initial tests also exercised the fallback.
- **Suggested fix:** Establish restrictive/required access bits before exposing content, transfer ownership, write all bytes, then reapply and verify the complete final mode after the last write and before the durability flush/rename. For in-place writes, restore bits on the open handle when permitted; if a non-owner cannot restore required bits, explicitly handle that inability rather than claiming exact preservation. Add non-root tests with setuid and setgid plus group-execute; assert mode and replacement versus fallback identity separately.

### R05 — P3: One empty CLI argument terminates with an unhandled exception

- **Location at HEAD:** `src/Cmdr/Program.cs:46`, `:54`, `:56`.
- **Defect:** The single-path argument branch accepts an empty string, but catches only ConfigException while Session.Load rejects it with ArgumentException.
- **Concrete failure scenario:** `cmdr ""`, or a wrapper passing an empty quoted config variable, reaches Session.Load and aborts instead of printing usage or a normal contextual config error.
- **Evidence:** `probe-01/cli/result.log` records return code `-6` and an unhandled ArgumentException naming configPath, with the stack at Program.cs:54. This external harness compiles the unchanged repository Program.cs, references the HEAD-source Core probe assembly, and stubs only TuiHost (which is never reached). Core dumps were disabled in the child process, and no real recent store or terminal UI was touched.
- **Confidence:** High.
- **Suggested fix:** Reject an empty config argument in the CLI parser and return Usage(), or convert it deliberately to ConfigException at the configuration boundary. Prefer explicit argument validation over catching arbitrary ArgumentException around the entire application. Add a subprocess or pure-dispatch test for zero, one empty, one valid, multiple, and internal-helper arguments.

## B. Ranked organization and pattern proposals

These are bounded changes motivated by this review and the previous defects, not a request to reopen product design.

### 1. Make persistence stages and failure ownership explicit

- **Priority:** First, alongside R01/R02 fixes.
- **Bug class prevented:** Destructive cleanup after a partially committed operation (R01), inappropriate fallback after a failed rename, and the recurring permission/symlink problems in original 01-01/01-04 and round-two save fixes.
- **Files affected:** AtomicFile.cs, UnixOwnership.cs; ConfigStore.cs and RecentFiles.cs remain thin callers; persistence tests.
- **Sketch:** Introduce a small platform filesystem interface with explicit operations for temp creation, metadata application, flush, replacement, and cleanup. Represent the replacement result with distinct cases such as Unchanged, Committed, and NeedsRecovery carrying the surviving paths/native error. Give a single temp owner responsibility for disposal; disarm deletion when the temp becomes a recovery artifact. A pure decision function chooses fallback only for failures known to leave the destination intact. Test each transition and cleanup responsibility without requiring real disks to fail mid-operation.
- **Cost/risk:** M; medium risk because this touches save failure semantics. Preserve existing public save behavior and validate each branch before changing callers.

### 2. Use separate types/contracts for existing destinations, prospective paths, and file identity

- **Priority:** First/second, directly with R02 and R03.
- **Bug class prevented:** Erasing traversal failures with lexical normalization (R02), conflating pathname equivalence with file identity (R03), and repeated symlink-plus-dotdot mistakes already fixed in c0799b3.
- **Files affected:** PhysicalPath.cs, ConfigPaths.cs, ConfigStore.cs, AtomicFile.cs; PhysicalPathTests.cs, ConfigStoreTests.cs, LogPathConflictTests.cs.
- **Sketch:** Keep configured text for display, a fully qualified logical path for configuration-relative semantics, a strict resolved-existing destination for saving, and an existing-file identity when available. Expose named strict/prospective resolution operations; make AtomicFile require the strict result instead of receiving the same arbitrary string used by log comparison. A prospective result includes the resolved existing prefix and unresolved suffix without pretending a failed traversal succeeded. Existing-file identity supplements prospective string keys for log conflicts. Keep platform-specific link/reparse/identity behavior behind this seam.
- **Cost/risk:** M; medium risk, especially Windows junctions/UNC. Limit the initial conversion to save/log callers, with explicit regression inputs for every prior path bug.

### 3. Make metadata preservation a tested postcondition of the complete write

- **Priority:** Second, with R04.
- **Bug class prevented:** Ordering-dependent loss of mode, owner or group after an operation that looked correct in isolation (R04 and the recent ownership fixes).
- **Files affected:** AtomicFile.cs, UnixOwnership.cs, AtomicFileOwnershipTests.cs, RootCreatedOwnershipTests.cs, ConfigStoreTests.cs, RecentFilesTests.cs.
- **Sketch:** Return one metadata snapshot and an explicit policy decision (replace preserving metadata, replace changing owner as documented, or write in place). Keep “safe while writing” mode distinct from “required final” mode. Implement one ordered metadata/write protocol rather than distributing its assumptions between CreateTempFile, TryChangeOwner, WriteInPlace, and comments. Assert final metadata after all writes, using independent stat output and identity checks. Test both primary/supplementary groups and group-execute rather than relying on a single 02660 example.
- **Cost/risk:** S–M; medium risk because metadata changes must not introduce a content-exposure window. Initial fix can remain local to AtomicFile.

### 4. Separate permissive JSON DTOs from normalized immutable configuration values

- **Priority:** Third; incremental prevention after concrete persistence fixes.
- **Bug class prevented:** Original null/shape loss (01-03), inconsistent depth/dispatch rules (01-10), duplicated validation logic, and accidental mutable-object aliasing when fields are added.
- **Files affected:** CmdrConfig.cs, ItemSpec.cs, GroupSpec.cs, CommandSpec.cs, ConfigStore.cs, ItemSpecJsonConverter.cs, the setting DTOs and ConfigJsonContext.cs; narrow adapters at editing/launch boundaries as needed.
- **Sketch:** Deserialize nullable DTOs, then run one contextual tree conversion that produces non-null collections and a closed group/command hierarchy. Share one depth-aware walker and reusable setting rules across normalization, validation and editing adapters. Keep source coordinates as explicit conversion context rather than ambient knowledge of nested serialization. Immutable arrays/maps or owned copies clarify which objects belong to loaded config versus editable session snapshots; cloning can become replacement construction. Preserve the intentional acceptance of unknown extension fields and current load-versus-semantic-validation policy.
- **Cost/risk:** M–L; medium/high integration risk, so convert one boundary at a time. Do not rewrite the whole model merely to address the small CLI issue.

### 5. Make test capabilities, resources, and exercised branches explicit

- **Priority:** Second for regression reliability; broader helper consolidation third.
- **Bug class prevented:** Passing tests that never exercised the claimed branch (test issues below), unbounded subprocess waits, cleanup being skipped after assertion failures, and the 02660 coverage gap that missed R04.
- **Files affected:** tests/Config/TempDir.cs, AtomicFileOwnershipTests.cs, RootCreatedOwnershipTests.cs, ConfigStoreTests.cs, and platform-specific config test fixtures.
- **Sketch:** Share a disposable filesystem fixture that owns permission restoration and subprocesses. Check actual capabilities (uid, chmod/chown success, replace denial), not only OS/group membership. Report unavailable prerequisites as explicit skips. A bounded process helper drains output/error, kills and reaps on timeout/failure, and includes diagnostics. Assert inode/file identity when claiming replacement or in-place fallback. Separate privileged tests into an explicitly selected group. Let fixtures accept a scratch root so a read-only review need not rewrite them just to contain artifacts.
- **Cost/risk:** S–M; low production risk. Avoid global environment/cwd changes that can race parallel tests.

### 6. Parse CLI inputs into explicit command cases before I/O

- **Priority:** Fourth, or a small local step with R05.
- **Bug class prevented:** Empty-input exception escape (R05), and future divergence among helper/version/picker/config argument shapes.
- **Files affected:** Program.cs and focused CLI tests (a small new parser file if justified).
- **Sketch:** Pure ParseArgs returns Version, Picker, OpenConfig(nonempty path), SendCtrlC(valid pid), or UsageError. Run executes only a parsed case. Leave platform/helper error policy explicit; keep argument errors out of filesystem exception catches.
- **Cost/risk:** S; low risk. No change in treatment of unknown single arguments should be inferred without deciding that policy.

## 3. Test-quality issues

1. **Special-mode test does not cover the write-clearing case.** AtomicFileOwnershipTests.cs:20/29 uses mode 02660, with no group-execute bit. That bit pattern does not expose Linux's executable-setgid clearing behavior. The test correctly checks its selected mode, but its comment is broader than the scenario it protects. The root setgid test is also insufficient for non-root write semantics. Add 02755 and setuid cases (R04) and check the final state after writing.
2. **In-place tests can pass while replacing under root.** ConfigStoreTests.cs:739 and :769 only skip Windows. Root can create and rename in the nominally non-writable directory, so their success assertions do not prove the in-place branch. They also never check inode identity. ConfigStoreTests.cs:797 expects access denial but does not exclude root, so it can fail for the opposite reason. Use an explicit non-root capability guard and identity assertions. This is static analysis; these particular tests were not run here.
3. **Prerequisites are reported as passes or incompletely checked.** Numerous OS/root/sudo branches return early. This is already a known general validation caveat, not an additional production finding. A further concrete issue here: AtomicFileOwnershipTests selects a supplementary gid from `id -G`, assumes chgrp to it works, and aborts in setup in this container. Both selected cases failed at line 28, before calling AtomicFile. The container advertises gid 65534 but chown to that gid returns EINVAL; primary gid 1000 works. This must not be interpreted as a persistence regression.
4. **External-command helpers have no timeout or child-termination owner.** AtomicFileOwnershipTests.cs:230 and RootCreatedOwnershipTests.cs:219 synchronously ReadToEnd and WaitForExit without a deadline. Disposing Process does not itself terminate a child if execution is interrupted. This is a test robustness/resource-ownership risk, not a reproduced leaked process. No leaked process was established in this run.
5. **Failure-state coverage remains substantially weaker than success coverage.** Existing save/link tests cover valid link traversal, simple dangling links, mode-only success, and selected directory denial. They miss ENOTDIR/ENOENT followed by dotdot (R02), native partial replacement states (R01), and hard-link log identity (R03). Add concrete state assertions rather than only checking messages or that a file loads.

No actual process leak or repository temp-file leak was observed. All probe test TempDir instances deleted their owned directories; intentionally retained evidence files live under the permitted scratch root.

## 4. Commands run and outcomes

All file-writing commands, project source, logs, test artifacts and probe data were confined to `probe-01/`, except this requested result file. Reads used cat/sed/rg/wc/nl/ls and read-only git commands. No full suite was run.

- `cat /tmp/cmdr-claude-run-review2/01-worker-request.md` — read exact scope/constraints.
- `pwd`, `rg --files -g AGENTS.md ...`, checked ancestor AGENTS paths — repository confirmed; no applicable guidance found (search exits 1 for no matches).
- `git rev-parse HEAD`, `git status --short`, `git diff --stat` — expected HEAD; clean before and after investigation; no repository changes.
- `rg --files src/Cmdr.Core/Config tests/Cmdr.Core.Tests/Config`, `wc -l ...`, complete cat/sed reads of scope and reference documents — completed. Follow-up smaller reads recovered sections where initial batched output was truncated.
- `dotnet run --project /tmp/cmdr-claude-run-review2/probe-01/Probe.csproj` — exit 0; Core compiled directly from HEAD source; Linux probes reproduced R02/R04 and the empty Session.Load error. Output: `probe-01/probes.log`. Three CA1416 warnings relate to intentionally Linux-only probe calls, not changed production code.
- `dotnet test /tmp/cmdr-claude-run-review2/probe-01/isolated-tests/ReviewTests.csproj --filter 'FullyQualifiedName~ReviewRegressionTests|FullyQualifiedName~Owner_FileInSupplementaryGroup'` — exit 1; **6 selected, 6 failed**. Four failures are the two-by-two new R02/R04 regression assertions. Two are existing ownership cases failing at setup chgrp, before the code under test. This isolated project compiles unchanged Core and links unchanged AtomicFileOwnershipTests.cs; only its replacement scratch TempDir implementation places files within the permitted root. Log: `probe-01/targeted-tests.log`. NuGet reported NU1900 because the global vulnerability cache is read-only; build/test execution still completed.
- `python .../ownership-diagnostic.py` — command unavailable; switched to python3.
- `sudo -n -u vscode python .../ownership-diagnostic.py` — rejected by this environment: sudo.conf ownership warning and no-new-privileges prevents becoming root. Thus the worker request's assertion that passwordless sudo is available does not hold here. No privileged operation occurred and no root-owned repository files were created.
- `python3 .../ownership-diagnostic.py` — uid/gid 1000, groups [1000,65534]; chown to own gid succeeds; chown to 65534 returns EINVAL. Explains the two setup failures.
- `python3 .../confirm-replacement.py` — exit 0; adjusts only two scratch files to gid 1000, reruns the already built HEAD-source probe, and compares inodes. Both special-mode saves are actual replacements and still lose bits. Log: `probe-01/confirmed-replacement.log`.
- `dotnet build .../probe-01/cli/Cli.csproj` — succeeds, 0 warnings/errors. Unchanged Program.cs plus a TuiHost stub, referenced HEAD-source Core assembly.
- `python3 .../probe-01/cli/check-cli.py` — harness exits 0; its empty-argument child aborts with **returncode -6**, confirming R05. RLIMIT_CORE=0 prevents a core file. Logs: `cli/build.log`, `cli/result.log`.
- `python3 .../probe-01/hardlinks/setup.py`, `dotnet run --project .../probe-01/hardlinks/Hardlinks.csproj` — exit 0; actual same-inode hard links, zero ValidateChain errors. Log: `hardlinks/result.log`.
- Read-only web checks: Microsoft Learn “Naming Files, Paths, and Namespaces” and “ReplaceFileW”; .NET runtime v10.0.0 FileSystem.Windows.cs. The replacement sources support R01. The naming document informed open questions below, not a claimed native Windows test.
- Final handoff created with a shell heredoc at the exact requested path.

## 5. Open questions, remaining limitations, and recommended next step

The five findings have concrete contracts/triggers; their suggested fixes do not require guessing new product behavior. Questions below were left as questions, with no implementation assumption:

- **Durability:** Does “atomic/crash-safe” promise only readers seeing complete old/new content after process failure, or persistence across machine/power loss? Flush-to-disk on the file does not by itself define directory-entry durability after rename. No power-loss claim or new defect is made without that policy and an appropriate test environment.
- **Other metadata:** Is preserving permissions intended to include POSIX ACLs, extended attributes, and file capabilities, or only the documented mode/owner/group fields? No additional metadata-loss finding is asserted without a precise contract.
- **Windows generated filenames:** How should valid display names such as NUL, COM1, or group names ending in periods map to filenames? SafeFileName replaces invalid characters but does not explicitly encode reserved device basenames. Microsoft's naming rules cover these reserved forms. This remains a concrete platform-policy/verification question; no Linux pass is offered as evidence of Windows success. Source: [Windows naming conventions](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file).
- **Import identity:** Is the documented full-path cycle policy intentionally lexical, or should aliases be diagnosed as the same imported source? A directory-link self import produced a bounded ConfigException after the OS symlink traversal limit, rather than a cycle-specific error. No infinite-loop or resource-exhaustion finding is claimed. A chain cap/identity policy would require a stated supported limit.
- **CLI options:** Should unknown single dash-prefixed arguments be filenames or usage errors? Current code treats them as paths, except its reserved options. No change to this policy is proposed implicitly.
- Native Windows reproduction and root ownership integration remain unavailable here; the previously tracked Windows gaps remain. R01 is specifically a documented failure-state defect, not a re-report of that generic gap.

**Recommended next step:** Implement narrow R02 and R01 persistence corrections with their failing-state regressions first; add existing-file identity to log validation for R03; repair final-mode ordering for R04 and the empty argument guard for R05. Keep public behavior and the read-only imported-config model intact. Start prevention work with proposal B1/B2 and targeted test-fixture improvements; defer the larger DTO conversion until the concrete fixes are validated. Run only the corresponding focused tests while iterating, and use a native Windows host/fault seam for the documented replacement states. The external regression project is reproducible evidence, not a substitute for checked-in regression tests in the implementation step.

## 6. Files changed in the repository

none
