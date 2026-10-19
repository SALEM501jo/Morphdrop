<#
.SYNOPSIS
    Builds a ShiftDrop release: tests, a self-contained publish, and a Velopack installer.

.DESCRIPTION
    Output goes to artifacts\releases: ShiftDropApp-win-Setup.exe (the installer), a portable zip, and the
    update packages that Velopack serves from GitHub Releases. Users need neither .NET nor admin rights.

.EXAMPLE
    ./scripts/release.ps1                 # version from src/ShiftDrop/ShiftDrop.csproj
    ./scripts/release.ps1 -Version 1.0.1
#>
param(
    [string]$Version,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $Version = ([xml](Get-Content src/ShiftDrop/ShiftDrop.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
Write-Host "Building ShiftDrop $Version" -ForegroundColor Cyan

$publish = Join-Path $root 'artifacts/publish'
$releases = Join-Path $root 'artifacts/releases'
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

if (-not $SkipTests) {
    dotnet test -c Release --nologo
    if ($LASTEXITCODE) { throw 'Tests failed.' }
}

# Self-contained, so users don't need to install .NET. Only the x64 native libraries are kept.
dotnet publish src/ShiftDrop/ShiftDrop.csproj -c Release -r win-x64 --self-contained `
    -p:Version=$Version -o $publish --nologo
if ($LASTEXITCODE) { throw 'Publish failed.' }

dotnet tool restore
# Velopack builds its packages in the temp folder; keep that next to the output rather than on drive C:.
$temp = Join-Path $root 'artifacts/tmp'
New-Item -ItemType Directory -Force $temp | Out-Null
$env:TEMP = $temp
$env:TMP = $temp
# The pack id is also the install folder (%LOCALAPPDATA%\ShiftDropApp), which Velopack deletes on uninstall.
# It must differ from %LOCALAPPDATA%\ShiftDrop, where settings, the log and the downloaded FFmpeg live.
dotnet vpk pack `
    --packId ShiftDropApp `
    --packVersion $Version `
    --packTitle ShiftDrop `
    --packAuthors Salem `
    --packDir $publish `
    --mainExe ShiftDrop.exe `
    --icon src/ShiftDrop/Assets/ShiftDrop.ico `
    --outputDir $releases
if ($LASTEXITCODE) { throw 'Packing failed.' }

Get-ChildItem $releases | Format-Table Name, @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } }
