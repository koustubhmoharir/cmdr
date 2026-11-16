# Measures the CPU time of processes by name over a window and prints a table.
# Works on Windows PowerShell 5.1 and pwsh 7 (Windows and Linux). See README.md.
#
#   .\measure.ps1 -Label before
#   pwsh tools/cpu/measure.ps1 -Seconds 5 -Warmup 1 -Names Cmdr,python3 -Label test
#   .\measure.ps1 -Label before -WslCmdr      (cmdr running inside WSL)
[CmdletBinding()]
param(
    [int]$Seconds = 30,
    [int]$Warmup = 5,
    [string[]]$Names,
    [string]$Label = '',
    [switch]$WslCmdr
)

$ErrorActionPreference = 'Stop'
$POLL_MS = 500

$onWindows = ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT)

if (-not $Names -or $Names.Count -eq 0) {
    if ($onWindows) {
        $Names = @('WindowsTerminal', 'OpenConsole', 'conhost', 'Cmdr', 'wsl', 'wslhost')
    } else {
        $Names = @('Cmdr', 'gnome-terminal-server', 'konsole', 'kitty', 'alacritty', 'wezterm-gui', 'foot', 'xterm', 'tmux')
    }
}
# Accept "a,b" as well as "a", "b" (for callers that pass one string, e.g. cmd.exe).
$Names = @($Names | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if ($WslCmdr -and $onWindows -eq $false) {
    Write-Warning '-WslCmdr is for running this script on Windows. Ignored here.'
    $WslCmdr = $false
}

# Reads the current CPU time of every matching process, keyed by PID. On Linux, PowerShell sometimes
# returns an empty TotalProcessorTime or StartTime for a live process, so a read is retried a few
# times and a process whose CPU time stays unreadable is kept with Cpu = $null (not counted as exited).
# The key is the PID alone: StartTime is not reliable enough to build a key from.
function Get-Snapshot {
    $snap = @{}
    foreach ($n in $Names) {
        foreach ($p in @(Get-Process -Name $n -ErrorAction SilentlyContinue)) {
            $cpu = $null
            for ($try = 0; $try -lt 5 -and $cpu -eq $null; $try++) {
                try {
                    $t = $p.TotalProcessorTime
                    if ($t -ne $null) { $cpu = $t.TotalSeconds }
                } catch {
                    # Exited between listing and reading.
                }
                if ($cpu -eq $null) { [System.Threading.Thread]::Sleep(20) }
            }
            $snap[[string]$p.Id] = @{ Name = $p.ProcessName; Pid = $p.Id; Cpu = $cpu }
        }
    }
    return $snap
}

# Runs one shell line inside the default WSL distro and returns cumulative utime+stime ticks of
# every process whose name is Cmdr, and the clock tick rate. Plain words only: no double quotes.
function Get-WslTicks {
    $sh = 't=0; for d in /proc/[0-9]*; do c=$(cat $d/comm 2>/dev/null); case $c in Cmdr) set -- $(cat $d/stat 2>/dev/null); t=$((t+${14}+${15}));; esac; done; echo $t $(getconf CLK_TCK)'
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = (& wsl.exe sh -c $sh 2>$null) -join ' '
    } catch {
        $raw = ''
    }
    $ErrorActionPreference = $prevEA
    $raw = ($raw -replace "`0", '').Trim()
    $parts = @($raw -split '\s+' | Where-Object { $_ -match '^\d+$' })
    if ($parts.Count -lt 2) {
        Write-Warning ('WSL read failed (output: "{0}"). Is WSL installed and is cmdr running in it?' -f $raw)
        return $null
    }
    return @{ Ticks = [double]$parts[0]; Hz = [double]$parts[1] }
}

$clock = [System.Diagnostics.Stopwatch]::StartNew()
$cores = [Environment]::ProcessorCount

if ($Warmup -gt 0) { [System.Threading.Thread]::Sleep($Warmup * 1000) }

$wslStart = $null
if ($WslCmdr) { $wslStart = Get-WslTicks }

# One record per process seen in the window. Cpu0 is its CPU time at the window start (0 when it
# started inside the window); CpuLast is the last reading. An exited process keeps its last reading,
# so CPU it used after the last poll (at most $POLL_MS ago) is not counted.
$rec = @{}
$first = Get-Snapshot
foreach ($k in $first.Keys) {
    $c = $first[$k].Cpu
    if ($c -eq $null) { $c = 0.0 }
    $rec[$k] = @{ Name = $first[$k].Name; Pid = $first[$k].Pid; Cpu0 = $c; CpuLast = $c; New = $false; Gone = $false }
}
$t0 = $clock.Elapsed.TotalSeconds
$windowEnd = $t0 + $Seconds

function Update-Records {
    $snap = Get-Snapshot
    foreach ($k in $snap.Keys) {
        $c = $snap[$k].Cpu
        if ($rec.ContainsKey($k)) {
            if ($c -ne $null) { $rec[$k].CpuLast = $c }
            $rec[$k].Gone = $false
        } else {
            # Started inside the window: all of its CPU time so far counts.
            if ($c -eq $null) { $c = 0.0 }
            $rec[$k] = @{ Name = $snap[$k].Name; Pid = $snap[$k].Pid; Cpu0 = 0.0; CpuLast = $c; New = $true; Gone = $false }
        }
    }
    foreach ($k in $rec.Keys) {
        if (-not $snap.ContainsKey($k)) { $rec[$k].Gone = $true }
    }
}

while ($clock.Elapsed.TotalSeconds -lt $windowEnd) {
    $left = $windowEnd - $clock.Elapsed.TotalSeconds
    $sleep = [Math]::Min($POLL_MS / 1000.0, $left)
    if ($sleep -gt 0) { [System.Threading.Thread]::Sleep([int]($sleep * 1000)) }
    Update-Records
}
Update-Records
$elapsed = $clock.Elapsed.TotalSeconds - $t0

$wslEnd = $null
if ($WslCmdr) { $wslEnd = Get-WslTicks }

$rows = @()
$total = 0.0
foreach ($k in $rec.Keys) {
    $r = $rec[$k]
    $cpu = $r.CpuLast - $r.Cpu0
    if ($cpu -lt 0) { $cpu = 0.0 }
    $total += $cpu
    $status = 'running'
    if ($r.Gone) { $status = 'exited' } elseif ($r.New) { $status = 'started' }
    $rows += [pscustomobject]@{
        Name     = $r.Name
        PID      = $r.Pid
        Status   = $status
        CpuSec   = [Math]::Round($cpu, 2)
        PctCore  = [Math]::Round(100.0 * $cpu / $elapsed, 1)
    }
}

if ($WslCmdr) {
    if ($wslStart -and $wslEnd) {
        $wslCpu = ($wslEnd.Ticks - $wslStart.Ticks) / $wslEnd.Hz
        $total += $wslCpu
        $rows += [pscustomobject]@{
            Name = 'Cmdr (in WSL)'; PID = '-'; Status = 'wsl'
            CpuSec = [Math]::Round($wslCpu, 2); PctCore = [Math]::Round(100.0 * $wslCpu / $elapsed, 1)
        }
    }
}

$rows = @($rows | Sort-Object Name, PID)

Write-Output ''
Write-Output ('CPU measurement: label="{0}"  window={1:0.0}s  warmup={2}s  logical CPUs={3}  {4}' -f $Label, $elapsed, $Warmup, $cores, (Get-Date -Format 's'))
Write-Output ('Names: ' + ($Names -join ', '))
if ($rows.Count -eq 0) {
    Write-Output 'No matching processes were running.'
} else {
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
}
Write-Output ('TOTAL  {0,8:0.00} CPU s  {1,7:0.0} % of one core  {2,6:0.0} % of all {3} logical CPUs' -f $total, (100.0 * $total / $elapsed), (100.0 * $total / ($elapsed * $cores)), $cores)
Write-Output 'Processes that exited in the window are counted up to their last poll (0.5 s resolution).'
