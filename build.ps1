# DreamEchoMod build & deploy for BepInEx-Manager isolated profile.
# Usage: powershell -File build.ps1 [-GameDir <game dir>]
# Resolves the profile BepInEx root from doorstop_config.ini (target_assembly),
# builds with -p:BepDir and deploys the dll into <profile>/BepInEx/plugins/.
param(
    [string]$GameDir = 'E:\steam\steamapps\common\DreamEcho'
)
$ErrorActionPreference = 'Stop'
$Proj = $PSScriptRoot
$Runtime = 'net6.0'

# 1) Locate profile BepInEx root from doorstop target_assembly
$doorstop = Join-Path $GameDir 'doorstop_config.ini'
if (-not (Test-Path $doorstop)) { Write-Host "doorstop_config.ini not found: $doorstop"; exit 1 }
$m = Select-String -Path $doorstop -Pattern '^\s*target_assembly\s*=\s*(.+)$' | Select-Object -First 1
if (-not $m) { Write-Host 'target_assembly not found in doorstop_config.ini'; exit 1 }
$target = $m.Matches[0].Groups[1].Value.Trim()
# target = <profile>/BepInEx/core/BepInEx.Unity.IL2CPP.dll  ->  BepInEx root is two levels up
$bepDir = Split-Path (Split-Path $target -Parent) -Parent
if (-not (Test-Path (Join-Path $bepDir 'core'))) { Write-Host "Invalid profile BepInEx dir: $bepDir"; exit 1 }
Write-Host "Profile BepInEx: $bepDir"

# 2) Build
dotnet build "$Proj\src\DreamEchoMod\DreamEchoMod.csproj" -c Release -p:BepDir="$bepDir"
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED'; exit 1 }

# 3) Deploy into profile plugins (game dir has no BepInEx folder - isolated mode)
$dst = Join-Path $bepDir 'plugins\DreamEchoMod.dll'
Copy-Item "$Proj\src\DreamEchoMod\bin\Release\$Runtime\DreamEchoMod.dll" $dst -Force
Write-Host "Deployed: $dst"
