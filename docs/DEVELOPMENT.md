# Before Your Frames: development notes

How the plugin works, how to build it, and the test tooling. For installing and using it, see the [README](../README.md).

The plugin runs alongside UUVR and does two things:

- **Blinks from Steam Frame eye tracking.** Eyelid values arrive over OSC/UDP from [frameeyeosc](https://github.com/sasaken1102r/frameeyeosc) (a fork of [konsti219's](https://github.com/konsti219/frameeyeosc)), which runs on the headset.
- **6DOF head tracking.** UUVR's pose driver only applies rotation; this plugin adds head position.

## How it works
- **Blink input.** The game has a click-to-blink mode where every blink mechanic reads `Input.GetKey/GetKeyDown/GetKeyUp(KeyCode.Mouse0)`. Click means blink, hold means eyes shut.
  - At startup, the plugin rewrites the IL of every game method that makes those calls, about 65 methods. The calls go to wrappers that OR the real mouse button with an "eyes closed" state computed from the eyelid data.
  - The physical mouse still works as a fallback.
- **Blink mode.** `Force Click Blink Mode` keeps the game's `BlinkCounter` in `DebugKeyOnly`, so the webcam/ULSee tracker is never used.
- **Head position.** A Harmony postfix on `Uuvr.UuvrPoseDriver.UpdateTransform` sets the `VrChildCamera` local position to the head position minus a recenter origin.
  - This is **off by default** (`Positional Tracking`), because the game is built around a fixed viewpoint.
- **Gaze aiming.** The game picks interactables with `Camera.ViewportPointToRay(0.5, 0.5)`.
  - Those calls are IL-redirected to a ray bent by eye gaze: frameeyeosc `EyeLeftX/Y`, `EyeRightX/Y`, at 1.0 = 45°.
  - The gaze is smoothed, and frozen while the eyes close.
  - With no gaze data it falls back to the centre of your view.
- **Head-relative aim.** UUVR's RelativeMatrix mode only swaps render matrices, so Unity's `ViewportPointToRay` ignored the VR head and followed the game camera's transform.
  - The redirected centre ray is now built from the rendered pose: camera base × head rotation × eye gaze, starting from the head position.
- **Blink menus.** In the main and pause menus, the aim ray drives the UI pointer, and a deliberate blink (eyes closed at least `Menu Blink Seconds`, default 0.3 s) presses the button under it. Natural blinks are ignored.
- **Intro skip.** After the first launch (`Intro Seen`), the logo splashes and the EULA notice are skipped and the game goes straight to the main menu (`Skip Intro Logos`).
- **Reticle.** UUVR shows the HUD on a small panel, about 48°×27°, fixed in room space. The game's reticle was stuck on that panel and clamped at its edges.
  - The plugin now draws a head-locked copy of the reticle along the aim ray, through UUVR's UI overlay camera. It copies the reticle's sprite and states every frame and hides the original.
- **Aim assist.** If the aim ray misses, `InteractableManager` also tries rings of rays within `Aim Assist Degrees` (3° by default) and takes the nearest interactable. This makes the hitbox bigger without changing anything visually.
- **Full-view eyelids and fades.** The game darkens the screen with UI images: the sliding eyelids, `SceneTransFadePanel`, and menu backdrops.
  - In VR those only covered UUVR's panel. The plugin mirrors each one as a head-locked sprite, with the canvas stretched to your whole field of view, and hides the original.
  - The sprites use their own unused layer. UUVR's UI capture camera also renders the UI layer into the panel, which would otherwise draw them a second time.
- **Comfort** (`StereoComfort.cs`):
  - *Eye separation.* A postfix on UUVR's `VrCamera.UpdateRelativeMatrix` pulls each eye's view matrix toward the midpoint of the two (element-wise; both eyes share a rotation), scaling the stereo baseline by `Eye Separation Scale`.
  - *Depth of field.* The main camera's `PostProcessingBehaviour.profile.depthOfField` is switched off every frame (scenes clone their profile, and some scripts re-enable it).
  - *Squint.* `BlinkCanvasManager.Squint(true)` (lids rise while the end-of-scene blink is on deck) and its matching `Squint(false)` are skipped unless `Squint When Blink Ready`.
  - *Reticle depth.* The head-locked reticle is placed at the aim ray's hit distance (interactables first, then solid geometry), divided by the eye separation scale, and scaled to keep its angular size.
- **No-webcam warning.** `CheckCamConnection.OnEnable` fires `CamConnected` instead of checking `WebCamTexture.devices` when `Block Webcam` is on.
- **Webcam blocked.** `Block Webcam` skips the ULSee tracker init (it opens the camera natively) and IL-redirects the game's `WebCamTexture.Play()` calls to a no-op.
- **UUVR setting.** `UI Patch Mode = CanvasRedirect`, set in `raicuparta.uuvr-legacy.cfg`. The default Mirror mode copied the whole black desktop frame onto the UI panel.

## Build
The project references the game's and BepInEx's DLLs, so it needs two paths. Copy `BYEFrameBlink.local.props.example` to `BYEFrameBlink.local.props` (git-ignored) and set:

- `GameDir`: the folder with `Before Your Eyes.exe`.
- `BepInExDir`: the BepInEx folder Rai Pal installed for the game (`%APPDATA%\raicuparta\rai-pal\data\installed-mods\<number>\bepinex\BepInEx`). The build copies the DLL to its `plugins\BYEFrameBlink\`.

Or pass them on the command line: `dotnet build -c Release -p:GameDir="..." -p:BepInExDir="..."`. Add `-p:SkipDeploy=true` to build without copying.
The game must be closed while building, because it locks the DLL.

`tools\package.ps1` builds a Release and writes `dist\BYEFrameBlink.zip` for a GitHub release.

The `tools\*.ps1` scripts find the BepInEx folder on their own (the one whose `plugins` holds `BYEFrameBlink`); set `$env:BYE_BEPINEX` to override.

## Debug commands
Messages to `/bye/*` addresses (`invoke`, `cfg`, `goto`, `autoplay`, `shot`, ...) are only accepted from localhost; the UDP port is open to the LAN for the headset, so those commands from another machine are dropped.

## Headset check
Launch the game with frameeyeosc running (see the README) and press **F8** for the debug overlay: "OSC packets" should go up, and "Lid L/R" should follow your eyes.

## Keys
- **F8**: debug overlay. It's drawn on the desktop window, which may not refresh in VR; the tools below give a reliable view.
- **F9**: hold to act as eyes closed.
- **F6**: recenter head position. Do this once you're seated.
- **F7**: toggle UnityExplorer (that's UnityExplorer's own key).

## Tuning (`BepInEx\config\byeframeblink.cfg`)
- **Thresholds.** `Close Threshold` (0.35) and `Open Threshold` (0.5) are on frameeyeosc's scale: 0 = shut, ~0.75 = relaxed, 1 = wide.
  - If held-closed sections drop out, raise both thresholds, since a held-shut eye reads about 0.2 on the Frame.
  - If blinks trigger too easily, lower them.
- **Debounce.** `Min Closed Seconds` / `Min Open Seconds` filter flicker.
- **Combining eyes.** `Combine Mode`: Average, BothClosed, or EitherClosed.
- **Movement.** `Position Scale` scales head movement; `Positional Tracking = true` turns 6DOF on.
- **Gaze settings** (`[Gaze]` section):
  - `Gaze Aiming` and `Move Reticle` turn the two features on or off.
  - `Smoothing Seconds` sets how steady vs. responsive the gaze is.
  - `Offset Yaw` / `Offset Pitch` correct a constant aim error.
  - `Invert X/Y` flip the axes.
  - `Aim Assist Degrees` sets the hitbox cone; 0 turns it off.
- **VR settings** (`[VR]` section): `Full View Veil` turns the full-view eyelids and fades on or off. `Veil Half FOV Degrees` sets how far the lid travel is stretched.

## Autoplay (full playthrough test)
`tools\bye_sim.ps1 autoplay on` (or `off`) plays the game through the same input paths a player uses: simulated head aim and simulated eyes.
- It holds the eyes shut during eyes-closed sections, turns to each interactable and blinks at it, and blinks scenes along once the HUD blink is on deck.
- Gaze-only moments (the reticle changes and you just *look* at something) are found by reading what the game's gaze listeners wait for; `tools\bye_sim.ps1 gazetargets` logs them. Connect-the-dots prompts are traced dot by dot, gaze minigames over tagged targets are swept, and paint prompts keep the aim on the canvas.
- If nothing progresses for 4 minutes, it logs `STUCK` and skips the scene.
- The log is `BepInEx\BYEFrameBlink_autoplay.log`; screenshots go to `BepInEx\BYEFrameBlink_shots\autoplay\`, one per scene plus one every 45 s.
- While it runs, real headset input is ignored.

## Regression tests
`tools\run_tests.ps1` builds the plugin, restarts the game, and drives it over OSC, with the head pose and eyes simulated. It then asserts on `/bye/status` snapshots. SteamVR must be running.
- Your `byeframeblink.cfg` is backed up and restored, and results go to `tools\last_test_results.txt`. Exit code 0 means everything passed.
- `-NoBuild -Attach` runs against the game that's already open; `-CloseGame` quits it afterwards.
- It presses Continue, or New Game if there's no progress.
- What it covers:
  - **Load:** the plugin loads with no errors, at least 70 IL redirects are in place including every blink and aim mechanic, and the UUVR patch is applied.
  - **Webcam:** stays off in the menu and in gameplay.
  - **Blinks:** click-blink mode is active; an OSC blink reaches the game's `BlinkConfirmed`; flicker is filtered; frameeyeosc addresses work; a held blink counts as one; no data means eyes open.
  - **Gaze:** the aim ray bends the right angle and direction; gaze freezes during blinks.
  - **Reticle:** follows gaze and head rotation.
  - **Positional tracking:** obeys its on/off setting.

## Testing without wearing the headset
- `tools\bye_sim.ps1` sends fake OSC to the plugin, e.g.:
  - `blink`, `hold 3`, `lid 0.2`, `ft 0.1` (frameeyeosc addresses)
  - `head x y z pitch yaw roll`, `lean`
  - `shot <name>` (left-eye screenshot to `BepInEx\BYEFrameBlink_shots\`)
  - `buttons`, `click "new game"`, `invoke Type.Method`, `scenes`
  - `gaze x y`, `status [tag]`, `addrs` (every OSC address received), `inspect <Type>`
  - `cfg "guid|Section|Key|value"` (change any plugin's setting live, including UUVR's)
- Set `Simulate Head = true` to drive the head from OSC or the keyboard (IJKL/UO move, arrow keys rotate).
- Set `Trace Csv = true` to log every frame to `BepInEx\BYEFrameBlink_trace.csv`. It includes the render-time camera pose and the left-eye view matrix.
- `tools\sendkey.ps1 -Key F9 -HoldMs 500` sends real keys to the game window.

## Verified so far (simulated, 2026-10-29)
- Simulated 0.2 m head moves on X, Y and Z move the rendered camera exactly 0.2 m on the matching axis.
- With a simulated 90° yaw, the eye matrices rotate correctly: the left-eye offset is about 30 mm.
- A 150 ms OSC blink advanced the intro dialogue; `BlinkCounter.BlinkConfirmed` fired once per blink.
- A 30 ms flicker was filtered out.
- frameeyeosc `FT/v2/EyeLid*` addresses are recognised.
- **Not yet tested:**
  - real frameeyeosc data
  - real HMD position: the idle headset reported a zero pose
  - held-eyes-closed scenes, which use the same patched input path
