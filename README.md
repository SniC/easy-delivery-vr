# Easy Delivery Co VR

**Deliver packages through the snow in full VR.** Sit behind the wheel of your truck, steer it with your own hands,
read your fuel and radio off the dashboard, then hop out, grab the package and carry it to the door.

A free, unofficial VR mod for Easy Delivery Co. Unzip one file into the game folder and play. Made for Quest 2
(Steam Link or Virtual Desktop) and Steam Frame (Steam Link); other SteamVR / OpenXR headsets should work too.

## Features

### Drive it for real
- **Sit in the cab.** A seated first-person view behind the wheel, auto-centered on the steering wheel, with your head
  tracked in 6DoF. Lean out the window, check the mirror, look over your shoulder.
- **Grab the steering wheel.** Squeeze the grip on the wheel and turn it with one or both hands, or just use the stick.
- **Feel the road.** Controller vibration on bumps, landings and crashes.
- **A dashboard that works.** Fuel and time on a screen behind the wheel; radio, money and distance to the drop-off
  built into the dashboard. Move, resize and tilt every screen to your liking.
- **Tune the radio by hand.** Hold the grip and flick the stick to switch the radio on, change channels or scan.

### Walk, reach and carry
- **Your choice of view on foot.** The game's over-the-head view, or first person with one click.
- **Real hands.** The character's own mitten hands follow your controllers.
- **Reach out and grab.** Pick up packages, open the truck bed and doors, and take items by reaching for them;
  carry the package in your hand and put it down exactly where you want. Deliveries count as usual.
- **Tools in your hand.** Held items sit in your right hand instead of in your face, scaled to a sensible size.

### Menus that make sense in VR
- **Laser pointer.** Point and click on menus, the map and the in-game computer with your right hand.
- **Tablet on your wrist.** In first person, menus and the computer appear on your left hand like a tablet.
- **Markers in the world.** Drop-off markers, labels and item icons line up with the places they point at.
- **Readable conversations.** Dialog text moves in front of the person you're talking to.
- **Touch controls on the controls screen.** The game's own controls screen shows the controller buttons.

### Comfortable for hours
- **Level horizon.** The cab follows the truck's heading but not its bumps and sway, with an in-game slider if you
  want some of that back.
- **No camera shake, no zoom, no jitter.** Effects made for a flat screen are switched off in VR.
- **Stable visuals.** The game's retro vertex wobble is turned off so nearby objects don't swim (toggleable).
- **Snap or smooth turning**, an optional **comfort vignette**, and **recenter** any time.

### Everything adjustable in game
Hold **Y** for the VR settings panel: comfort, turning, wheel sensitivity, vibration, menu and HUD size and distance,
hands, dashboard screens and seat position. Changes apply instantly and are saved.

## Install

1. Download `EasyDeliveryVR-vX.Y.Z.zip` from the [releases](../../releases).
2. Unzip it into the game folder, next to `EasyDeliveryCo.exe` (Steam: right-click the game > Manage > Browse local
   files). It includes BepInEx 5 and Unity's OpenXR runtime files, so nothing else is needed.
3. Start SteamVR (Steam Link) or Virtual Desktop, then start the game.

To play flat again, set `EnableVR = false` in `BepInEx\config\easydeliveryvr.cfg`.

## Controls

| Input | Action |
|---|---|
| Left stick | Steer / walk / navigate menus |
| Right trigger / left trigger | Throttle / brake + reverse |
| A / B | Interact, select / handbrake, back |
| X | Map (in the truck) / items (on foot) |
| Y: tap / hold 1 s | Job selection / **VR settings panel** |
| Click both sticks together | Pause (also leaves the camera mode from the computer's cam.exe) |
| B (in cam.exe camera mode) | Back to the game |
| Left menu button: tap / hold 1 s | Pause / VR settings panel (if the runtime passes the button to the game; Steam Link doesn't) |
| Grip near the steering wheel | Grab the wheel and steer with your hands (one or both) |
| Grip near a package / door / truck bed / item (first person on foot) | Pick up, get in, open, use; grip again while carrying = put the package down where your hand is |
| Right grip (away from wheel) | Horn |
| Hold left grip (hand away from wheel) + flick left stick | Radio: left/right = previous/next station, down = on/off, up = start/stop scanning |
| Left stick click / hold 1 s | Headlights / reset truck |
| Right stick click / hold 1 s | Change camera / recenter view |
| Right stick | Camera look (chase / over-the-head cam); snap turn when on foot in first person |

Keyboard: **F8** = recenter, **F10** = VR settings panel, **F9** = save a VR view snapshot to `BepInEx/vr_snapshots`,
**F7** = dump camera setup to the BepInEx log. For development, writing `settings` or `snapshot` to `BepInEx/vr_cmd.txt`
triggers the same actions. Gamepad and keyboard keep working.
Set `EnableVR = false` in `BepInEx\config\easydeliveryvr.cfg` to play flat.


## Developing

### Game info

| | |
|---|---|
| Unity version | 6000.0.58 (URP, Input System 1.14.2) |
| Scripting backend | Mono (`EasyDeliveryCo_Data/Managed/Assembly-CSharp.dll`) |
| Mod loader | BepInEx 5.4.23.6 (x64) |

### Setup

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

### Making a release

Releases are built on a PC with the game installed (the build needs the game's assemblies, so GitHub's servers
can't build it). With the XR runtime installed (step 3 above) and the GitHub CLI signed in:

```
./tools/New-Release.ps1 -Version 0.2.0            # bump version, commit, tag, build, zip, publish the GitHub release
./tools/New-Release.ps1 -Version 0.2.0 -DryRun    # build + zip + notes into .\release only
./tools/New-Release.ps1 -Version 0.2.0 -Draft     # publish as a draft release
```

The release notes contain the install steps and the version's section of [CHANGELOG.md](CHANGELOG.md), written for
players. Add that section before releasing; the script stops if it's missing.

### Repo layout

- `src/EasyDeliveryVR/`: the BepInEx plugin
- `unity-xr-runtime/`: empty Unity project used only to build Unity's XR runtime files
- `tools/`: install scripts
- `decompiled/` (git-ignored): `ilspycmd` output of the game, for reference

Game, Unity and BepInEx assemblies are **not** committed. They're referenced from the local game install;
override the path with `<GameDir>` in a git-ignored `Directory.Build.local.props`.
