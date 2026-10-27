# An update from one version to the next, as users get it: publishes
# UpdateApp 1.0.0 and 2.0.0 with NativeAOT, packs both with Velopack's vpk,
# installs 1.0.0 with its setup, runs it, and checks it updates itself to
# 2.0.0 through Oriel.Updates. Then uninstalls it.
#
#   pwsh tests/updates/run.ps1
#
# It installs OrielUpdateTest for the current user for a minute
# (%LOCALAPPDATA%\OrielUpdateTest), and removes it.
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) "oriel-updates-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$installed = Join-Path $env:LOCALAPPDATA 'OrielUpdateTest'
$report = Join-Path $work 'report.txt'
New-Item -ItemType Directory $work | Out-Null

function Invoke([string] $file, [string[]] $arguments) {
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { throw "$file failed with $LASTEXITCODE" }
}

try {
    Invoke dotnet @('tool', 'install', 'vpk', '--version', '1.2.161', '--tool-path', "$work/tools")
    foreach ($version in '1.0.0', '2.0.0') {
        Invoke dotnet @('publish', "$PSScriptRoot/UpdateApp", '-c', 'Release', '-r', 'win-x64', "-p:Version=$version", '-o', "$work/publish-$version")
        Invoke "$work/tools/vpk" @('pack', '--packId', 'OrielUpdateTest', '--packVersion', $version, '--packDir', "$work/publish-$version", '--mainExe', 'UpdateApp.exe', '--outputDir', "$work/releases")
        # Each pack makes the setup of its version: keep the first one's.
        if ($version -eq '1.0.0') { Copy-Item "$work/releases/OrielUpdateTest-win-Setup.exe" "$work/setup-1.0.0.exe" }
    }

    Start-Process "$work/setup-1.0.0.exe" -ArgumentList '--silent' -Wait
    if (-not (Test-Path "$installed/current/UpdateApp.exe")) { throw "the setup installed nothing in $installed" }

    $env:ORIEL_UPDATE_SOURCE = "$work/releases"
    $env:ORIEL_UPDATE_REPORT = $report
    Start-Process "$installed/current/UpdateApp.exe" -Wait
    # The update restarts the app, which reports again.
    $deadline = (Get-Date).AddSeconds(90)
    while (-not ((Test-Path $report) -and (Get-Content $report -Raw) -match 'running 2\.0\.0') -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    $lines = if (Test-Path $report) { Get-Content $report } else { @() }
    $lines | ForEach-Object { Write-Host "  $_" }
    $expected = @('running 1.0.0 installed=True', 'found 2.0.0', 'downloaded', 'running 2.0.0 installed=True', 'found none')
    if (Compare-Object $expected $lines) { throw "the update did not go as expected" }
    Write-Host 'Updated from 1.0.0 to 2.0.0.'
}
finally {
    Remove-Item Env:ORIEL_UPDATE_SOURCE, Env:ORIEL_UPDATE_REPORT -ErrorAction SilentlyContinue
    if (Test-Path "$installed/Update.exe") { Start-Process "$installed/Update.exe" -ArgumentList '--uninstall', '--silent' -Wait }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
