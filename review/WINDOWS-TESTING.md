# Windows test checklist

Everything here has passed on Linux. The Windows code paths have never run on Windows. Record results in
[TRACKER.md](TRACKER.md) (section 1 and "Validation gaps"), then remove the items that pass.

## Setup

- A Windows 10/11 machine (not WSL), .NET 10 SDK (`dotnet --version`), Windows Terminal, and PowerShell 7 (`pwsh`).
  Windows PowerShell 5.1 is built in.
- Node.js with npm, for the `.cmd` shim checks.
- `git clone` the repo, then `git checkout review-fixes`.

## 1. Automated tests (about 10 minutes)

```powershell
dotnet test tests/Cmdr.Core.Tests
dotnet test tests/Cmdr.Tests
```

Both must pass. Many tests return early on Linux and only do real work here: `WindowsJobContainmentTests`,
`WindowsProcessPlatformTests`, and the `File.Replace`, ACL and case-insensitive path tests in `Tests/Config`. A
failure in `WindowsJobContainmentTests` prints a diagnostic dump; send it back as-is. Also send any test that hangs,
and the `Passed`/`Failed` count lines.

## 2. Manual checks in the real app

Save this as `C:\cmdr-test\cmdr.json`, and create `C:\cmdr-test\hello.cmd` containing `@echo hello %1`:

```json
{
  "groups": [
    {
      "name": "win",
      "items": [
        { "name": "ping", "program": "ping", "args": ["-t", "127.0.0.1"] },
        { "name": "watch", "program": "dotnet", "args": ["watch", "--project", "C:/path/to/any/console/app"], "stdin": "console" },
        { "name": "batch", "program": "C:\\cmdr-test\\hello.cmd", "args": ["a&ver"] },
        { "name": "batch-no-comspec", "program": "C:\\cmdr-test\\hello.cmd", "args": ["x"], "env": { "ComSpec": null } },
        { "name": "batch-rel-comspec", "program": "C:\\cmdr-test\\hello.cmd", "args": ["x"], "env": { "ComSpec": "cmd.exe" } },
        { "name": "drive-relative", "program": "C:tools\\run" },
        { "name": "drive-rel-path", "program": "ping", "args": ["-n", "1", "127.0.0.1"], "env": { "PATH": "C:bin${pathsep}${PATH}" } },
        { "name": "missing", "program": "no-such-program" },
        { "name": "npm", "program": "npm", "args": ["--version"] },
        { "name": "batch-bang", "program": "C:\\cmdr-test\\hello.cmd", "args": ["!PATH!"] },
        { "name": "batch-newline", "program": "C:\\cmdr-test\\hello.cmd", "args": ["a\nb"] },
        { "name": "pct", "program": "cmd", "args": ["/c", "echo", "--%", "x"] },
        { "name": "log1", "program": "cmd", "args": ["/c", "echo 1"], "log": { "enabled": true, "path": "Out.log" } },
        { "name": "log2", "program": "cmd", "args": ["/c", "echo 2"], "log": { "enabled": true, "path": "out.log" } }
      ]
    }
  ]
}
```

Run `dotnet run --project src/Cmdr -- C:\cmdr-test\cmdr.json` and check:

| Check | Expected |
|---|---|
| Start `ping`, then stop it | It stops cleanly, and no `ping.exe` is left in Task Manager. Start it again and quit cmdr: no `ping.exe` is left either. |
| `watch` (console stdin) | `dotnet watch` runs and its keyboard prompts work. |
| `batch` | Prints `hello` and `a&ver` (possibly in quotes). The Windows version banner from `ver` must **not** appear. |
| `batch-no-comspec`, `batch-rel-comspec` | Both print `hello x` (cmdr runs `C:\Windows\System32\cmd.exe` for them). |
| `drive-relative` | Fails to start, with a message saying `C:tools\run` is relative to drive C:'s current directory. |
| `drive-rel-path` | Runs normally: the `C:bin` PATH entry is skipped. |
| `missing` | Fails to start, with a "not found" message. |
| `npm` | Prints npm's version (npm is an `npm.cmd` shim). |
| Loading the file | cmdr reports a log path conflict between `log1` and `log2` (`Out.log` and `out.log` are one file on Windows). Remove `log2` to continue. |
| F5 on `ping` twice, quickly | The second press shows "Restart already pending". Force kill now restarts it. |
| `batch-bang` | Prints `hello` and `!PATH!` literally (possibly in quotes), even with delayed expansion enabled in the registry (`HKCU\Software\Microsoft\Command Processor\DelayedExpansion` = 1). |
| `batch-newline` | Fails to start: "argument 1 contains a line break, which cmd.exe cannot pass to a batch file". |

## 3. Copying a command line into PowerShell (06-05)

In the list, select `batch` and press `c`, then paste into **pwsh** and into **Windows PowerShell 5.1**. Each must print
exactly what cmdr printed. Repeat with the argument changed (edit it in the UI) to each of `x|y`, `%TEMP%`, `"q"`,
and an empty argument. Do the same for an npm script shim (`node_modules\.bin\something.cmd`). A plain command such as
`pnpm run dev --port=3000` should be copied in the plain form, without `$env:CMDR_CMD`.

Also:
- In PowerShell run `$env:CMDR_CMD = 'mine'`, then paste the copied `batch` command. Afterwards `$env:CMDR_CMD` must still be
  `mine`, and `$LASTEXITCODE` the batch file's exit code. Without the variable set, it must still be unset afterwards.
- Copy `pct` and paste it: it must print `--% x`.

## 4. Saving and logs

- Edit a command in the UI and save. Then check the file's ACL is unchanged: run `icacls C:\cmdr-test\cmdr.json`
  before and after. This tests the `File.Replace` path.
- Make the file read-only for deletes but writable (`icacls cmdr.json /deny "%USERNAME%":(DE)`), and save again. The
  save should succeed by writing in place.
- Save the config through a symbolic link (`mklink link.json cmdr.json`, needs Developer Mode or an admin prompt) and
  open `link.json`. The save must update `cmdr.json` and leave the link in place.
- Open the config through a UNC path (`\\localhost\C$\cmdr-test\cmdr.json`) and start `ping`: it runs. Start `batch`: it
  fails to start with "… is a UNC path, which cmd.exe cannot use as the working directory of a batch file …" (the working
  folder defaults to the config's folder, which is on the share).
- Hard-linked logs: `fsutil hardlink create C:\cmdr-test\logs\h2.log C:\cmdr-test\logs\h1.log` (create `h1.log` first), then
  give two commands `"log": { "path": "h1.log" }` and `"h2.log"`. Loading reports a log path conflict between them.
