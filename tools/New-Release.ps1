<#
.SYNOPSIS
  Builds the mod, packages a ready-to-unzip release and publishes it as a GitHub release.

.DESCRIPTION
  The build needs the game's own assemblies, so releases are made on a PC with the game installed
  (not on GitHub's servers). Steps:
    1. Sets the version in Plugin.cs and the .csproj, commits "Release vX.Y.Z", tags vX.Y.Z.
    2. Builds Release.
    3. Packages EasyDeliveryVR-vX.Y.Z.zip laid out like the game folder:
         BepInEx\plugins\EasyDeliveryVR\EasyDeliveryVR.dll
         BepInEx\plugins\EasyDeliveryVR\XR\Unity.XR.*.dll
         EasyDeliveryCo_Data\Plugins\x86_64\UnityOpenXR.dll, openxr_loader.dll
         EasyDeliveryCo_Data\UnitySubsystems\UnityOpenXR\UnitySubsystemsManifest.json
         INSTALL.txt, THIRD-PARTY-NOTICES.txt
       The Unity OpenXR files are taken from the game install (put there by Install-XrRuntime.ps1).
    4. Pushes main and the tag, then creates the GitHub release with install instructions and the
       changes since the previous release.

.EXAMPLE
  ./tools/New-Release.ps1 -Version 0.2.0
.EXAMPLE
  ./tools/New-Release.ps1 -Version 0.2.0 -DryRun     # build + zip + notes in .\release only; no commit, tag or upload
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version,
    [string] $GameDir = "D:\SteamLibrary\steamapps\common\Easy Delivery Co",
    [switch] $DryRun,
    [switch] $Draft
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$tag = "v$Version"
$git = if (Get-Command git -ErrorAction SilentlyContinue) { "git" } else { "C:\Program Files\Git\cmd\git.exe" }
$gh = if (Get-Command gh -ErrorAction SilentlyContinue) { "gh" } else { "C:\Program Files\GitHub CLI\gh.exe" }
$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { "dotnet" } else { "C:\Program Files\dotnet\dotnet.exe" }

function Run([string] $exe) {
    & $exe @args
    if ($LASTEXITCODE -ne 0) { throw "$exe $($args -join ' ') failed ($LASTEXITCODE)" }
}

Push-Location $repo
try {
    # ---- Checks ----
    if (-not $DryRun) {
        if (& $git status --porcelain) { throw "Working tree has uncommitted changes; commit or stash them first." }
        if ((& $git branch --show-current) -ne "main") { throw "Releases are made from main." }
        if (& $git tag --list $tag) { throw "Tag $tag already exists." }
        Run $gh auth status
    }
    $data = Join-Path $GameDir "EasyDeliveryCo_Data"
    $xrManaged = Join-Path $GameDir "BepInEx\plugins\EasyDeliveryVR\XR"
    foreach ($p in $xrManaged, (Join-Path $data "Plugins\x86_64\UnityOpenXR.dll"), (Join-Path $data "Plugins\x86_64\openxr_loader.dll"),
                   (Join-Path $data "UnitySubsystems\UnityOpenXR")) {
        if (-not (Test-Path $p)) { throw "Missing $p. Run tools/Install-XrRuntime.ps1 first." }
    }
    $previous = (& $git describe --tags --abbrev=0 2>$null)

    # ---- Version ----
    $pluginCs = "src\EasyDeliveryVR\Plugin.cs"
    $csproj = "src\EasyDeliveryVR\EasyDeliveryVR.csproj"
    if (-not $DryRun) {
        (Get-Content $pluginCs -Raw) -replace 'Version = "[^"]+"', "Version = `"$Version`"" | Set-Content $pluginCs -NoNewline
        (Get-Content $csproj -Raw) -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>" | Set-Content $csproj -NoNewline
        if (& $git status --porcelain) {
            Run $git commit -q -am "Release $tag"
        }
        Run $git tag -a $tag -m "Easy Delivery VR $tag"
    }

    # ---- Build ----
    Run $dotnet build -c Release "-p:GameDir=$GameDir" --nologo -v q
    $dll = "src\EasyDeliveryVR\bin\Release\EasyDeliveryVR.dll"
    if (-not (Test-Path $dll)) { throw "Build output $dll not found" }

    # ---- Package ----
    $out = Join-Path $repo "release"
    $stage = Join-Path $out "EasyDeliveryVR-$tag"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $pluginDir = Join-Path $stage "BepInEx\plugins\EasyDeliveryVR"
    $nativeDir = Join-Path $stage "EasyDeliveryCo_Data\Plugins\x86_64"
    New-Item -ItemType Directory -Force (Join-Path $pluginDir "XR"), $nativeDir | Out-Null
    Copy-Item $dll $pluginDir
    Copy-Item (Join-Path $xrManaged "Unity.XR.*.dll") (Join-Path $pluginDir "XR")
    Copy-Item (Join-Path $data "Plugins\x86_64\UnityOpenXR.dll"), (Join-Path $data "Plugins\x86_64\openxr_loader.dll") $nativeDir
    Copy-Item (Join-Path $data "UnitySubsystems") (Join-Path $stage "EasyDeliveryCo_Data") -Recurse

    $install = @"
Easy Delivery VR $tag
=====================

VR mod for Easy Delivery Co (BepInEx 5 + Unity OpenXR). Tested with Quest 2 via Steam Link and
Virtual Desktop, and Steam Frame via Steam Link.

Install
-------
1. Install BepInEx 5.4.23 or newer (5.x, Windows x64) into the game folder:
   https://github.com/BepInEx/BepInEx/releases  (file BepInEx_win_x64_5.4.x.zip)
   Unzip it next to EasyDeliveryCo.exe and start the game once, then close it.
2. Unzip this release into the same game folder (next to EasyDeliveryCo.exe). It adds:
     BepInEx\plugins\EasyDeliveryVR\           the mod and Unity's OpenXR files
     EasyDeliveryCo_Data\Plugins\x86_64\       UnityOpenXR.dll, openxr_loader.dll
     EasyDeliveryCo_Data\UnitySubsystems\      OpenXR subsystem manifest
3. Start SteamVR (Steam Link) or Virtual Desktop (OpenXR runtime), then start the game.

To play flat again, set EnableVR = false in BepInEx\config\easydeliveryvr.cfg.
To uninstall, delete the files listed in step 2.

Controls (Touch / Index naming)
-------------------------------
Left stick            steer / walk / menus          Triggers      throttle (R) / brake + reverse (L)
A / B                 interact, ok / brake, back    X             map / items
Y tap / hold 1 s      job selection / VR settings   Both sticks   pause (click together)
Left stick click      lights (hold 1 s: reset)      Right click   change camera (hold 1 s: recenter)
Grip at the wheel     grab and steer by hand        Right grip    horn
Left grip + stick     radio: down on/off, left/right channels, up scan
First person on foot  grip near things to pick up / use; grip again to put a package down

Hold Y in game for the VR settings panel: comfort (cab tilt, vignette, turning), wheel, vibration,
menu / HUD size and distance, dashboard screens, seat position.
"@
    Set-Content (Join-Path $stage "INSTALL.txt") $install

    Set-Content (Join-Path $stage "THIRD-PARTY-NOTICES.txt") @"
This release includes files from Unity's OpenXR plugin (com.unity.xr.openxr), XR Management and XR Core Utils
(Unity.XR.*.dll, UnityOpenXR.dll, UnitySubsystemsManifest.json), (c) Unity Technologies, licensed under the
Unity Companion License: https://unity.com/legal/licenses/unity-companion-license

openxr_loader.dll is the Khronos OpenXR loader, (c) The Khronos Group Inc., Apache License 2.0:
https://github.com/KhronosGroup/OpenXR-SDK/blob/main/LICENSE

Easy Delivery Co is not included and is (c) its developers. This mod is unofficial.
"@

    $zip = Join-Path $out "EasyDeliveryVR-$tag.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

    # ---- Release notes ----
    $range = if ($previous) { "$previous..HEAD" } else { "HEAD" }
    $changes = & $git log $range --no-merges --format="- %s" | Where-Object { $_ -notmatch '^- Release v' }
    $notes = Join-Path $out "notes-$tag.md"
    @"
## Install

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (5.4.23 or newer, ``BepInEx_win_x64_5.4.x.zip``) into the Easy Delivery Co folder and start the game once.
2. Download **EasyDeliveryVR-$tag.zip** below and unzip it into the same folder, next to ``EasyDeliveryCo.exe``.
3. Start SteamVR (Steam Link) or Virtual Desktop, then start the game.

Hold **Y** in game for the VR settings. Full controls are in ``INSTALL.txt`` in the zip. To play flat again, set ``EnableVR = false`` in ``BepInEx\config\easydeliveryvr.cfg``.

## Changes$(if ($previous) { " since $previous" })

$($changes -join "`n")
"@ | Set-Content $notes

    Write-Host "Package: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
    Write-Host "Notes:   $notes"
    if ($DryRun) { Write-Host "Dry run: nothing committed, tagged or uploaded."; return }

    # ---- Publish ----
    Run $git push -q origin main
    Run $git push -q origin $tag
    $flags = @("--title", "Easy Delivery VR $tag", "--notes-file", $notes)
    if ($Draft) { $flags += "--draft" }
    Run $gh release create $tag $zip @flags
}
finally {
    Pop-Location
}
