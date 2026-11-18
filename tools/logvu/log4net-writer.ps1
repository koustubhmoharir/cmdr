#!/usr/bin/env pwsh
# Writes log4net-core style events into a directory the way log4net's RollingFileAppender does with rollingStyle Size and
# maxSizeRollBackups: the active file is kept open for writing with read sharing only, and is closed before a numbered
# rotation (delete the oldest backup, rename N -> N+1, rename the active file to 1, create a new active file).
# Use it to exercise logvu on Windows while it tails the directory.
#
#   ./log4net-writer.ps1 -Directory C:\temp\Logs [-IntervalMs 200] [-RotateEvery 40] [-Backups 5] [-Count 0]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [int] $IntervalMs = 200,
    [int] $RotateEvery = 40,
    [int] $Backups = 5,
    [int] $Count = 0   # 0 = until Ctrl+C
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Directory | Out-Null
$active = Join-Path $Directory 'SheetKraft.log'
function Backup([int] $i) { Join-Path $Directory "SheetKraft$i.log" }

$loggers = 'SheetKraft.Engine.Runner', 'SheetKraft.Data.SqlStore', 'SheetKraft.Web.Api.OrdersController'
$levels = 'DEBUG', 'INFO', 'INFO', 'WARN', 'ERROR'
$trace = @"
System.InvalidOperationException: Sheet not found
   at SheetKraft.Engine.Runner.Execute(Int32 id) in C:\src\Engine\Runner.cs:line 88
   at System.Threading.Tasks.Task.InnerInvoke()
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext)
"@

function Open-Active {
    # log4net's default locking model: the writer holds the file, sharing it for reading only.
    $stream = [System.IO.File]::Open($active, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
}

function Rotate {
    $oldest = Backup $Backups
    if (Test-Path $oldest) { Remove-Item $oldest }
    for ($i = $Backups - 1; $i -ge 1; $i--) {
        if (Test-Path (Backup $i)) { Move-Item (Backup $i) (Backup ($i + 1)) }
    }
    Move-Item $active (Backup 1)
}

$writer = Open-Active
$n = 0
try {
    while ($Count -eq 0 -or $n -lt $Count) {
        $n++
        $level = Get-Random -InputObject $levels
        $now = Get-Date
        $line = '{0,-5} {1:yyyy-MM-dd HH:mm:ss,fff} [{2}] {3} - Step {4}: processed {5} rows' -f $level, $now, (Get-Random -Maximum 40), (Get-Random -InputObject $loggers), $n, (Get-Random -Maximum 999)
        $writer.Write($line + "`r`n")
        if ($level -eq 'ERROR') { $writer.Write(($trace -replace "`r?`n", "`r`n")) }
        $writer.Flush()
        if ($n % $RotateEvery -eq 0) {
            $writer.Dispose()
            Rotate
            $writer = Open-Active
            Write-Host "rotated after step $n"
        }
        Start-Sleep -Milliseconds $IntervalMs
    }
}
finally {
    $writer.Dispose()
}
