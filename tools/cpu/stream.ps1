# Streaming output for the CPU measurement kit (see README.md). Same modes as stream.py.
# Runs on Windows PowerShell 5.1 and pwsh 7.
#
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File stream.ps1 [rate]
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -File stream.ps1 -Progress
#
# -Rate      lines per second in line mode (default 100)
# -Progress  progress bar redrawn in place about 20 times a second, newline about every 5 s (-Rate ignored)
# -Seed      random seed (default 1); the same seed gives the same text (not the same text as stream.py)
param(
    [double]$Rate = 100,
    [switch]$Progress,
    [int]$Seed = 1
)

$ErrorActionPreference = 'Stop'

$ESC = [string][char]27
$ALPHA = 'abcdefghijklmnopqrstuvwxyz'
$COLORS = @(($ESC + '[31m'), ($ESC + '[32m'), ($ESC + '[33m'), ($ESC + '[34m'), ($ESC + '[35m'), ($ESC + '[36m'))
$RESET = $ESC + '[0m'
$BAR_WIDTH = 20
$PROGRESS_PERIOD = 0.05
$NEWLINE_EVERY = 5.0
$TOTAL_BYTES = [double]1073741824

$rng = New-Object System.Random $Seed
$out = [Console]::Out

function Get-Word {
    $n = $script:rng.Next(1, 13)
    $s = ''
    for ($k = 0; $k -lt $n; $k++) {
        $s += $script:ALPHA.Substring($script:rng.Next(0, 26), 1)
    }
    return $s
}

function Get-Line([int]$i) {
    $target = $script:rng.Next(20, 151)
    $colored = $script:rng.NextDouble() -lt 0.30
    $nCol = 0
    if ($colored) { $nCol = $script:rng.Next(1, 4) }
    $words = New-Object System.Collections.ArrayList
    $length = ('{0:D8} ' -f $i).Length
    while ($length -lt $target) {
        $w = Get-Word
        [void]$words.Add($w)
        $length += $w.Length + 1
    }
    # mark nCol distinct random word indices to be coloured
    $idx = @{}
    $nCol = [Math]::Min($nCol, $words.Count)
    while ($idx.Count -lt $nCol) {
        $idx[$script:rng.Next(0, $words.Count)] = $true
    }
    $parts = New-Object System.Collections.ArrayList
    for ($k = 0; $k -lt $words.Count; $k++) {
        if ($idx.ContainsKey($k)) {
            [void]$parts.Add($script:COLORS[$script:rng.Next(0, $script:COLORS.Count)] + $words[$k] + $script:RESET)
        } else {
            [void]$parts.Add($words[$k])
        }
    }
    return ('{0:D8} ' -f $i) + ($parts -join ' ')
}

# Sleeps for the given number of seconds (no-op when not positive).
function Wait-Seconds([double]$s) {
    $ms = [int][Math]::Floor($s * 1000)
    if ($ms -gt 0) { [System.Threading.Thread]::Sleep($ms) }
}

$clock = [System.Diagnostics.Stopwatch]::StartNew()

if (-not $Progress) {
    if ($Rate -le 0) { throw 'Rate must be greater than 0.' }
    $period = 1.0 / $Rate
    $nextT = $clock.Elapsed.TotalSeconds
    $i = 0
    while ($true) {
        $out.Write((Get-Line $i) + "`n")
        $out.Flush()
        $i++
        $nextT += $period
        $d = $nextT - $clock.Elapsed.TotalSeconds
        if ($d -gt 0) { Wait-Seconds $d }
        elseif ($d -lt -1.0) { $nextT = $clock.Elapsed.TotalSeconds }
    }
} else {
    $done = 0.0
    $last = $clock.Elapsed.TotalSeconds
    $nlAt = $last
    $nextT = $last
    while ($true) {
        $now = $clock.Elapsed.TotalSeconds
        $dt = $now - $last
        $last = $now
        $speed = 12.0 + 3.0 * [Math]::Sin($now / 2.0)  # MB/s, varies smoothly
        $done += $speed * 1000000.0 * $dt
        if ($done -ge $TOTAL_BYTES) { $done = 0.0 }
        $pct = [int][Math]::Floor(100.0 * $done / $TOTAL_BYTES)
        $filled = [int][Math]::Floor($pct * $BAR_WIDTH / 100.0)
        $bar = '#' * $filled + ' ' * ($BAR_WIDTH - $filled)
        $text = '[' + $bar + '] ' + ('{0,3}%' -f $pct) + '  ' + ('{0,4:0.0}' -f $speed) + ' MB/s'
        if ($now - $nlAt -ge $NEWLINE_EVERY) {
            $out.Write("`r" + $text + "`n")
            $nlAt = $now
        } else {
            $out.Write("`r" + $text)
        }
        $out.Flush()
        $nextT += $PROGRESS_PERIOD
        $d = $nextT - $clock.Elapsed.TotalSeconds
        if ($d -gt 0) { Wait-Seconds $d }
        elseif ($d -lt -1.0) { $nextT = $clock.Elapsed.TotalSeconds }
    }
}
