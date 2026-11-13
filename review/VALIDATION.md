# Validation evidence

## Baseline suite

Environment: Ubuntu 24.04 x64, .NET SDK 10.0.107, .NET runtime 10.0.7.

Command, run from repository root:

```sh
dotnet test Cmdr.slnx --no-restore --logger 'console;verbosity=minimal'
```

Result: exit code 0; **1,190 reported passes**, zero failures, zero reported skips.

| Assembly | Reported passes | Test duration |
|---|---:|---:|
| Cmdr.Core.Tests | 618 | 11 seconds |
| Cmdr.Tests | 572 | 2 seconds |

The test command also built both production projects and both test projects in Debug. Existing restored dependencies were used. No code changes were needed to run the suite.

## Interpretation and limits

- Passing the suite does not disprove findings involving scenarios the suite does not cover. Review-specific evidence and probes are documented in the area reports.
- Eight Windows integration tests in `tests/Cmdr.Core.Tests/Processes/WindowsProcessPlatformTests.cs` return immediately when the host is not Windows (lines 18, 46, 74, 97, 124, 161, 176, 196). Their reported passes are not Windows runtime validation. Other platform guards may similarly limit coverage.
- Windows native launch, Job Objects, Ctrl+C delivery, and real console input need a Windows runner. Windows findings here are static unless explicitly stated otherwise.
- No interactive terminal acceptance session, sustained whole-application memory benchmark, or published single-file smoke test was performed by the coordinator. Focused probes, where performed by reviewers, are identified in their reports.
- No dependency vulnerability audit was requested or performed. Package freshness is not a review conclusion.

## Repository-level quality observations

The baseline has 184 tracked C# files across production and tests. The test suite is substantial and the Core/UI split permits many behaviors to be exercised without a terminal. Nullable reference checking is enabled in both production projects.

No tracked CI workflow, SDK `global.json`, or NuGet dependency lock file was found. These are delivery/reproducibility recommendations, not demonstrated runtime defects: add Linux and Windows CI, make platform-excluded tests visibly skipped, document/pin the supported SDK policy, and smoke-test the published executables. Prioritize Windows runtime coverage because static inspection cannot validate the native lifetime and console contracts.

## Review-specific verification

| Area | Existing focused tests | Additional evidence |
|---|---:|---|
| Configuration | 284 passed | Disposable Unix permission/link/path, BOM, JSON-shape/depth, diagnostic and log-collision probes |
| Process lifecycle | 5 passed | Real Linux stale-output/disposal/stdin races; subprocess fault injection for count-query exceptions; gated log sink |
| Platform processes | 48 passed | Pure Windows resolver/plan probes; real Linux argv, stdin, environment, group/signal probes |
| Output/terminal | 336 passed | Source-linked parser, Unicode, viewport/selection and log-failure probes; Release current-thread allocation measurements |
| Session orchestration | 6 passed | Real Linux restart/shutdown, cancellation, queued-group conflict, API ownership/order; bounded concurrent tree-edit probe |
| UI/application | 418 passed | Actual Terminal.Gui buffer-driver dialogs/controller/picker/quit; CLI subprocesses; Windows formatter output on Linux |

These focused-test totals overlap the baseline suite. Do not sum them to claim a larger number of distinct tests. The output/terminal report records its own focused tests and allocation measurements. Detailed evidence, commands, assumptions and limitations are in each report. Session report 05 also preserves a complete runnable probe harness in the document; other temporary harness paths are local review evidence, not durable repository artifacts.

The coordinator read every area report, checked key reported paths/control flow (shutdown/restart, force-kill/log ordering, exit callbacks, pump publication/cleanup, atomic save, log-path mapping, lossy argument conversion, hint caching and Windows start-before-attach), and reconciled shared findings. The coordinator did not independently rerun every reviewer probe. In particular, Windows-native claims retain their static-verification qualification.
