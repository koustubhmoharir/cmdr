#!/usr/bin/env pwsh
# Publishes cmdr, logvu, cfgedit and mdedit as single framework-dependent executables into publish/<rid>/
# (Cmdr.exe, logvu.exe, cfgedit.exe and mdedit.exe on Windows, Cmdr, logvu, cfgedit and mdedit on Linux), then installs them for the current user.
#
#   ./publish.ps1             publish for the current platform only, then install
#   ./publish.ps1 -All        publish for win-x64 and linux-x64 (and the current platform), then install
#   ./publish.ps1 -NoInstall  publish without installing
#
# Install: on Windows, publish/<rid> is added to the user PATH (new terminals pick it up). On Linux,
# ~/.local/bin/cmdr, ~/.local/bin/logvu, ~/.local/bin/cfgedit and ~/.local/bin/mdedit become symbolic links to the published executables.
#
# Works from a fresh clone: it first fetches the Terminal.Gui submodule (a fork that both build from source)
# at the commit this repository records. Needs git and the .NET 10 SDK.
param(
    [switch]$All,
    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'

$arch = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
    'X64' { 'x64' }
    'Arm64' { 'arm64' }
    default { Write-Error "Unsupported architecture: $_"; exit 1 }
}

if ($IsWindows) {
    $currentRid = "win-$arch"
}
elseif ($IsLinux) {
    $currentRid = "linux-$arch"
}
else {
    Write-Error "Only Windows and Linux are supported."
    exit 1
}

$rids = if ($All) { @('win-x64', 'linux-x64', $currentRid) | Select-Object -Unique } else { @($currentRid) }

git -C "$PSScriptRoot" submodule update --init --recursive
if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not fetch the Terminal.Gui submodule (see the git output above)."
    exit $LASTEXITCODE
}

foreach ($rid in $rids) {
    foreach ($project in 'Cmdr', 'Logvu', 'Cfgedit', 'Mdedit') {
        dotnet publish "$PSScriptRoot/src/$project" -c Release -r $rid -o "$PSScriptRoot/publish/$rid"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

if ($NoInstall) {
    exit 0
}

$outputDir = [System.IO.Path]::GetFullPath("$PSScriptRoot/publish/$currentRid")

if ($IsWindows) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($userPath -split ';' | Where-Object { $_ })
    $present = $entries | Where-Object { $_.TrimEnd('\') -ieq $outputDir.TrimEnd('\') }
    if ($present) {
        Write-Host "$outputDir is already on the user PATH."
    }
    else {
        [Environment]::SetEnvironmentVariable('Path', (($entries + $outputDir) -join ';'), 'User')
        $env:Path = "$env:Path;$outputDir"
        Write-Host "Added $outputDir to the user PATH. Open a new terminal to use cmdr, logvu, cfgedit and mdedit."
    }
}
else {
    $binDir = Join-Path $HOME '.local/bin'
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null
    $links = [ordered]@{ cmdr = 'Cmdr'; logvu = 'logvu'; cfgedit = 'cfgedit'; mdedit = 'mdedit' }

    # Check every link path first, so a blocked one leaves nothing half installed.
    foreach ($name in $links.Keys) {
        $link = Join-Path $binDir $name
        $existing = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        if ($existing -and -not $existing.LinkType) {
            Write-Error "$link exists and is not a symbolic link; not replacing it. Remove it, or run with -NoInstall."
            exit 1
        }
    }

    foreach ($name in $links.Keys) {
        $link = Join-Path $binDir $name
        $target = Join-Path $outputDir $links[$name]
        if (Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue) {
            Remove-Item -LiteralPath $link -Force
        }

        New-Item -ItemType SymbolicLink -Path $link -Target $target | Out-Null
        Write-Host "$link -> $target"
    }

    $onPath = ($env:PATH -split ':') | Where-Object { $_.TrimEnd('/') -eq $binDir.TrimEnd('/') }
    if (-not $onPath) {
        Write-Warning "$binDir is not on PATH; add it to use cmdr, logvu, cfgedit and mdedit by name."
    }
}
