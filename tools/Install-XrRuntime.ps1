<#
.SYNOPSIS
  Builds the unity-xr-runtime helper project (headless) and copies Unity's OpenXR runtime files into the game.

.DESCRIPTION
  Requires Unity 6000.0.58 installed via Unity Hub with "Windows Build Support (Mono)" (included by default).
  Files copied (none of them are committed to this repo):
    <plugin>\XR\Unity.XR.*.dll                         managed XR Management + OpenXR
    <game>_Data\Plugins\x86_64\UnityOpenXR.dll         native OpenXR provider
    <game>_Data\Plugins\x86_64\openxr_loader.dll       Khronos loader
    <game>_Data\UnitySubsystems\UnityOpenXR\...        subsystem manifest Unity scans at startup

.EXAMPLE
  ./tools/Install-XrRuntime.ps1
  ./tools/Install-XrRuntime.ps1 -GameDir "E:\Games\Easy Delivery Co" -SkipBuild
#>
param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Easy Delivery Co",
    [string]$UnityExe,
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo "unity-xr-runtime"
$buildData = Join-Path $project "Build\XrRuntime_Data"
$gameData = Join-Path $GameDir "EasyDeliveryCo_Data"
$pluginXr = Join-Path $GameDir "BepInEx\plugins\EasyDeliveryVR\XR"

if (-not (Test-Path $gameData)) { throw "Game not found at $GameDir (pass -GameDir)." }

if (-not $SkipBuild) {
    if (-not $UnityExe) {
        $UnityExe = Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -Filter "6000.0.58*" -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $UnityExe) { throw "Unity 6000.0.58 not found. Install it with Unity Hub or pass -UnityExe." }

    Write-Host "Building XR runtime with $UnityExe (first run imports packages, can take several minutes)..."
    $log = Join-Path $project "Build\build.log"
    New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
    $p = Start-Process $UnityExe -Wait -PassThru -NoNewWindow -ArgumentList @(
        "-batchmode", "-nographics", "-quit",
        "-projectPath", "`"$project`"",
        "-executeMethod", "BuildXrRuntime.Build",
        "-buildTarget", "Win64",
        "-logFile", "`"$log`"")
    if ($p.ExitCode -ne 0) { throw "Unity build failed (exit $($p.ExitCode)). See $log" }
}

if (-not (Test-Path $buildData)) { throw "Build output not found at $buildData" }

# Managed assemblies -> plugin XR folder
New-Item -ItemType Directory -Force $pluginXr | Out-Null
$managed = Get-ChildItem (Join-Path $buildData "Managed") -Filter "Unity.XR.*.dll"
if (-not $managed) { throw "No Unity.XR.*.dll in build output" }
$managed | Copy-Item -Destination $pluginXr -Force

# Native plugins -> game Plugins\x86_64
$nativeSrc = Join-Path $buildData "Plugins\x86_64"
$nativeDst = Join-Path $gameData "Plugins\x86_64"
foreach ($n in "UnityOpenXR.dll", "openxr_loader.dll") {
    $src = Join-Path $nativeSrc $n
    if (-not (Test-Path $src)) { throw "Missing $n in build output ($nativeSrc)" }
    Copy-Item $src $nativeDst -Force
}

# Subsystem manifests -> game UnitySubsystems
$subSrc = Join-Path $buildData "UnitySubsystems"
if (-not (Test-Path $subSrc)) { throw "Missing UnitySubsystems folder in build output" }
Copy-Item $subSrc $gameData -Recurse -Force

Write-Host "Installed:"
$managed | ForEach-Object { Write-Host "  XR\$($_.Name)" }
Write-Host "  Plugins\x86_64\UnityOpenXR.dll, openxr_loader.dll"
Get-ChildItem (Join-Path $gameData "UnitySubsystems") -Recurse -File | ForEach-Object { Write-Host "  $($_.FullName.Replace($gameData + '\', ''))" }
Write-Host "Done. Start SteamVR (or Virtual Desktop) first, then launch the game."
