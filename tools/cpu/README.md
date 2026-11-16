# CPU measurement for streaming output

Measures the total CPU of cmdr and its terminal while cmdr shows streaming output.

Files: `stream.py` and `stream.ps1` (output generators), `cmdr-cpu.json` (Linux/macOS config),
`cmdr-cpu-windows.json` (Windows config), `measure.ps1` (CPU sampler).

## Build
- Before: `git worktree add ../cmdr-before 590fe85`, then in `../cmdr-before`:
  `dotnet publish src/Cmdr -c Release -r win-x64 -o <dir>`
- After: `./publish.ps1` in this repo, which gives `publish/win-x64/Cmdr.exe`.

## Run
1. Open the terminal under test at a fixed size (e.g. maximized, or 160 x 50). Keep the size and font the same for every run.
2. Close other busy tabs and programs.
3. Start cmdr with the config: `<build>\Cmdr.exe <path>\tools\cpu\cmdr-cpu-windows.json`
   (Linux: `cmdr-cpu.json`).
4. Select a command and press Enter. Leave the output pane following (do not scroll).
5. In another window, after about 5 s of output, run:
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\cpu\measure.ps1 -Label before`
   (defaults: 30 s window, 5 s warm-up). On Linux: `pwsh tools/cpu/measure.ps1 -Label before`.
6. Stop the command (Ctrl+C), then run the other build the same way with `-Label after`.
7. Cases: `stream 100/s`, `stream 10/s`, `progress`, and idle (cmdr open, no command running).
8. Repeat each case 2-3 times and compare the TOTAL lines; single runs can vary a lot.

## Reading the output
- The table lists each process: CPU seconds and % of one core. TOTAL is the sum; it also gives % of all logical CPUs.
- Default names on Windows: WindowsTerminal, OpenConsole, conhost, Cmdr, wsl, wslhost.
  Windows Terminal's renderer runs in the WindowsTerminal process, so it is included.
- Generators run as their own process (`powershell.exe`, `python3`) and are not counted by default.
- cmdr inside WSL runs in the WSL VM, which is not visible per process. Either run
  `measure.ps1 -WslCmdr` (adds a "Cmdr (in WSL)" row), or read the `vmmem`/`VmmemWSL` row.
- On Linux, run cmdr from the published build (`dotnet Cmdr.dll` shows as `dotnet`, not `Cmdr`).
- Use `-Names` to change the list, e.g. `-Names Cmdr,python3 -Seconds 10`.
- Processes that exit in the window count up to their last sample (0.5 s resolution).
