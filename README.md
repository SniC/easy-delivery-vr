# Easy Delivery Co VR

A VR mod for Easy Delivery Co (BepInEx 5 + Unity's OpenXR plugin injected at runtime).

## Game info

| | |
|---|---|
| Unity version | 6000.0.58 (URP, Input System 1.14.2) |
| Scripting backend | Mono (`EasyDeliveryCo_Data/Managed/Assembly-CSharp.dll`) |
| Local install | `D:\SteamLibrary\steamapps\common\Easy Delivery Co` |
| Mod loader | BepInEx 5.4.23.6 (x64) |

## Planned features

| Milestone | Features | Status |
|---|---|---|
| M0 | BepInEx plugin scaffold | done |
| M1 | OpenXR injection, stereo + head tracking, recenter | code done, needs headset test |
| M2 | Comfort (fixed FOV, no shake/jitter), seated cab driving, controller mapping | |
| M3 | HUD + in-game computer/menus on VR panels, laser pointer | |
| M4 | On-foot VR: head-relative locomotion, snap/smooth turn | |
| M5 | Hands, grab-the-wheel steering, haptics | |
| M6 | Grab packages/items by hand, vignette, settings polish | |

## Setup

1. Install BepInEx 5.4.23.x (win x64) into the game folder.
2. Install the .NET SDK (11 or newer), then build. The build copies the plugin into
   `BepInEx\plugins\EasyDeliveryVR\`:
   ```
   dotnet build -c Release
   ```
3. Install **Unity 6000.0.58** via Unity Hub (only needed once, to produce Unity's OpenXR runtime files), then:
   ```
   ./tools/Install-XrRuntime.ps1
   ```
4. Start SteamVR (Steam Link) or Virtual Desktop, then launch the game.

Controls: **F8** or both thumbsticks clicked = recenter. **F7** = dump camera setup to the BepInEx log.
Set `EnableVR = false` in `BepInEx\config\easydeliveryvr.cfg` to play flat.

## Repo layout

- `src/EasyDeliveryVR/`: the BepInEx plugin
- `unity-xr-runtime/`: empty Unity project used only to build Unity's XR runtime files
- `tools/`: install scripts
- `decompiled/` (git-ignored): `ilspycmd` output of the game, for reference

Game, Unity and BepInEx assemblies are **not** committed. They're referenced from the local game install;
override the path with `<GameDir>` in a git-ignored `Directory.Build.local.props`.
