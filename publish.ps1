#!/usr/bin/env pwsh
# Publishes cmdr and logvu as single framework-dependent executables for Windows and Linux:
#   publish/win-x64/Cmdr.exe, publish/win-x64/logvu.exe, publish/linux-x64/Cmdr and publish/linux-x64/logvu
# Works from a fresh clone: it first fetches the Terminal.Gui submodule (a fork that both build from source)
# at the commit this repository records. Needs git and the .NET 10 SDK.
$ErrorActionPreference = 'Stop'

git -C "$PSScriptRoot" submodule update --init --recursive
if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not fetch the Terminal.Gui submodule (see the git output above)."
    exit $LASTEXITCODE
}

foreach ($rid in 'win-x64', 'linux-x64') {
    foreach ($project in 'Cmdr', 'Logvu') {
        dotnet publish "$PSScriptRoot/src/$project" -c Release -r $rid -o "$PSScriptRoot/publish/$rid"
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
