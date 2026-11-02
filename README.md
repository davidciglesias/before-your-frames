# Before Your Frames

Play **Before Your Eyes** in VR on a **Steam Frame**, with your real blinks and eye gaze.

Made for the Steam Frame, but not only for it: on a PC, other eye-tracking headsets can work too, through SteamVR's own eye output, Babble or VRCFaceTracking (see [Other eye-tracking sources](#other-eye-tracking-sources)). Those haven't been tested on real hardware other than the Frame yet. Headsets without eye tracking, like a Quest 3 or Pico streamed from a PC, can play with head aiming and a controller button for blinks (see [Playing without eye tracking](#playing-without-eye-tracking)).

*Before Your Eyes* is controlled by blinking. The game normally reads your blinks from a webcam. This mod, a BepInEx plugin that runs next to [UUVR](https://github.com/Raicuparta/uuvr), replaces the webcam with the Frame's eye tracking:

- **Blinks** come from your eyelids. Holding your eyes shut works too, for the scenes that ask for it.
- **Gaze aiming.** You select things by looking at them, not by pointing your head.
- **Blink menus.** In the main and pause menus you look at a button and blink to press it. The options are spread in two columns so they're easy to pick, and during play you can open the menu by looking down, no controller needed.
- **Readable subtitles** in any of the game's languages, chosen from a simple page, plus menus in your language.
- **Runs streamed from a PC or on the Steam Frame by itself.**
- **Full-view eyelids and fades.** The game's eyelid animation and screen fades cover your whole field of view, not just a small panel.
- **Head-locked reticle** that follows where you look (and can be hidden), and a small **aim assist** so tiny targets are easier to hit.
- **Comfort options** for close-up scenes: smaller eye separation, no depth-of-field blur.
- **Optional head position tracking** (off by default; the game is built around a fixed viewpoint).
- **The webcam is never used.** The game's tracker is bypassed, so you get no camera prompts and no calibration.

Your normal mouse still works as a fallback.

> Unofficial fan mod. Not affiliated with Goodbye World Games, Valve or Raicuparta. You need your own copy of the game. This repository contains no game files.

## Status

Works on the author's Steam Frame. The whole game has also been played from start to the credits with an automated bot that drives the same input paths a player uses (simulated eyes and head), and 115 regression tests pass. Please open an issue if something misbehaves on your setup.

On the Frame, the eye data comes from frameeyeosc, an experimental tool that reads a private interface of the headset software, so a headset update can break it. When streaming to a PC, SteamVR's own eye output (Steam Link) works too, without frameeyeosc.

## What you need

- A **Steam Frame**
- **Before Your Eyes** on Steam
- **frameeyeosc** on the headset, for the eye data (see [Eye data from the headset](#eye-data-from-the-headset))

You can play in two ways:

- **On the Steam Frame by itself**: the game runs on the headset (through Proton). Nothing else needed. See [Install on the Steam Frame](#install-on-the-steam-frame-standalone).
- **Streamed from a Windows PC** with SteamVR and Steam Link: sharper and smoother, if you have a capable PC. See [Install on a PC](#install-on-a-pc-streamed).

Standalone play is Steam Frame only: it works because the Frame runs Steam, Proton and SteamVR itself. Quest and Pico headsets don't, so on those the game has to be streamed from a PC.

## Install on the Steam Frame (standalone)

1. Install *Before Your Eyes* on the Frame from your Steam library and start it once (flat), then quit.
2. Install frameeyeosc (next section) if you haven't yet.
3. Open the Frame's desktop, start the terminal (Konsole) and run:

   ```
   curl -fsSLO https://github.com/davidciglesias/before-your-frames/releases/latest/download/frame_install.sh
   sh frame_install.sh --eyes-local
   ```

   The script ([`tools/frame_install.sh`](tools/frame_install.sh), read it first if you like) finds the game, downloads [BepInEx 5](https://github.com/BepInEx/BepInEx) (the mod loader), [UUVR](https://github.com/Raicuparta/uuvr) 0.3.1 (the VR mod) and this mod from their GitHub releases, checks them against known checksums and puts them in the game folder, with UUVR's `UI Patch Mode = CanvasRedirect` set. Nothing outside the game folder is touched, except that `--eyes-local` points frameeyeosc at the headset itself (`127.0.0.1`; your previous frameeyeosc config is kept as `config.json.before-bye`). Leave `--eyes-local` out if you set the target yourself. Run the script again to update the mod; your settings are kept.
4. In Steam, open *Before Your Eyes* > **Properties** > **Launch Options** and enter:

   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

   This lets Proton load BepInEx. Without it the game starts flat, without the mod.
5. Launch the game from the headset's library. The first launch takes a little longer.

On the headset, **performance mode** turns on by itself (`Performance Mode = Auto`): lighter rendering, automatic resolution between 80% and 120%, and the game's live reflections paused (water looks flatter, `Performance Reflections`). Menus always render at full resolution so their text stays readable.

To go back to streaming from a PC, restore frameeyeosc's target (`~/.config/frameeyeosc/config.json.before-bye`, or set the target PC in its dashboard panel). To remove the mod, clear the launch option (BepInEx no longer loads) and run *Verify integrity of game files* in Steam, which undoes UUVR's change to the game's settings file. You can also delete `BepInEx`, `winhttp.dll` and `doorstop_config.ini` from the game folder.

## Install on a PC (streamed)

You also need [**Rai Pal**](https://github.com/Raicuparta/rai-pal), with **UUVR** (the Unity *Mono, legacy* variant) installed for the game. Rai Pal also installs BepInEx 5 for you.

1. In Rai Pal, add *Before Your Eyes* and install **UUVR** for it. Start the game once with UUVR to check the VR view works, then quit.
2. Download `BYEFrameBlink.zip` from the [Releases](https://github.com/davidciglesias/before-your-frames/releases) page.
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

The Steam Frame tracks your eyes, but games can't read that data yet. **frameeyeosc** reads it on the headset and sends it out as OSC messages (the format VRChat face tracking uses), and this mod listens for them:

- **Standalone:** frameeyeosc sends to the headset itself (`127.0.0.1`), where the game runs.
- **Streamed:** frameeyeosc sends to your PC (its default target, `auto`, is the PC you stream from).

Either way the mod gets your eyelids (for blinks) and your gaze (for aiming), with frameeyeosc's own filtering and calibration on top. No webcam, no extra hardware.

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

For standalone play, set its target to `127.0.0.1` (the install script's `--eyes-local` does that). For streaming, keep the default target and allow **UDP 9000 inbound** in the Windows firewall (private network) for the game's executable, with the PC and the Frame on the same network. Anything else that sends the same OSC addresses will work too.

### Other eye-tracking sources

The mod also reads these, so frameeyeosc isn't the only option:

| Source | What it sends | Notes |
|---|---|---|
| **SteamVR (Steam Link)** | `/avatar/parameters/LeftEyeLid`, `RightEyeLid`, `LeftEyeX/Y`, `RightEyeX/Y` to port 9000 | Built into Steam Link when streaming from a Steam Frame (SteamVR 2.18.2+, SteamOS 0.4.3+). Turn on its OSC output and eye-tracking sharing in Steam Link's VR settings. Streaming only. |
| **Babble** (Baballonia, Babble Frame Runtime) | `/LeftEyeLid`, `/RightEyeLid`, `/LeftEyeX/Y`, `/RightEyeX/Y` to port 8888 | Enable Babble's OSC output. The mod also listens on 8888 (`Extra Ports`). |
| **VRCFaceTracking** | `/avatar/parameters/v2/EyeOpenLeft/Right`, `EyeLeftX`, `EyeRightX`, `EyeY` to port 9000 | VRCFT only sends parameters an avatar asks for; the mod doesn't set that up for you, so it works only if VRCFT is already sending these. Likely route for a **Quest Pro** (or Pico 4 Pro) streamed with Virtual Desktop; untested. |

Only one source drives the game at a time. By default it's the first of **frameeyeosc → SteamVR → Babble → VRCFT** that is sending eyelid data. If that one goes quiet for a second, the next one takes over. When a higher one starts sending again, it takes back over after half a second. Change the order, or leave a source out, with `Source Priority` in the `[OSC]` config section. The *eye tracking* row in the mod's VR settings shows which source is in use.

## Keys

| Key | Action |
|---|---|
| F6 | Recenter: the direction you're facing becomes straight ahead (also resets head position) |
| Shift+F6 | Turn automatic recentering at the start of each scene on/off |
| F8 | Debug overlay: OSC packet count and live eyelid values |
| F9 | Hold to act as eyes closed |
| F10 | Show/hide the aiming dot during gameplay (menus always show their cursor) |
| F11 | Cycle the world size: real, a bit bigger, bigger (default), much bigger (see Comfort below) |

### VR controller shortcuts

They work the same on either controller, so you can play holding just one:

| Button | Action |
|---|---|
| Upper face button (Y / D-pad up on Steam Frame) | Open/close the pause menu (or back out of VR settings) |
| Lower face button | Hold = eyes closed (blink without eye tracking; a press clicks a menu option) |
| Thumbstick/trackpad click | Recenter view (same as F6) |
| Grip | Cycle the world size (same as F11) |
| Trigger | Show/hide the aiming dot (same as F10) |

These are read through SteamVR's "legacy" controller buttons, the compatibility layer it keeps for exactly this kind of pre-2019, point-and-click game, so they need no per-headset setup. SteamVR exposes only two face-button slots there: `Menu Button` and `Blink Button` in the `[Controller]` section pick which one does what (`Menu` / `A`), if your controller maps them the other way round. The dedicated Menu/Start button next to the Steam button never reaches games, so it can't be used.

### Menus in VR

- The main menu, the pause menu and chapter select are laid out in **two columns** with plenty of room between options, so they're easy to pick by gaze. Options about the webcam (calibration, camera feed, eye detection guide) are hidden, since the headset replaces it.
- **subtitles** replaces the game's settings page: every subtitle language (and "no subtitles") is a button, and the menus follow the chosen language. Subtitles are drawn bigger and bold, with an outline, so they read well in the headset.
- **vr settings**: eye tracking, world size, aiming dot, recentering, render quality, performance mode and auto resolution, all changeable with gaze + blink (or the controller). Changes are saved to the config file right away.
- **Opening the menu without a controller:** during play, turn your head down to the left. A "menu" label appears there, fixed in the world. Point your head at it (it lights up) and close your eyes for half a second to open the pause menu. Blinks don't reach the game while it's lit, so the story can't move on by accident. `Gaze Menu Opener = false` turns this off.

### Playing without eye tracking

Headsets without eye tracking work too: with no eye data the aim follows your head (the dot sits at the center of your view and is always shown), and the lower face button acts as your eyes, held down for as long as the game wants them closed. To force this even when eye data arrives, set `Eye Tracking = Off` (or choose it in vr settings). The main menu then shows `eyes: no data` (or `eyes: off`) after the version.

That's how a **Quest 3, Pico** or any other PC VR headset plays. Install the mod on the PC as in [Install on a PC](#install-on-a-pc-streamed), then stream SteamVR to the headset with Quest Link / Air Link, Virtual Desktop, ALVR or Steam Link. Not tested on these headsets yet. They can't run the game standalone: that needs Steam, Proton and SteamVR on the headset itself, which only the Steam Frame has.

## Settings

All settings are in `BepInEx\config\byeframeblink.cfg`; the defaults should work.

- **Blinks are missed or held-closed scenes drop out:** raise `Close Threshold` and `Open Threshold` (defaults 0.35 and 0.5). A held-shut eye reads around 0.2 on the Frame.
- **Blinks trigger too easily:** lower them, or raise `Min Closed Seconds`.
- **Aim is slightly off:** adjust `Offset Yaw` / `Offset Pitch`, or flip `Invert X` / `Invert Y`.
- **Aim feels jittery or laggy:** change `Smoothing Seconds`.
- **Targets are hard to hit:** raise `Aim Assist Degrees` (3 by default, 0 turns it off).
- **Head position:** set `Positional Tracking = true`. `Position Scale` scales the movement.
- **Recentering:** `Recenter On Scene Start` (default on) makes the direction you're facing straight ahead at the start of every scene, since each scene is framed for that view. F6 does it any time, and Shift+F6 turns this on or off in game. These keys and F10/F11 show a short message in your view.
- **Menus:** look roughly at an option and do a deliberate blink. The option nearest to where you look is selected if it is within `Menu Snap Degrees` (8; Back buttons reach further), and while a menu is open its panel is drawn `Menu Panel Scale` (1.5×) bigger and the main and pause menu options are spaced `Menu Spacing` (1.35×) farther apart, so they are easier to pick. `Menu Blink Seconds` (0.3) is how long the press blink must last; natural blinks are ignored.
- **Intro:** `Skip Intro Logos` skips the splash screens after the first launch.
- **Aiming dot:** `Show Reticle` (F10 toggles it). `Reticle At Target Depth` draws the dot at the distance of what you look at, so it doesn't split in two when you focus on something close.

### Comfort

The game was framed for a flat screen, and many scenes put objects a few centimetres from the camera, closer than your eyes can comfortably focus in a headset.

- **World size** (F11 in game: real, a bit bigger, bigger, much bigger). Making the world look bigger pushes close objects farther away, so they are easier to focus on. Default: bigger. In the config file this is `Eye Separation Scale` (1, 0.8, 0.6, 0.4; 1 is real size).
- `Disable Depth Of Field` (default on) removes the game's focus blur; in VR your eyes do the focusing.
- Some of the game's effects are drawn for the flat camera and in a headset stick to your view as you turn your head. These are off by default: `Disable Volumetric Lights` (light shafts and haze) and `Disable Screen Space Reflections`. The water's reflections are re-rendered for each eye instead (`VR Water Reflections`, sharpness `Water Reflection Resolution`); `Disable Water Reflections` turns them off entirely if that's too slow.
- **Sharpness.** SteamVR picks the render resolution, and over a headset's wide field of view it is much coarser than a monitor, which shows most on the main menu's wide scenery. `Menu Render Scale` (default 1.5) renders the main menu sharper; it's cheap to draw there. `Render Scale` (default 1, also "render quality" in vr settings) does the same in game, at a GPU cost. Raising the game's resolution in SteamVR's per-app video settings works too. `Smooth UI` (default on) keeps menu text crisp instead of jagged. On the headset itself (performance mode) the render quality steps are 80/100/120%, auto resolution never drops below 80% (`Auto Resolution Min`), and menus always render at 100% or more so their text stays readable.
- `Vignette` (default `Stretched`): the edge darkening the game uses for mood at some moments is drawn across your whole view instead of on the flat image. `Off` removes it, `Original` keeps the flat-screen version.
- `Render From Head Pose` (default on): while each frame is drawn, the game camera is turned to your head pose, so shading that depends on the view (for example the room tint in one chapter 3 scene) stays in the world instead of following your head.
- **The camera you hold in some scenes:** it stays in front of you and points where you look (`Camera Device Follows View`, `Camera Device Follows Eyes`). `Camera Device Distance` (3) draws it farther away at the same size so it's easier to focus on, `Camera Device Size` (1.6) enlarges it so its edges stay out of view, and `Camera Device Sharp Frame` (on) draws its frame from a sharpened, higher-resolution copy. `Sync UI Camera` (on) draws the game's in-world UI from exactly your view, so the viewfinder sits on the aiming dot.
- `Squint When Blink Ready` (default off): in the flat game the eyelids rise a little when a scene-ending blink is ready, which in VR shows up as a grey band at the edge of your view. The metronome icon still tells you.

## Known issues

- **Standalone: SteamVR's menu opens at launch, and the game is listed as "wine-preloader" without a picture.** Steam treats *Before Your Eyes* as a flat game and docks its window in SteamVR's menu, and under Proton SteamVR names it after the Wine process. Press **Resume Game** once; the rest of the session is unaffected. Other Steam Frame mods of flat games hit the same thing, and there's no fix from a mod's side so far.
- **Standalone: water looks flatter** in performance mode, because live reflections are paused. Set `Performance Reflections = Full` to bring them back, at a big frame-rate cost in some scenes.

## Troubleshooting

- **Nothing happens when I blink.** Press F8. If "OSC packets" stays at 0, the headset's data isn't arriving: check the target address and port, the firewall, and that both are on the same network. If packets arrive but "Lid L/R" doesn't move, check the addresses in the `[OSC]` section.
- **The screen is black or shows the desktop.** Check `UI Patch Mode = CanvasRedirect` in the UUVR config.
- **Menu options are hard to select, or the desktop window shows debug panels.** Rai Pal may also have installed *Unity Explorer*, a modding tool whose windows sit on the desktop window. This plugin ignores them, but for normal play you can uninstall Unity Explorer for this game in Rai Pal (or press F7 to hide its windows).
- **The camera asks for access / calibration appears.** Make sure `Force Click Blink Mode` and `Block Webcam` are `true`.
- **A "no webcam" warning appears when you start playing.** The game checks for a webcam even though this mod never uses one. With `Block Webcam = true` the plugin skips that check; if a warning still shows up, you can ignore it (blinks come from the headset) and please open an issue with a photo of it.
- **Aim is off after a headset update.** frameeyeosc may need an update; try `Invert X/Y` and the offsets in the meantime.

The BepInEx log is `BepInEx\LogOutput.log`; attach it to bug reports, along with the mod version shown under the game version in the main menu (`VR V ...`).

## Building from source and testing

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): how it works, build instructions, the simulation tools and the autoplay bot.

## Credits and license

Built on [BepInEx](https://github.com/BepInEx/BepInEx), [HarmonyX](https://github.com/BepInEx/HarmonyX) and [UUVR](https://github.com/Raicuparta/uuvr) by Raicuparta, and on [frameeyeosc](https://github.com/konsti219/frameeyeosc) by konsti219 and its fork and installer by [sasaken1102r](https://github.com/sasaken1102r/frameeyeosc). *Before Your Eyes* is by GoodbyeWorld Games. None of them are included in this repository; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

MIT licensed, see [LICENSE](LICENSE).
