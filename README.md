# BYE Frame Blink

Play **Before Your Eyes** in VR on a **Steam Frame**, with your real blinks and eye gaze.

*Before Your Eyes* is controlled by blinking. The game normally reads your blinks from a webcam. This mod, a BepInEx plugin that runs next to [UUVR](https://github.com/Raicuparta/uuvr), replaces the webcam with the Frame's eye tracking:

- **Blinks** come from your eyelids. Holding your eyes shut works too, for the scenes that ask for it.
- **Gaze aiming.** You select things by looking at them, not by pointing your head.
- **Blink menus.** In the main and pause menus you look at a button and blink to press it.
- **Full-view eyelids and fades.** The game's eyelid animation and screen fades cover your whole field of view, not just a small panel.
- **Head-locked reticle** that follows where you look, and a small **aim assist** so tiny targets are easier to hit.
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

This is how the author set it up, with the default answers. On the Frame, open the desktop, start the terminal (Konsole) and run the installer from [sasaken](https://frame.sasaken1102s.net), then pick **frameeyeosc** in its menu:

```
curl -fsSL https://frame.sasaken1102s.net | sh
```

It installs into your home folder without admin rights and downloads from GitHub releases, but it still runs code on your headset, so read the script first if you want to (it is served as plain text at `https://frame.sasaken1102s.net/i`). The installer's "dashboard panel" is where you can see the status and the target PC.

Leave the output on plain OSC to your PC on port 9000. If you switch it to a mode meant for VRCFaceTracking (a different port), this plugin will not receive it.

The upstream project is [konsti219/frameeyeosc](https://github.com/konsti219/frameeyeosc). If you build that one yourself instead, run it with `--target <PC address>:9000`.

Allow **UDP 9000 inbound** in the Windows firewall (private network) for the game's executable, and make sure the PC and the Frame are on the same network. Anything else that sends the same OSC addresses will work too.

## Keys

| Key | Action |
|---|---|
| F6 | Recenter head position (do this once you're seated) |
| F8 | Debug overlay: OSC packet count and live eyelid values |
| F9 | Hold to act as eyes closed |

## Settings

All settings are in `BepInEx\config\byeframeblink.cfg`; the defaults should work.

- **Blinks are missed or held-closed scenes drop out:** raise `Close Threshold` and `Open Threshold` (defaults 0.35 and 0.5). A held-shut eye reads around 0.2 on the Frame.
- **Blinks trigger too easily:** lower them, or raise `Min Closed Seconds`.
- **Aim is slightly off:** adjust `Offset Yaw` / `Offset Pitch`, or flip `Invert X` / `Invert Y`.
- **Aim feels jittery or laggy:** change `Smoothing Seconds`.
- **Targets are hard to hit:** raise `Aim Assist Degrees` (3 by default, 0 turns it off).
- **Head position:** set `Positional Tracking = true`. `Position Scale` scales the movement.
- **Menu presses:** `Menu Blink Seconds` (0.3) is how long a deliberate blink must last. Natural blinks are ignored.
- **Intro:** `Skip Intro Logos` skips the splash screens after the first launch.

## Troubleshooting

- **Nothing happens when I blink.** Press F8. If "OSC packets" stays at 0, the headset's data isn't arriving: check the target address and port, the firewall, and that both are on the same network. If packets arrive but "Lid L/R" doesn't move, check the addresses in the `[OSC]` section.
- **The screen is black or shows the desktop.** Check `UI Patch Mode = CanvasRedirect` in the UUVR config.
- **The camera asks for access / calibration appears.** Make sure `Force Click Blink Mode` and `Block Webcam` are `true`.
- **Aim is off after a headset update.** frameeyeosc may need an update; try `Invert X/Y` and the offsets in the meantime.

The BepInEx log is `BepInEx\LogOutput.log`; attach it to bug reports.

## Building from source and testing

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): how it works, build instructions, the simulation tools and the autoplay bot.

## Credits and license

Built on [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX) and [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta, and on [frameeyeosc](https://github.com/konsti219/frameeyeosc) by konsti219 and its Steam Frame installer by sasaken. *Before Your Eyes* is by GoodbyeWorld Games. None of them are included in this repository; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

MIT licensed, see [LICENSE](LICENSE).
