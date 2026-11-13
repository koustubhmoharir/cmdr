# Configuration and persistence review

Reviewed commit: `a06d405e01095309617069e43c5d4be9200c0ebc` (2026-11-13). Scope: every file in `src/Cmdr.Core/Config`, all configuration tests, and the session, launch, output and UI integration needed to establish consequences. Production code was not changed. All probe/build/test writes were made under `/tmp/cmdr-review01-8wvwbJ`; this report is the only repository write.

## Assessment

Ten verified findings: **six P2 and four P3**. No P1 established. The most important problems concern file permissions during saves, collisions in automatically generated log paths, silently discarded command fields, and saving through symbolic links. Existing tests exercise normal configuration behavior well but do not protect these boundaries.

Severity convention: P1 = urgent major failure; P2 = actionable correctness, integrity or compatibility defect under a concrete trigger; P3 = lower-impact robustness or diagnostic defect. Findings below are supported by focused execution on Linux. Windows-specific observations in the recommendations are static and are not counted as reproduced Windows defects.

## Functionality overview

- `CmdrConfig`, `GroupSpec`, `CommandSpec`, and the stop/log/readiness settings describe an ordered tree. Groups and commands share a case-insensitive sibling-name namespace. Cloning copies the mutable lists, dictionaries and setting objects.
- `ConfigStore.Load` supplies defaults for an absent opened file. Parsing accepts JSON comments and trailing commas, uses case-sensitive camel-case properties and source-generated JSON metadata, rejects legacy group `commands`, normalizes supported nested nulls, and rejects null collection elements/arguments. Loading intentionally does not perform semantic validation.
- `LoadChain` follows `base` relative to each importing file, detects lexical path cycles and returns deepest base first. Each file retains its own settings and groups; this is import composition, not property merging. `ValidateChain` validates files separately and prefixes base-file errors with their relative paths. Session integration preserves source ownership and saves only the opened file.
- Validation checks positive buffer size, nonblank names/programs, duplicate sibling names, stop/input compatibility, malformed argument input placeholders, and readiness regex syntax. It deliberately returns messages rather than rejecting the whole editable session.
- `ConfigPaths` resolves group working directories from outermost inward, command working directories against the resulting group directory, and log directories against the owning configuration directory. Explicit absolute paths replace their base; generated log paths include sanitized ancestor names.
- `EnvExpansion` supports previous-level `${NAME}`, `${cwd}`, `${configDir}`, `${pathsep}`, `$$`, null removals and undefined-as-empty values. Every overlay expands against a snapshot before that overlay, so sibling assignments are order-independent. Expansion is for environment values, not general shell expansion of paths or arguments.
- `InputPlaceholders` scans argument placeholders, deduplicates requests in encounter order, makes a name required if any occurrence requires it, supports an escape, and substitutes supplied values without rescanning inserted text. Sessions prepare substituted argument copies and retain the original placeholders for saving.
- `Save` serializes first, writes and flushes a uniquely named sibling temporary file, renames it over the destination, and removes remaining temporary files. Output is indented UTF-8 without BOM plus a trailing newline. `RecentFiles` has a separate tolerant JSON store, bounded to 20 entries, with full-path insertion/removal and best-effort atomic persistence.

## Verified findings

### 01-01 — P2: Saving a private configuration broadens its permissions

**References:** `src/Cmdr.Core/Config/ConfigStore.cs:156`, `:159`, `:166`. The same replacement pattern occurs in `src/Cmdr.Core/Config/RecentFiles.cs:126`, `:129`, `:136`.

**Trigger:** Open an existing Unix configuration with mode `0600`, then call `ConfigStore.Save` under a less restrictive umask. Environment overlays and arguments can contain credentials, making preservation of a user's restrictive mode meaningful.

**Evidence:** The Linux probe created an ordinary `0600` file and saved it. `File.GetUnixFileMode` changed from `UserRead, UserWrite` to `UserRead, UserWrite, GroupRead, GroupWrite, OtherRead` (`0664` on this host). The replacement is a newly created file using default permissions; no existing metadata is transferred. The temporary file is also created with those default permissions before the configuration is written.

**Impact:** Saving can expose formerly private configuration contents to other users and grant group members write access. Merely correcting the destination mode after replacement would leave an exposure interval while writing the temporary file. Recent-file path privacy has the same underlying issue, though lower sensitivity.

**Fix direction:** Create the temporary file with restricted permissions from the outset. Preserve the existing destination's intended mode before writing/replacing it, with a defined policy for newly created files. Review Windows ACL handling separately rather than assuming the Unix result applies to Windows.

**Regression test:** On Unix, save a `0600` configuration under a permissive umask and assert its mode remains `0600`, its contents round-trip, and no temporary file remains. Use a subprocess to isolate umask changes. Cover restrictive existing recent-store permissions as well.

### 01-02 — P2: Distinct valid names can share a default log file and lose output

**References:** `src/Cmdr.Core/Config/ConfigPaths.cs:80`, `:93`, `:98`; `src/Cmdr.Core/Config/CmdrConfig.cs:122`; consequence at `src/Cmdr.Core/Output/CommandOutput.cs:175`.

**Trigger:** Define sibling commands `a b` and `a_b`, or distinct group branches whose names sanitize to the same directory, and enable their default logs. A related conflict occurs with a command `build` beside a nested group `build.log`: the first command's log file is the second branch's required directory.

**Evidence:** Both commands pass `CmdrConfig.Validate` with zero errors. `ResolveLogPath` returns the same `logs/g/a_b.log` for both. In a Linux probe, two `CommandOutput` instances both reported `IsLogging == true`; after writing `first-log-output` and `second-log-output` respectively, the file contained only `second-log-output`. Independent append streams have no coordination here. A follow-up probe confirmed that the valid `build`/`build.log` tree produces `logs/g/build.log` and `logs/g/build.log/child.log`: the first log opens and the child log is disabled because its parent is a file. The collision itself is platform-independent; Windows concurrent-open behavior was not executed.

**Impact:** Ordinary, valid display names can cause silent log loss or a log-open failure on a platform enforcing the sharing restriction. Even nonconcurrent commands unexpectedly mix histories. Allowed duplicate top-level names across imported files can also collide when those files share a log directory; raw sibling-name validation cannot establish log-path uniqueness.

**Fix direction:** Make generated path components collision-resistant, for example by escaping characters reversibly or adding a stable suffix derived from original identity/source. Alternatively detect conflicting resolved log paths across a session and provide an actionable error. Preserve deliberate sharing of explicitly configured paths only if that behavior is defined and supported.

**Regression test:** Assert distinct default paths for `a b`/`a_b`, `/`/`_`, and colliding ancestor names. Add a two-output integration test proving both logs retain their own content; include imported sources sharing a log root and file-versus-directory conflicts from names ending in `.log`.

### 01-03 — P2: Command-shaped objects in `groups` are accepted and silently discarded

**References:** `src/Cmdr.Core/Config/CmdrConfig.cs:27`; `src/Cmdr.Core/Config/ConfigStore.cs:186`, `:194`, `:238`; compare nested-item rejection in `src/Cmdr.Core/Config/ItemSpecJsonConverter.cs:24`–`:29`.

**Trigger:** A hand-edited top-level entry contains `program`, whether alone or together with `items`:

```json
{"groups":[{"name":"build","program":"dotnet","args":["build"],"items":[]}]}
```

**Evidence:** The probe loaded one empty group, returned zero validation errors, and saved a file containing neither `program` nor the arguments. Top-level entries deserialize directly to `GroupSpec`, so these known command fields are treated as unknown properties. The identical mixed shape nested under `items` is correctly rejected.

**Impact:** A misplaced command or ambiguous group/command appears successfully loaded, never runs, and is permanently erased on the next save. This is narrower than the intentional general acceptance of unknown extension properties: the fields identify an incompatible existing item shape.

**Fix direction:** Validate top-level group shape before typed deserialization; reject `program` on entries in `groups` with the full JSON path. Keep supported empty groups without an `items` property valid. Do not broadly reject all unknown properties unless the compatibility policy changes.

**Regression test:** Reject a top-level `program` object and a top-level object containing both `program` and `items`, with `groups[index]` in the error. Retain tests allowing minimal empty groups and unknown extension fields.

### 01-04 — P2: Saving through a symbolic link replaces the link instead of updating its target

**References:** `src/Cmdr.Core/Config/ConfigStore.cs:114`, `:150`, `:156`, `:166`.

**Trigger:** Open `linked.json`, a file symlink to a shared configuration, edit a setting and save to that same path.

**Evidence:** The probe created `linked.json -> target.json`, with target `bufferLines = 7`, loaded through the link, changed the value to 8, and saved through the link. Afterwards `linked.json` was a regular file (`LinkTarget == null`) containing 8; `target.json` still contained 7. Reading follows the symlink, while replacement renames over the link's directory entry.

**Impact:** A successful-looking save breaks a user's shared configuration setup and leaves other users of the target reading stale data. There is no warning that the link is being removed.

**Fix direction:** Define symlink semantics explicitly. For supported links, resolve the final destination before choosing the sibling temporary file and atomically replace that target while retaining the link. Otherwise reject a symlink destination with an actionable error. Account for dangling links and relative link targets.

**Regression test:** On a host supporting file symlinks, load/edit/save through a relative and an absolute link and assert the link remains intact and the target changes; cover dangling links and failure cleanup under the chosen policy. Windows link behavior needs its own supported-host test.

### 01-05 — P2: Load failures escape the configuration error boundary or masquerade as a new file

**References:** `src/Cmdr.Core/Config/ConfigStore.cs:23`, `:55`, `:64`, `:114`, `:125`; `src/Cmdr.Core/Session/ConfigSource.cs:18`. Callers catch only `ConfigException` at `src/Cmdr/Program.cs:52`–`:60` and `src/Cmdr/Tui/RecentPicker.cs:110`–`:118`.

**Triggers and evidence:**

- Reading a real file with mode `0000` throws `UnauthorizedAccessException` directly from `File.ReadAllBytes`, outside the parse `try`. This was reproduced as the ordinary non-root review user.
- A valid JSON document containing `{"base":"bad\u0000file"}` makes `LoadChain` throw raw `ArgumentException`. `{"logsDir":"bad\u0000dir"}` similarly makes `Session.Load` throw while constructing `ConfigSource`, before it can expose validation errors.
- With an existing configuration under an inaccessible parent directory, `File.Exists` returns false and `Load` returns a new default configuration. The probe's real `bufferLines = 7` was presented as `10000`. The same existence check can mislabel an inaccessible base as nonexistent.

**Impact:** Expected user/configuration errors can terminate the CLI or escape the picker's normal error dialog. A file the user cannot inspect can instead appear to have no commands, which is misleading and creates an overwrite risk if access is later restored and that empty session is saved. An actual destructive overwrite was not performed or claimed.

**Fix direction:** Open/read the file and distinguish genuine absence from access or other I/O failures instead of relying on `File.Exists` as an error classifier. Wrap expected read/path-resolution failures with `ConfigException`, including the configuration/importer and setting path. Handle invalid `logsDir` at a boundary the CLI/picker already handles, or report it as an editable validation error with a safe fallback.

**Regression test:** Missing opened files must still yield defaults, missing bases must still fail, unreadable opened/base files must produce contextual `ConfigException`, and inaccessible parents must not yield defaults. Add NUL-containing base/log directory tests through `Session.Load`. Permission tests should explicitly require a non-root Unix process.

### 01-06 — P2: UTF-8 BOM files are rejected; recent history is silently lost on read

**References:** `src/Cmdr.Core/Config/ConfigStore.cs:114`, `:118`, `:123`; `src/Cmdr.Core/Config/RecentFiles.cs:168`, `:169`, `:179`.

**Trigger:** Save an otherwise valid configuration or recent store as UTF-8 with a BOM, e.g. `File.WriteAllText(path, json, new UTF8Encoding(true))`. This concerns input; continuing to write BOM-free output is appropriate.

**Evidence:** The BOM configuration probe using `{}` raised `ConfigException` at line 1, position 1: `'0xEF' is an invalid start of a value`. A BOM recent store with one valid absolute path loaded with zero entries. Both consumers pass the raw bytes, including `EF BB BF`, to the JSON reader.

**Impact:** A valid configuration body becomes unusable after an encoding-only edit. The tolerant recent-store reader treats the same encoding issue as corruption and can overwrite the existing history when recording the next open. This is a reproduced interoperability limitation, not a claim that JSON readers are universally required to accept BOMs.

**Fix direction:** Strip a leading UTF-8 BOM consistently before both document inspection and typed deserialization. Apply the same treatment to recent stores. Keep the writer's existing BOM-free format.

**Regression test:** Load BOM-prefixed empty and nonempty configurations, including an imported base and a comment-prefixed body, and a BOM recent store. Verify semantic equality with their BOM-free equivalents and preserve the existing save-format tests.

### 01-07 — P3: Recent-file validation retains unusable paths that later cannot be removed

**References:** `src/Cmdr.Core/Config/RecentFiles.cs:60`–`:72`, `:87`–`:90`, `:106`–`:108`; picker actions at `src/Cmdr/Tui/RecentPicker.cs:112`, `:133`.

**Trigger:** A malformed or hand-edited `recent.json` contains an absolute string with a NUL, such as `"/tmp/a\u0000b"`.

**Evidence:** `RecentFiles.Load` retains the entry because `Path.IsPathFullyQualified` does not validate all path contents. The probe returned one entry; `Remove` then threw `ArgumentException: Null character in path` while canonicalizing it. `Session.Load` similarly canonicalizes before the picker's `ConfigException` handler can handle the error.

**Impact:** An invalid recent entry can break both opening and deleting that entry, despite the store's otherwise tolerant recovery design. Qualification alone also leaves `.`/`..` spellings noncanonical, while `Add` and `Remove` canonicalize their inputs, making their identity rules inconsistent. This was also reproduced: loading canonical `/tmp/.../a.json` beside `/tmp/.../sub/../a.json` kept two entries; removing the alias removed the canonical entry first, leaving the alias, and a second removal of that remaining alias returned false.

**Fix direction:** Validate and normalize each candidate with `Path.GetFullPath` inside a per-entry expected-exception guard, then deduplicate normalized results with the platform comparer. Drop unusable entries while preserving valid ones.

**Regression test:** Mix valid paths with a fully qualified NUL-containing entry and assert only the valid paths remain and normal picker removal succeeds. Include a normalized path and its `sub/../file` equivalent and assert one removable entry.

### 01-08 — P3: String enum converters accept comma-separated combinations for single-choice settings

**References:** `src/Cmdr.Core/Config/GroupSpec.cs:56`; equivalent converter declarations at `src/Cmdr.Core/Config/StopSpec.cs:30`, `src/Cmdr.Core/Config/LeftoverMode.cs:25`, `src/Cmdr.Core/Config/StdinMode.cs:28`.

**Trigger:** Supply a composite string such as `"mode":"parallel, sequential"` instead of one supported mode.

**Evidence:** The probe loaded `parallel, sequential` as `GroupMode.Sequential`, `parallel, parallel` as `Parallel`, and `sequential, sequential` as `Sequential`, all with zero validation errors. The generic string enum converter processes comma-delimited enum names even though these settings represent a single choice. Rejecting integers does not enforce a single canonical token. Noncanonical `Parallel` alone was correctly rejected; the defect is not general case insensitivity.

**Impact:** Invalid/ambiguous input silently becomes a valid execution policy and is rewritten in canonical form on save. The practical blast radius is limited because the current enums have only two values, but this is a poor foundation for extending them: combinations can acquire a different meaning when more members are added.

**Fix direction:** Use a shared strict single-token enum converter or explicitly reject comma-separated input before delegating. Preserve existing exact token spelling and integer rejection behavior.

**Regression test:** For every configured enum, reject mixed and repeated comma-separated names and retain acceptance/round-trip tests for each supported single value. Follow-up Linux probes also accepted `stdin: "pipe, console"` as `Console`, `leftovers: "track, detach"` as `Detach`, and `stop.method: "ctrlC, input"` as `Input` (with stop input supplied); all returned zero validation errors.

### 01-09 — P3: Nested JSON errors report item-local lines as whole-file coordinates

**References:** `src/Cmdr.Core/Config/ItemSpecJsonConverter.cs:16`, `:34`, `:39`; `src/Cmdr.Core/Config/ConfigStore.cs:125`–`:129`.

**Trigger:** A syntactically valid, multiline configuration has an invalid typed value inside a nested command, e.g. `"args":12` on physical line 9.

**Evidence:** The probe's invalid `args` was on line 9, but `ConfigException` reported line 4, position 21 and `Path: $.args`. Deserializing a detached item `JsonElement` starts a new reader; its relative coordinates/path are then formatted by `ConfigStore.Parse` as if they referred to the original file. The existing malformed-JSON test passes because the initial whole-document parse catches syntax errors with correct original positions.

**Impact:** Users are directed to an unrelated line and lose the enclosing `groups/items` path precisely when fixing nested configuration errors.

**Fix direction:** Preserve the original reader's location/path when discriminating and deserializing items, or explicitly attach the containing JSON path and translate source positions before constructing the configuration error. Do not present item-local coordinates as file-global coordinates.

**Regression test:** Put an invalid `args`, enum, or readiness property on a known physical line in a nested group and assert the original file line and full enclosing JSON path. Keep the existing root syntax-error coordinate test.

### 01-10 — P3: Editable deep trees pass validation but cannot be saved through the normal error handler

**References:** `src/Cmdr.Core/Config/CmdrConfig.cs:73`, `:115`; `src/Cmdr.Core/Config/ConfigJsonContext.cs:6`; `src/Cmdr.Core/Config/ConfigStore.cs:154`; session validation/save at `src/Cmdr.Core/Session/Session.cs:358`–`:367`; UI error handling at `src/Cmdr/Tui/MainController.cs:899`–`:904`.

**Trigger:** Repeatedly add nested groups through the supported session editing API until the resulting JSON exceeds the serializer's default maximum depth. There is no model validation limit on nesting.

**Evidence:** An acyclic chain of 40 groups created with `Session.AddGroup` returned zero errors from `Session.Validate`. `Session.Save` threw `JsonException` reporting the maximum depth of 64. The original file remained unchanged and the session stayed dirty. The UI save handler catches validation `InvalidOperationException` and I/O/access failures, but does not handle this serialization exception. This is a reproducible depth limit, not an actual object cycle in the model.

**Impact:** A tree the editor accepts cannot be persisted, and the failure escapes the normal save-error dialog. The depth is unusual, so this is P3, but the restriction should be visible before users accumulate unsavable edits.

**Fix direction:** Define a supported tree depth consistent with both whole-document loading and serialization, validate it with a useful group path, and surface serialization failures safely at the save boundary. Raising only the writer's limit would allow files the initial document reader still rejects.

**Regression test:** Build an acyclic tree at the supported boundary and round-trip it; build one beyond the boundary and assert a contextual validation/save error with the old file intact, session still dirty, and no exception escaping the UI save action.

## Strengths

- The import model is explicit and consistently implemented: independent settings, importer-relative base paths, deepest-first presentation, source-aware validation and read-only base ownership all have useful integration tests. No erroneous merge behavior was found.
- Source-generated JSON metadata and explicit runtime-type writing provide an inspectable serialization surface. Nested shape ambiguity and legacy group syntax are rejected deliberately rather than silently migrated.
- Recursive normalization clearly distinguishes supported null settings from invalid null entries. Error messages for null arguments and nested collection entries include actionable indexes.
- Validation covers duplicate names across mixed command/group siblings and recursively labels errors. Readiness patterns are syntax-checked; runtime matching separately has a bounded timeout, so an absent validation-time match timeout is not itself a regex-execution defect.
- Environment snapshots implement the documented previous-level semantics correctly for production platform dictionaries. Undefined variables, removal visibility, built-in precedence, escaping and per-level working directories have direct tests and real-process coverage.
- One scanner serves placeholder errors, requests and substitution, limiting grammar drift. Required-wins semantics, case sensitivity, literal escapes, copied arguments and sequential propagation are exercised. No configuration-layer placeholder substitution or secret-persistence defect was established.
- Saves serialize before replacing the destination and flush file data before rename. Existing success-path tests verify overwrite, directory creation, temporary-file cleanup, UTF-8 round trips and formatting. Session validation prevents an invalid semantic model from being saved through the UI.
- Recent history is bounded, isolated from main configuration, and tolerant of normal malformed JSON, missing files and null entries. Its save failures do not block opening a configuration.

## Maintainability, extensibility and recommendations (not additional verified production defects)

- **Unknown property preservation:** `ConfigJsonContext.cs:6` and the model types intentionally accept unknown properties, as tested by `ConfigStoreTests.Load_AcceptsCommentsTrailingCommasAndUnknownProperties`. `Save` drops those properties, along with comments. This is existing policy, not counted as a separate bug. For future-version interoperability, consider extension-data retention or a version/compatibility policy; session reconstruction and clones must preserve extension data too if added.
- **Multiple item types require coordinated changes:** adding another `ItemSpec` requires updates to `ItemSpecJsonConverter`, generated metadata, normalization, validation, cloning and session tree traversal. `CmdrConfig.ValidateItems` and `ConfigStore.NormalizeGroup` currently have no unsupported-type failure branch, although JSON writing does. Keep the hierarchy intentionally closed, or centralize traversal/dispatch and reject unknown runtime types consistently. No currently serialized type was found omitted.
- **Shared policy duplication:** `ConfigStore.Save` and `RecentFiles.TrySave` duplicate temporary-file persistence, which duplicates the permission/link problems and future fixes. Extract a narrowly scoped persistence helper with explicit metadata, link and cleanup policies. Readiness validation also exists in both `CmdrConfig` and `CommandForm`; share reusable rules so additions do not drift. Their current empty-field differences are largely explained by the UI's conversion policy.
- **Environment comparer API contract:** `EnvExpansion.ApplyLevels` documents target-owned key comparison at `EnvExpansion.cs:81` but builds lookup snapshots from the host OS at `:89`. On Linux, a case-insensitive target containing `PATH=/bin` resolves `target["path"]` to `/bin`, while expanding `${path}` produces empty. This mismatch was probed, but production process dictionaries follow host semantics and `EnvLevelsTests` explicitly asserts the platform behavior. Treat this as an API contract/extensibility recommendation: document the platform requirement or accept an explicit comparer used consistently by caller and snapshot.
- **Windows path policy:** character replacement is not a complete Windows filename policy (`ConfigPaths.cs:86`–`:106`). Device basenames and trailing-dot aliases deserve Windows-host regression coverage and a deliberate encoding strategy. Do not infer successful Windows log creation from Linux sanitizer tests. These Windows behaviors were not executed here and are not added to the verified-finding count.
- **Filesystem identity in import cycles:** `LoadChain` uses normalized path strings and an OS-wide case heuristic (`ConfigStore.cs:55`–`:61`, `:108`). Directory symlinks, case-sensitive volumes on otherwise case-insensitive OS defaults, and aliases are not tested. Consider a documented link/identity policy and a chain-length bound. No infinite-loop or cycle-bypass claim was established in this review.
- **Concurrent saves and durability:** unique sibling temporary files protect readers from partial replacement, but do not detect stale editors or merge simultaneous recent-list changes. Consider change detection if multiple instances are a supported workflow. Flushing the file before rename does not establish tested power-loss durability of the directory entry. No crash-injection, concurrent-writer or power-loss test was run; these are design considerations, not observed failures.
- **Validation boundaries:** missing working directories are handled by launch failure and invalid working-directory syntax is rendered as an error in the details pane. Do not expand semantic validation to require all paths or executables to exist at load time without considering generated build directories and PATH lookup. Conversely, base/log directory syntax is processed during load, so the unhandled cases in 01-05 require an explicit boundary.

## Coverage and tests performed

Fully read all 19 production files under `src/Cmdr.Core/Config` and all 13 test/helper files under `tests/Cmdr.Core.Tests/Config` (2,688 lines). Also fully read `SessionBaseConfigTests`, `SessionExpandedTests`, `SessionInputTests`, `SessionEditingTests`, `SessionTreeTests`, `SessionTreeEditingTests`, `LaunchSpecTests`, `EnvExpansionProcessTests`, and their session/process helpers. Traced the relevant load/save, source settings, launch preparation, log opening, error handling and recent-picker paths in production. UI configuration forms and environment text conversion were inspected for context; the entire UI/process subsystem was outside this review's scope.

The core source and core test project were copied outside the repository, excluding `bin`/`obj`. Executed:

```text
dotnet test tests/Cmdr.Core.Tests/Cmdr.Core.Tests.csproj --filter
'FullyQualifiedName~Cmdr.Core.Tests.Config|
 FullyQualifiedName~SessionBaseConfigTests|
 FullyQualifiedName~SessionExpandedTests|
 FullyQualifiedName~SessionInputTests|
 FullyQualifiedName~LaunchSpecTests|
 FullyQualifiedName~EnvExpansionProcessTests'
```

(The actual filter is one line, without spaces/newlines after `|`.) Result: **249 passed, 0 failed**, total test execution reported 2.171 seconds. This was a focused run, not the full suite assigned to the coordinator. Log: `/tmp/cmdr-review01-8wvwbJ/focused-tests.log`.

A second, nonoverlapping focused run used `--no-build --no-restore` and filter `FullyQualifiedName~SessionEditingTests|FullyQualifiedName~SessionTreeTests|FullyQualifiedName~SessionTreeEditingTests`. Result: **35 passed, 0 failed**, reported 1.2447 seconds. **Combined total: 284 passed, 0 failed**. Log: `/tmp/cmdr-review01-8wvwbJ/followup-tests.log`.

A standalone probe project referenced the isolated core project and exercised BOM configuration/history input, permission preservation, symbolic-link saves, top-level command shape loss, enum composites, sanitized log-name collisions with actual writes, NUL-containing base/log directories, unreadable files and parent directories, invalid recent entries, nested error coordinates, and custom environment dictionary comparison. Evidence: `/tmp/cmdr-review01-8wvwbJ/probes/Program.cs` and `/tmp/cmdr-review01-8wvwbJ/probes.log`. Follow-up probes in `/tmp/cmdr-review01-8wvwbJ/followup/Program.cs` and `followup-probes.log` verified noncanonical recent removal, all configured enum composite strings, file-versus-directory log conflicts, and the deep-tree save failure. They also checked that a replacement failure against an existing directory preserves its sentinel and removes temporary files, and that a serialization failure preserves an existing configuration byte-for-byte with no temporary file. Their generated data directories are `/tmp/cmdr-review01-followup-*`. Probes did not access real credentials or the user's recent store.

Priority missing regression coverage is specified per finding. Additional worthwhile checks are failed replacement preserving original bytes and cleaning temporary files, serializer failure leaving the destination untouched, noncanonical recent-path identity, the depth boundary described in 01-10, and Windows filesystem/path cases. Existing successful overwrite/cleanup tests do not establish failure-path or metadata preservation guarantees. The isolated failure probes below establish two particular failure paths only.

## Limitations

Host: Ubuntu 24.04 x64, ordinary non-root user, .NET SDK 10.0.107 and runtime 10.0.7. Linux process and filesystem probes executed locally. Windows behavior was static unless explicitly described as shared, platform-independent model logic; no Windows runtime was available. No web research was needed. No full-suite run, fault-injected power-loss test, hostile filesystem race test or Windows ACL test was performed. A final content-hash comparison found no changes to the copied core source or core test files during this review. Concurrent reviewers' files were not edited, and no production fixes or repository tests were added.
