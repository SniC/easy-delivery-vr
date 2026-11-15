# Changelog

Player-facing notes per release. `tools/New-Release.ps1` puts the section of the version being released into the
GitHub release notes, so add a section here before releasing.

## v0.2.2

- Faster: the headset view is no longer rendered twice per eye (the game's menu camera also drew into the headset).
- Smoother: removed work the mod did every frame, including a scan that could cause a small hitch every few seconds.
- New "Render resolution" setting (50–100%) in the VR settings for extra performance; the pixel-art look hides it well.
- Menus and the title screen no longer show a big gray panel floating next to them; instead a dark see-through backing
  sits right behind the menu (with the game's pixel corners), so it stays readable.

## v0.2.1

- Gentler controller rumble: it now only reacts to bumps, landings and crashes, not to accelerating, braking or
  steering, and is much softer (it was far too strong on some controllers, like the Steam Frame's).
- Vibration setting has finer steps (25% and 75%).

## v0.2.0

First release.

**VR**
- PC VR through SteamVR (OpenXR): stereo view with head tracking, for any SteamVR headset.
- Recenter any time: hold the right stick click.
- Play flat again with `EnableVR = false` in `BepInEx\config\easydeliveryvr.cfg`.

**Driving**
- Seated cab view behind the wheel; the chase camera is one click (right stick) away.
- Drive with the controllers (left stick steers, triggers for throttle and brake), or grab the steering wheel with one or both hands and turn it.
- Controller vibration on bumps and crashes.
- Fuel and time on a screen behind the wheel; radio, money and distance to the drop-off on screens in the dashboard. Move, resize and tilt them in the VR settings.
- Radio: hold the left grip and flick the left stick (down on/off, left/right channels, up scan).

**On foot**
- Over-the-head view by default; click the right stick for first person.
- In first person: walk where you look, snap or smooth turning, and your own hands.
- Pick up packages, open doors and the truck bed by reaching out and squeezing the grip; put a package down where your hand is.
- Held items sit in your right hand, scaled down if they're big.

**Menus and HUD**
- Menus, map and the computer on a screen in front of you, or on your left hand like a tablet in first person; point and click with the right hand's laser.
- Drop-off markers and labels line up with the world.
- Pause by clicking both sticks (many SteamVR setups keep the menu button for themselves).
- The controls screen shows the Touch controller buttons.
- Conversation text comes closer so it isn't behind the person you talk to.

**Comfort**
- Level horizon in the cab with adjustable truck tilt, no camera shake, optional vignette when turning.
- The game's retro vertex wobble is off in VR (can be turned back on).
- Hold Y for the VR settings panel: comfort, turning, wheel, vibration, menu and HUD size and distance, hands, dashboard screens, seat position.
