#!/usr/bin/env pwsh
# Publishes cmdr as a single framework-dependent executable for Windows and Linux:
#   publish/win-x64/Cmdr.exe and publish/linux-x64/Cmdr
$ErrorActionPreference = 'Stop'

foreach ($rid in 'win-x64', 'linux-x64') {
    dotnet publish "$PSScriptRoot/src/Cmdr" -c Release -r $rid -o "$PSScriptRoot/publish/$rid"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
