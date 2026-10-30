# BYE Frame Blink

Play **Before Your Eyes** in VR on a **Steam Frame**, with your real blinks and eye gaze.

*Before Your Eyes* is controlled by blinking. The game normally reads your blinks from a webcam. This mod, a BepInEx plugin that runs next to [UUVR](https://github.com/Raicuparta/uuvr), replaces the webcam with the Frame's eye tracking:

- **Blinks** come from your eyelids. Holding your eyes shut works too, for the scenes that ask for it.
- **Gaze aiming.** You select things by looking at them, not by pointing your head.
- **Blink menus.** In the main and pause menus you look at a button and blink to press it.
- **Full-view eyelids and fades.** The game's eyelid animation and screen fades cover your whole field of view, not just a small panel.
- **Head-locked reticle** that follows where you look (and can be hidden), and a small **aim assist** so tiny targets are easier to hit.
- **Comfort options** for close-up scenes: smaller eye separation, no depth-of-field blur.
- **Optional head position tracking** (off by default; the game is built around a fixed viewpoint).
- **The webcam is never used.** The game's tracker is bypassed, so you get no camera prompts and no calibration.

Your normal mouse still works as a fallback.

> Unofficial fan mod. Not affiliated with Goodbye World Games, Valve or Raicuparta. You need your own copy of the game. This repository contains no game files.

## Status

Works on the author's Steam Frame. The whole game has also been played from start to the credits with an automated bot that drives the same input paths a player uses (simulated eyes and head), and 67 regression tests pass. Please open an issue if something misbehaves on your setup.

The eye data comes from frameeyeosc, an experimental tool that reads a private interface of the headset software, so a headset update can break it.

## What you need

- A **Steam Frame**, with SteamVR on the PC and the game streamed through Steam Link
- **Before Your Eyes** on Steam (Windows)
- [**Rai Pal**](https://github.com/Raicuparta/rai-pal), with **UUVR** (the Unity *Mono, legacy* variant) installed for the game. Rai Pal also installs BepInEx 5 for you.
- **frameeyeosc** running on the headset, sending to your PC (see below)

## Install

1. In Rai Pal, add *Before Your Eyes* and install **UUVR** for it. Start the game once with UUVR to check the VR view works, then quit.
2. Download `BYEFrameBlink.zip` from the [Releases](https://github.com/davidciglesias/before-your-frame/releases) page.
3. Extract it so that `BYEFrameBlink.dll` ends up in the game's BepInEx plugins folder:

   ```
   %APPDATA%\raicuparta\rai-pal\data\installed-mods\<number>\bepinex\BepInEx\plugins\BYEFrameBlink\BYEFrameBlink.dll
   ```

   `<number>` is a folder Rai Pal made for this game; the one that contains a `bepinex` folder with `plugins\uuvr-mono-legacy` is the right one.
4. Open `BepInEx\config\raicuparta.uuvr-legacy.cfg` and set `UI Patch Mode = CanvasRedirect`. The default (Mirror) paints the whole desktop frame onto the game's UI panel.
5. Start frameeyeosc on the headset (next section).
6. Start SteamVR, then launch the game from Rai Pal. At the first launch the plugin writes `BepInEx\config\byeframeblink.cfg`.

Use **New Game** or **Continue** as usual. Gaze at a menu button and blink to press it.

## Eye data from the headset

The plugin listens for OSC messages over UDP on port **9000** and understands the format frameeyeosc sends:

| Data | Address |
|---|---|
| Eyelid openness (0 shut, about 0.75 relaxed, 1 wide) | `/avatar/parameters/FT/v2/EyeLidLeft`, `.../EyeLidRight` |
| Gaze (1.0 = 45°) | `/avatar/parameters/FT/v2/EyeLeftX`, `EyeLeftY`, `EyeRightX`, `EyeRightY` |

### Install frameeyeosc on the Frame (once)

This is how the author set it up, with the default answers. The eye sender is [sasaken1102r/frameeyeosc](https://github.com/sasaken1102r/frameeyeosc), a fork of [konsti219's frameeyeosc](https://github.com/konsti219/frameeyeosc) (the original proof of concept), installed with sasaken's [installer](https://github.com/sasaken1102r/frame). On the Frame, open the desktop, start the terminal (Konsole) and run, then pick **frameeyeosc** in its menu:

```
curl -fsSL https://frame.sasaken1102s.net | sh
```

It installs into your home folder without admin rights and downloads from GitHub releases, but it still runs code on your headset, so read the script first if you want to (it is served as plain text at `https://frame.sasaken1102s.net/i`). Its dashboard panel is where you see the status and the target PC.

Keep the output on **VRChat directly** (the default): plain OSC to your PC on port 9000, with the default `/FT` prefix. The "VRCFT" output modes use other ports and formats, which this plugin does not read.

Allow **UDP 9000 inbound** in the Windows firewall (private network) for the game's executable, and make sure the PC and the Frame are on the same network. Anything else that sends the same OSC addresses will work too.

## Keys

| Key | Action |
|---|---|
| F6 | Recenter: the direction you're facing becomes straight ahead (also resets head position) |
| Shift+F6 | Turn automatic recentering at the start of each scene on/off |
| F8 | Debug overlay: OSC packet count and live eyelid values |
| F9 | Hold to act as eyes closed |
| F10 | Show/hide the aiming dot during gameplay (menus always show their cursor) |
| F11 | Cycle the world size: real, a bit bigger, bigger (default), much bigger (see Comfort below) |

## Settings

All settings are in `BepInEx\config\byeframeblink.cfg`; the defaults should work.

- **Blinks are missed or held-closed scenes drop out:** raise `Close Threshold` and `Open Threshold` (defaults 0.35 and 0.5). A held-shut eye reads around 0.2 on the Frame.
- **Blinks trigger too easily:** lower them, or raise `Min Closed Seconds`.
- **Aim is slightly off:** adjust `Offset Yaw` / `Offset Pitch`, or flip `Invert X` / `Invert Y`.
- **Aim feels jittery or laggy:** change `Smoothing Seconds`.
- **Targets are hard to hit:** raise `Aim Assist Degrees` (3 by default, 0 turns it off).
- **Head position:** set `Positional Tracking = true`. `Position Scale` scales the movement.
- **Recentering:** `Recenter On Scene Start` (default on) makes the direction you're facing straight ahead at the start of every scene, since each scene is framed for that view. F6 does it any time, and Shift+F6 turns this on or off in game. These keys and F10/F11 show a short message in your view.
- **Menus:** look roughly at an option and do a deliberate blink. The option nearest to where you look is selected if it is within `Menu Snap Degrees` (8; Back buttons reach further), and while a menu is open its panel is drawn `Menu Panel Scale` (1.5×) bigger so options are spread apart. `Menu Blink Seconds` (0.3) is how long the press blink must last; natural blinks are ignored.
- **Intro:** `Skip Intro Logos` skips the splash screens after the first launch.
- **Aiming dot:** `Show Reticle` (F10 toggles it). `Reticle At Target Depth` draws the dot at the distance of what you look at, so it doesn't split in two when you focus on something close.

### Comfort

The game was framed for a flat screen, and many scenes put objects a few centimetres from the camera, closer than your eyes can comfortably focus in a headset.

- **World size** (F11 in game: real, a bit bigger, bigger, much bigger). Making the world look bigger pushes close objects farther away, so they are easier to focus on. Default: bigger. In the config file this is `Eye Separation Scale` (1, 0.8, 0.6, 0.4; 1 is real size).
- `Disable Depth Of Field` (default on) removes the game's focus blur; in VR your eyes do the focusing.
- Some of the game's effects are drawn for the flat camera and in a headset stick to your view as you turn your head. These are off by default: `Disable Volumetric Lights` (light shafts and haze), `Disable Screen Space Reflections`, and `Disable Water Reflections` (the water then reflects a plain sky colour). The water keeps its soft edges and light absorption (`Disable Water Edge Blend` and `Disable Water Light Absorption` default to off; turn them on if those move with your view in some scene).
- `Squint When Blink Ready` (default off): in the flat game the eyelids rise a little when a scene-ending blink is ready, which in VR shows up as a grey band at the edge of your view. The metronome icon still tells you.

## Troubleshooting

- **Nothing happens when I blink.** Press F8. If "OSC packets" stays at 0, the headset's data isn't arriving: check the target address and port, the firewall, and that both are on the same network. If packets arrive but "Lid L/R" doesn't move, check the addresses in the `[OSC]` section.
- **The screen is black or shows the desktop.** Check `UI Patch Mode = CanvasRedirect` in the UUVR config.
- **Menu options are hard to select, or the desktop window shows debug panels.** Rai Pal may also have installed *Unity Explorer*, a modding tool whose windows sit on the desktop window. This plugin ignores them, but for normal play you can uninstall Unity Explorer for this game in Rai Pal (or press F7 to hide its windows).
- **The camera asks for access / calibration appears.** Make sure `Force Click Blink Mode` and `Block Webcam` are `true`.
- **A "no webcam" warning appears when you start playing.** The game checks for a webcam even though this mod never uses one. With `Block Webcam = true` the plugin skips that check; if a warning still shows up, you can ignore it (blinks come from the headset) and please open an issue with a photo of it.
- **Aim is off after a headset update.** frameeyeosc may need an update; try `Invert X/Y` and the offsets in the meantime.

The BepInEx log is `BepInEx\LogOutput.log`; attach it to bug reports.

## Building from source and testing

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): how it works, build instructions, the simulation tools and the autoplay bot.

## Credits and license

Built on [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX) and [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta, and on [frameeyeosc](https://github.com/konsti219/frameeyeosc) by konsti219 and its fork and installer by [sasaken1102r](https://github.com/sasaken1102r/frameeyeosc). *Before Your Eyes* is by GoodbyeWorld Games. None of them are included in this repository; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

MIT licensed, see [LICENSE](LICENSE).
