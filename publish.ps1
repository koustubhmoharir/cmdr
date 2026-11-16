#!/usr/bin/env pwsh
# Publishes cmdr as a single framework-dependent executable for Windows and Linux:
#   publish/win-x64/Cmdr.exe and publish/linux-x64/Cmdr
# Works from a fresh clone: it first fetches the Terminal.Gui submodule (a fork that cmdr builds from source)
# at the commit this repository records. Needs git and the .NET 10 SDK.
$ErrorActionPreference = 'Stop'

git -C "$PSScriptRoot" submodule update --init --recursive
if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not fetch the Terminal.Gui submodule (see the git output above)."
    exit $LASTEXITCODE
}

foreach ($rid in 'win-x64', 'linux-x64') {
    dotnet publish "$PSScriptRoot/src/Cmdr" -c Release -r $rid -o "$PSScriptRoot/publish/$rid"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
