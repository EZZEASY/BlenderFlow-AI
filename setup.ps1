# BlenderFlow AI — one-shot installer (Windows).
# Builds the C# plugin and links the Blender addon into their respective
# app-data directories. Python deps are installed on demand by the addon
# the first time it starts inside Blender.
#
# Windows is NOT the main test platform — if something misbehaves, please
# open an issue with the full error output. Run from an elevated PowerShell
# or with Developer Mode enabled so the symlink step works without admin.

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AddonName = 'blenderflow_addon'

Write-Host '========================================='
Write-Host '  BlenderFlow AI installer (Windows)'
Write-Host '========================================='
Write-Host ''

# ─── Build the C# plugin ───
Write-Host '> Building BlenderFlow plugin...'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'X .NET SDK not found. Install .NET 8 from https://dotnet.microsoft.com/' -ForegroundColor Red
    exit 1
}

Push-Location (Join-Path $ScriptDir 'BlenderFlowPlugin')
try {
    dotnet build --configuration Debug --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
Write-Host '  Plugin built' -ForegroundColor Green

# ─── Register with Logi Plugin Service ───
Write-Host ''
Write-Host '> Registering plugin with Logi Plugin Service...'
$PluginDir = Join-Path $env:LOCALAPPDATA 'Logi\LogiPluginService\Plugins'
New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
$LinkTarget = Join-Path $ScriptDir 'BlenderFlowPlugin\bin\Debug\'
Set-Content -Path (Join-Path $PluginDir 'BlenderFlowPlugin.link') -Value $LinkTarget -NoNewline
Write-Host '  Plugin registered' -ForegroundColor Green

# ─── Link the Blender addon ───
Write-Host ''
Write-Host '> Installing Blender addon...'

$BlenderRoot = Join-Path $env:APPDATA 'Blender Foundation\Blender'
if (-not (Test-Path $BlenderRoot)) {
    Write-Host "X Blender user-data directory not found at $BlenderRoot" -ForegroundColor Red
    Write-Host '  Install Blender from https://www.blender.org/ and run it once.' -ForegroundColor Red
    exit 1
}

# Pick the highest-numbered version directory (e.g. "4.2", "5.0").
$BlenderVersion = Get-ChildItem $BlenderRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+' } |
    Sort-Object Name -Descending |
    Select-Object -First 1

if (-not $BlenderVersion) {
    Write-Host "X No Blender version directory found under $BlenderRoot" -ForegroundColor Red
    exit 1
}

$AddonParent = Join-Path $BlenderVersion.FullName 'scripts\addons'
$AddonDst = Join-Path $AddonParent $AddonName
$AddonSrc = Join-Path $ScriptDir $AddonName

New-Item -ItemType Directory -Force -Path $AddonParent | Out-Null
if (Test-Path $AddonDst) {
    Remove-Item -Recurse -Force $AddonDst
}

# Symlink requires Developer Mode (Windows 10+) or an elevated shell.
try {
    New-Item -ItemType SymbolicLink -Path $AddonDst -Target $AddonSrc | Out-Null
    Write-Host '  Addon symlinked (dev mode — reload the addon in Blender to pick up edits)' -ForegroundColor Green
}
catch {
    Write-Host 'X Symlink failed — enable Developer Mode (Settings > Privacy & Security > For developers)' -ForegroundColor Red
    Write-Host '  or rerun this script from an elevated PowerShell.' -ForegroundColor Red
    Write-Host "  Source: $AddonSrc"
    Write-Host "  Target: $AddonDst"
    exit 1
}

# ─── Reload Plugin Service ───
Write-Host ''
Write-Host '> Reloading Logi Plugin Service...'
try {
    Start-Process 'loupedeck:plugin/BlenderFlow/reload' -ErrorAction SilentlyContinue
    Write-Host '  Reload signal sent' -ForegroundColor Green
}
catch {
    Write-Host '  Reload signal could not be sent — restart Logi Options+ manually' -ForegroundColor Yellow
}

# ─── Done ───
Write-Host ''
Write-Host '========================================='
Write-Host '  Install complete' -ForegroundColor Green
Write-Host '========================================='
Write-Host ''
Write-Host 'Next steps:'
Write-Host '  1. Open Blender -> Edit -> Preferences -> Add-ons'
Write-Host "     Search 'BlenderFlow' -> enable the checkbox"
Write-Host '     (first enable installs Python deps; give it a few seconds)'
Write-Host ''
Write-Host '  2. Open Logi Options+ -> device customization -> All Actions'
Write-Host '     Find BlenderFlow AI -> drag actions onto keys'
Write-Host ''
Write-Host '  3. For AI 3D generation: configure a provider in the addon''s'
Write-Host '     preferences (BlenderFlow ships with Hyper3D Rodin enabled by'
Write-Host '     default, using a shared free-trial key so it works out of the'
Write-Host '     box).'
Write-Host ''
