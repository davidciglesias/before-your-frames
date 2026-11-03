using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    [BepInPlugin(Guid, "Before Your Frames", Version)]
    [BepInDependency("raicuparta.uuvr-legacy", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "byeframeblink";
        public const string Version = "0.9.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // OSC
        internal static ConfigEntry<int> OscPort;
        internal static ConfigEntry<string> LeftLidAddress;
        internal static ConfigEntry<string> RightLidAddress;
        internal static ConfigEntry<bool> LidValuesInverted;
        internal static ConfigEntry<string> SourcePriority;
        internal static ConfigEntry<string> ExtraPorts;

        // Blink detection
        internal static ConfigEntry<float> CloseThreshold;
        internal static ConfigEntry<float> OpenThreshold;
        internal static ConfigEntry<EyeCombineMode> CombineMode;
        internal static ConfigEntry<float> MinClosedSeconds;
        internal static ConfigEntry<float> MinOpenSeconds;
        internal static ConfigEntry<float> DataTimeoutSeconds;
        internal static ConfigEntry<bool> ForceClickBlinkMode;
        internal static ConfigEntry<bool> BlockWebcam;

        // Head tracking
        internal static ConfigEntry<bool> PositionalTracking;
        internal static ConfigEntry<LevelHorizonMode> LevelHorizon;
        internal static ConfigEntry<float> LevelPitchDegrees;
        internal static ConfigEntry<float> PositionScale;
        internal static ConfigEntry<bool> SimulateHead;
        internal static ConfigEntry<bool> RecenterOnSceneStart;

        // Gaze
        internal static ConfigEntry<bool> GazeAiming;
        internal static ConfigEntry<EyeTrackingMode> EyeTracking;
        internal static ConfigEntry<ControllerButton> ControllerMenuButton;
        internal static ConfigEntry<ControllerButton> ControllerBlinkButton;
        internal static ConfigEntry<float> GazeDegreesPerUnit;
        internal static ConfigEntry<float> GazeSmoothingSeconds;
        internal static ConfigEntry<float> GazeHoldAfterBlink;
        internal static ConfigEntry<float> GazeRewindSeconds;
        internal static ConfigEntry<bool> GazeInvertX;
        internal static ConfigEntry<bool> GazeInvertY;
        internal static ConfigEntry<float> GazeOffsetYaw;
        internal static ConfigEntry<float> GazeOffsetPitch;
        internal static ConfigEntry<bool> GazeMoveReticle;
        internal static ConfigEntry<float> AimAssistDegrees;

        // VR presentation
        internal static ConfigEntry<bool> FullViewVeil;
        internal static ConfigEntry<bool> SmoothUi;
        internal static ConfigEntry<float> RenderScale;
        internal static ConfigEntry<float> MenuRenderScale;
        internal static ConfigEntry<PerformanceMode> Performance;
        internal static ConfigEntry<int> PerformanceMsaa;
        internal static ConfigEntry<bool> PerformanceHdr;
        internal static ConfigEntry<ReflectionMode> PerformanceReflections;
        internal static ConfigEntry<AutoResolutionMode> AutoResolution;
        internal static ConfigEntry<bool> AimFromRenderedView;
        internal static ConfigEntry<float> RevealSphereScale;
        internal static ConfigEntry<float> AutoResolutionMin;
        internal static ConfigEntry<bool> BlinkMenus;
        internal static ConfigEntry<bool> SkipIntro;
        internal static ConfigEntry<bool> IntroSeen;
        internal static ConfigEntry<float> MenuBlinkSeconds;
        internal static ConfigEntry<float> MenuSnapDegrees;
        internal static ConfigEntry<float> MenuPanelScale, MenuSpacing;
        internal static ConfigEntry<float> VeilHalfFovDegrees;
        internal static ConfigEntry<bool> ShowReticle;
        internal static ConfigEntry<KeyCode> ReticleToggleKey;
        internal static ConfigEntry<KeyCode> FreezeKey;
        internal static ConfigEntry<bool> ReticleAtTargetDepth;
        internal static ConfigEntry<bool> CameraDeviceFollow, CameraDeviceFollowGaze;
        internal static ConfigEntry<bool> SyncUiCamera, ViewSyncEnabled, ParticlesFaceHead;
        internal static ConfigEntry<float> ParticleMaxSize;
        internal static ConfigEntry<bool> GazeMenuOpener;
        internal static ConfigEntry<float> CameraDeviceDistance, CameraDeviceSize;
        internal static ConfigEntry<int> CameraDeviceResolution;
        internal static ConfigEntry<bool> CameraDeviceSharpFrame;
        internal static ConfigEntry<float> EyeSeparationScale;
        internal static ConfigEntry<KeyCode> EyeSeparationKey;
        internal static ConfigEntry<KeyCode> LevelHorizonKey;
        internal static ConfigEntry<bool> DisableDepthOfField;
        internal static ConfigEntry<bool> DisableVolumetricLights;
        internal static ConfigEntry<bool> DisableScreenSpaceReflections;
        internal static ConfigEntry<VignetteMode> Vignette;
        internal static ConfigEntry<bool> DisableWaterEdgeBlend;
        internal static ConfigEntry<bool> DisableWaterLightAbsorption;
        internal static ConfigEntry<bool> DisableWaterReflections;
        internal static ConfigEntry<bool> VrWaterReflections;
        internal static ConfigEntry<int> WaterReflectionResolution;
        internal static ConfigEntry<bool> SquintWhenBlinkReady;

        // Debug
        internal static ConfigEntry<bool> TraceCsv;
        internal static ConfigEntry<bool> TestMode;
        internal static ConfigEntry<KeyCode> OverlayKey;
        internal static ConfigEntry<KeyCode> ForceBlinkKey;
        internal static ConfigEntry<KeyCode> RecenterKey;

        internal static bool OverlayVisible;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            OscPort = Config.Bind("OSC", "Port", 9000, "UDP port to listen on for OSC eye data. frameeyeosc ('VRChat direct'), Steam Link's built-in OSC and VRCFaceTracking all send to 9000.");
            ExtraPorts = Config.Bind("OSC", "Extra Ports", "8888",
                "More UDP ports to listen on, comma-separated (Babble / Baballonia sends to 8888). Empty = none.");
            SourcePriority = Config.Bind("OSC", "Source Priority", "frameeyeosc,steamvr,babble,vrcft",
                "Eye-tracking sources in order of preference. Only one drives the game at a time: the first one in this list that is " +
                "sending eyelid data. A lower one takes over when the one in use goes quiet. Leave a name out to ignore that source. " +
                "frameeyeosc = .../FT/v2/... addresses (and other VRCFT-style senders); steamvr = Steam Link's own OSC output; " +
                "babble = Baballonia / Babble Frame Runtime; vrcft = VRCFaceTracking (/avatar/parameters/v2/...).");
            LeftLidAddress = Config.Bind("OSC", "Left Lid Address", "/avatar/parameters/FT/v2/EyeLidLeft",
                "frameeyeosc source: OSC address for the left eyelid. Any address ending in this value's last path segment also matches.");
            RightLidAddress = Config.Bind("OSC", "Right Lid Address", "/avatar/parameters/FT/v2/EyeLidRight",
                "frameeyeosc source: OSC address for the right eyelid. Any address ending in this value's last path segment also matches.");
            LidValuesInverted = Config.Bind("OSC", "Lid Values Inverted", false,
                "frameeyeosc source: set true if it sends 0 = open, 1 = shut (frameeyeosc itself sends 0 = shut). The other sources are handled automatically.");

            CloseThreshold = Config.Bind("Blink", "Close Threshold", 0.35f, "Openness (0 shut .. 1 wide) below which an eye counts as closed.");
            OpenThreshold = Config.Bind("Blink", "Open Threshold", 0.5f, "Openness above which a closed eye counts as open again (hysteresis).");
            CombineMode = Config.Bind("Blink", "Combine Mode", EyeCombineMode.Average, "How to combine both eyes.");
            MinClosedSeconds = Config.Bind("Blink", "Min Closed Seconds", 0.04f, "Eyes must stay closed this long before it counts (filters noise).");
            MinOpenSeconds = Config.Bind("Blink", "Min Open Seconds", 0.08f, "Eyes must stay open this long before it counts as open again (merges flicker).");
            DataTimeoutSeconds = Config.Bind("Blink", "Data Timeout Seconds", 1f, "If no OSC data arrives for this long, eyes are treated as open.");
            ForceClickBlinkMode = Config.Bind("Blink", "Force Click Blink Mode", true,
                "Forces the game into its mouse-click blink mode so the webcam tracker is never used. Eye-tracking blinks are injected as a virtual left mouse button.");
            BlockWebcam = Config.Bind("Blink", "Block Webcam", true,
                "Never let the game turn the webcam on (face tracker init and webcam preview feeds are skipped).");

            PositionalTracking = Config.Bind("Head Tracking", "Positional Tracking", false,
                "Adds head position (6DOF) on top of UUVR's rotation-only tracking. Off by default: the game is built around a fixed viewpoint.");
            PositionScale = Config.Bind("Head Tracking", "Position Scale", 1f, "Multiplier for head movement (use if the game world isn't 1 unit = 1 meter).");
            RecenterOnSceneStart = Config.Bind("Head Tracking", "Recenter On Scene Start", true,
                "Make the direction you're facing 'straight ahead' at the start of every scene (each scene is framed for that view). F6 does it any time; Shift+F6 toggles this in game.");
            LevelHorizon = Config.Bind("Head Tracking", "Level Horizon", LevelHorizonMode.RollAndPitch,
                "Some scenes are framed with the game camera tilted, which tilts the whole world in VR. Roll = level any sideways tilt; RollAndPitch = also level up/down tilt up to Level Pitch Degrees (scenes aimed farther up or down keep their framing); Off = as the game frames it.");
            LevelPitchDegrees = Config.Bind("Head Tracking", "Level Pitch Degrees", 20f,
                "With Level Horizon = RollAndPitch: camera pitch up to this many degrees is leveled; beyond it the leveling fades out, gone at twice this.");
            LevelHorizonKey = Config.Bind("Head Tracking", "Level Horizon Key", KeyCode.F, "Switch Level Horizon between Off and RollAndPitch in game.");
            SimulateHead = Config.Bind("Head Tracking", "Simulate Head", false,
                "Debug: replace the real HMD pose with a simulated one (keyboard IJKL/UO move, arrows rotate, or OSC /bye/head).");

            GazeAiming = Config.Bind("Gaze", "Gaze Aiming", true, "Aim at interactables with your eyes instead of the center of your view. false = blinks only: your eyes still blink, but you aim (and pick menu options) with your head, using the dot at the center of your view; a comfort option if gaze aiming feels twitchy.");
            EyeTracking = Config.Bind("Gaze", "Eye Tracking", EyeTrackingMode.Auto,
                "Auto = use the headset's eye data (gaze + eyelids) when it arrives, otherwise aim with your head. Off = always aim with your head (dot at the center of your view) and blink with the controller's Blink Button, e.g. for headsets without eye tracking.");
            ControllerMenuButton = Config.Bind("Controller", "Menu Button", ControllerButton.Menu,
                "Controller button that opens/closes the pause menu (SteamVR legacy button: Menu is the upper face button / menu button on most controllers, A the lower one).");
            ControllerBlinkButton = Config.Bind("Controller", "Blink Button", ControllerButton.A,
                "Controller button that acts as closing your eyes while held (any press clicks a menu option). Use it to play without eye tracking.");
            GazeDegreesPerUnit = Config.Bind("Gaze", "Degrees Per Unit", 45f, "Gaze angle for an OSC value of 1.0 (frameeyeosc/VRCFT: 45).");
            GazeSmoothingSeconds = Config.Bind("Gaze", "Smoothing Seconds", 0.08f, "Time constant of the gaze smoothing filter. Higher = steadier but laggier.");
            GazeHoldAfterBlink = Config.Bind("Gaze", "Hold After Blink Seconds", 0.2f, "Keep the last gaze direction this long after the eyes reopen.");
            GazeRewindSeconds = Config.Bind("Gaze", "Rewind On Blink Seconds", 0.25f,
                "When a blink starts, aim where you were looking this long before (eyes drift while the lids close). 0 = off.");
            GazeInvertX = Config.Bind("Gaze", "Invert X", false, "Flip horizontal gaze.");
            GazeInvertY = Config.Bind("Gaze", "Invert Y", false, "Flip vertical gaze.");
            GazeOffsetYaw = Config.Bind("Gaze", "Offset Yaw", 0f, "Degrees added to horizontal gaze (+ right) to correct a constant aim error.");
            GazeOffsetPitch = Config.Bind("Gaze", "Offset Pitch", 0f, "Degrees added to vertical gaze (+ up) to correct a constant aim error.");
            GazeMoveReticle = Config.Bind("Gaze", "Move Reticle", true, "Draw the game's reticle where you're aiming (head + gaze) instead of on the fixed UI panel.");
            AimAssistDegrees = Config.Bind("Gaze", "Aim Assist Degrees", 3f,
                "If the aim ray misses, also pick interactables within this cone (degrees). Bigger hitbox, no visual change. 0 = off.");
            AimFromRenderedView = Config.Bind("Gaze", "Aim From Rendered View", true,
                "Aim from the orientation each frame is actually drawn with instead of the head pose read earlier in the frame. Fixes targets only being hit a few degrees off what you see (seen on a Steam Frame running the game itself).");

            SkipIntro = Config.Bind("VR", "Skip Intro Logos", true,
                "Go straight to the main menu at startup (skips the logo splashes and the EULA notice) once the intro has been seen.");
            IntroSeen = Config.Bind("VR", "Intro Seen", false,
                "Set automatically the first time the main menu is reached. Set false to see the intro once more.");
            BlinkMenus = Config.Bind("VR", "Blink Menus", true,
                "Use the menus with your eyes: look at a button (head + gaze) and do a deliberate blink to press it.");
            MenuBlinkSeconds = Config.Bind("VR", "Menu Blink Seconds", 0.3f,
                "How long the eyes must stay closed for a blink to press a menu button (natural blinks are ~0.1-0.15 s).");
            MenuSnapDegrees = Config.Bind("VR", "Menu Snap Degrees", 8f,
                "In menus, the button nearest to where you look is selected if it's within this many degrees, so you don't have to hit it exactly.");
            MenuSpacing = Config.Bind("VR", "Menu Spacing", 1.35f, "How much farther apart a menu's options are spread vertically while it's open in VR, so they're easier to pick by gaze (1 = as the game lays them out).");
            MenuPanelScale = Config.Bind("VR", "Menu Panel Scale", 1.5f,
                "How much bigger the menu panel is drawn while a menu is open (spreads the options apart). 1 = UUVR's normal size.");
            FullViewVeil = Config.Bind("VR", "Full View Veil", true,
                "Show the game's eyelids and black fades across your whole view instead of only on the floating UI panel.");
            RenderScale = Config.Bind("VR", "Render Scale", 1f,
                "Renders the game at this multiple of SteamVR's resolution (supersampling): sharper, but heavier on the GPU. 1 = SteamVR's choice, below 1 is blurrier but lighter (0.25 to 2). Also in vr settings ('render quality').");
            MenuRenderScale = Config.Bind("VR", "Menu Render Scale", 1.5f,
                "Render scale in the main menu, whose wide scenery looks low-res at SteamVR's resolution. It's cheap to draw, so it can be higher than Render Scale.");
            Performance = Config.Bind("VR", "Performance Mode", PerformanceMode.Auto,
                "For running the game on the headset itself (Proton on a Steam Frame): Performance MSAA, medium shadows, no ambient occlusion, bloom, grain or FXAA, render quality steps 80/100/120%, menus at 100% or more. Auto = on under Proton/Wine, off on Windows.");
            PerformanceMsaa = Config.Bind("VR", "Performance MSAA", 2,
                "Anti-aliasing samples (0, 2, 4 or 8) in performance mode. Smooths jagged edges; 2 keeps a steady frame rate on a Steam Frame, 4 costs about 4 ms more per frame.");
            GazeMenuOpener = Config.Bind("VR", "Gaze Menu Opener", true, "During play, turn your head down to the left and a \"menu\" label appears there, fixed in the world: point your head at it and close your eyes for a moment to open the pause menu (no controller needed). Blinks while it's lit don't reach the game.");
            PerformanceReflections = Config.Bind("VR", "Performance Reflections", ReflectionMode.Off, "In performance mode: whether the game's live reflections (water, shiny floors) keep updating. On a Steam Frame they are the heaviest thing in some scenes (12 ms, half frame rate, against 9 ms and full rate without). Off = they keep their last image; Full = as the game does.");
            PerformanceHdr = Config.Bind("VR", "Performance HDR", false,
                "Keep HDR rendering in performance mode. Off by default: with MSAA it is the biggest GPU cost on the headset itself (the head tracking shakes when frames are missed).");
            AutoResolution = Config.Bind("VR", "Auto Resolution", AutoResolutionMode.Auto,
                "Lower the render resolution a little in heavy moments so no frame is missed (a missed frame makes the view shake), and raise it again when there's headroom. Auto = on in performance mode; On also helps on PCs with slower GPUs.");
            AutoResolutionMin = Config.Bind("VR", "Auto Resolution Min", 0.8f,
                "The lowest fraction of Render Scale that Auto Resolution may drop to.");
            RevealSphereScale = Config.Bind("VR", "Reveal Sphere Scale", 1f,
                "Enlarges the spheres some scenes use to change the room's look (e.g. to night), which are sized for a flat screen and show their edges in a headset's wider view. 1 = as the game sets them.");
            SmoothUi = Config.Bind("VR", "Smooth UI", true,
                "Filter the floating menu/HUD panel properly (mipmaps) so its text stays crisp in the headset instead of looking jagged and shimmering.");
            VeilHalfFovDegrees = Config.Bind("VR", "Veil Half FOV Degrees", 50f,
                "Half the vertical field of view the eyelids are stretched to (at least the headset's own, read from SteamVR). Lower = lids reach the center sooner.");
            ShowReticle = Config.Bind("VR", "Show Reticle", true,
                "Show the aiming dot during gameplay. Toggle in game with 'Reticle Toggle Key'. The menu cursor is always shown. If eye data stops arriving while you aim with your eyes, the dot is shown anyway (aim falls back to your head); in a head-aim mode you chose (Gaze Aiming = false, Eye Tracking = Off) this setting is followed.");
            ReticleToggleKey = Config.Bind("VR", "Reticle Toggle Key", KeyCode.F10, "Show/hide the gameplay aiming dot.");
            ViewSyncEnabled = Config.Bind("VR", "Render From Head Pose", true, "While drawing, turn the game camera to your head pose so particles (e.g. fog) and view-dependent shading sit right in the world instead of following your head.");
            ParticleMaxSize = Config.Bind("VR", "Particle Max Screen Size", 0f, "Unity shrinks particles bigger than this many screen-widths to fit; sized for a flat screen, it made big fog patches resize with your view so they followed your head. 0 = leave the game's value.");
            ParticlesFaceHead = Config.Bind("VR", "Particles Face Head", false, "Turn billboard particles (fog, dust) toward your head's position instead of laying them flat to the view, so they stay put when you turn your head.");
            SyncUiCamera = Config.Bind("VR", "Sync UI Camera", true, "Draw the game's in-world UI (e.g. the camera viewfinder) with exactly the main view, so it lines up with what it frames.");
            CameraDeviceFollow = Config.Bind("VR", "Camera Device Follows View", true,
                "In the scenes where you hold a camera, keep it (and the photo it takes) on the view you see as you turn your head.");
            CameraDeviceFollowGaze = Config.Bind("VR", "Camera Device Follows Eyes", true,
                "Also point the camera where your eyes look (with eye tracking).");
            CameraDeviceSize = Config.Bind("VR", "Camera Device Size", 1.6f,
                "How much bigger the camera's viewfinder is drawn, so its edges sit outside the headset's wider view (1 = as the game draws it).");
            CameraDeviceSharpFrame = Config.Bind("VR", "Camera Device Sharp Frame", true, "Draw the camera viewfinder's frame from a sharpened, higher-resolution copy (the game's is blurry when drawn this large).");
            CameraDeviceResolution = Config.Bind("VR", "Camera Device Resolution", 0, "Width (pixels) of the camera viewfinder's image; the game's 960 is blurry in a headset. 0 = leave the game's.");
            CameraDeviceDistance = Config.Bind("VR", "Camera Device Distance", 3f,
                "How much farther away the camera is drawn, at the same apparent size (1 = as the game places it; too close to focus on in a headset).");
            ReticleAtTargetDepth = Config.Bind("VR", "Reticle At Target Depth", true,
                "Draw the aiming dot at the distance of what you're looking at (same apparent size), so it doesn't double when you focus on something close.");
            EyeSeparationScale = Config.Bind("VR", "Eye Separation Scale", 0.6f,
                "Scales the distance between the two eyes' views. Below 1 the world looks bigger and close objects farther away, so they're easier to focus. 1 = real scale.");
            EyeSeparationKey = Config.Bind("VR", "Eye Separation Key", KeyCode.F11, "Cycle 'Eye Separation Scale' through 1, 0.8, 0.6, 0.4 in game.");
            DisableDepthOfField = Config.Bind("VR", "Disable Depth Of Field", true,
                "Turn off the game's depth-of-field blur. Your eyes do their own focusing in VR, so the blur just looks smeared.");
            DisableVolumetricLights = Config.Bind("VR", "Disable Volumetric Lights", true,
                "Turn off the game's light shafts / haze effect. It is drawn for the flat camera, so in the headset it sticks to your view as you turn your head.");
            DisableScreenSpaceReflections = Config.Bind("VR", "Disable Screen Space Reflections", true,
                "Turn off the game's screen-space reflections (e.g. on water). They are traced for the flat camera, so in the headset they move with your head.");
            Vignette = Config.Bind("VR", "Vignette", VignetteMode.Stretched,
                "The game's vignette (edge darkening it uses for mood at some moments). Stretched = drawn across your whole view; Off = never shown; Original = the flat-screen effect on each eye's image.");
            VrWaterReflections = Config.Bind("VR", "VR Water Reflections", true,
                "Render the water's reflection separately for each eye with the headset's view, so it stays on the water. Off = the game's flat-screen version (slides with your head).");
            WaterReflectionResolution = Config.Bind("VR", "Water Reflection Resolution", 1024, "Size (pixels) of the water reflection image with VR Water Reflections. Higher = sharper, slower.");
            DisableWaterReflections = Config.Bind("VR", "Disable Water Reflections", false,
                "Turn the water's reflection/refraction off entirely (plain colours instead), e.g. if VR Water Reflections is too slow.");
            DisableWaterEdgeBlend = Config.Bind("VR", "Disable Water Edge Blend", false,
                "Turn off the water's soft edges/foam where it meets objects. Drawn for the flat camera, so in the headset it can move with your head.");
            DisableWaterLightAbsorption = Config.Bind("VR", "Disable Water Light Absorption", false,
                "Turn off the water's depth-based light absorption (most of its brightness). Drawn for the flat camera, so in the headset it can move with your head.");
            SquintWhenBlinkReady = Config.Bind("VR", "Squint When Blink Ready", false,
                "The game raises the eyelids a little (a grey band at the edge of your view) while a scene-ending blink is ready. The metronome icon shows the same thing.");

            TestMode = Config.Bind("Debug", "Test Mode", false,
                "Ignore OSC from other machines (the headset); only localhost test input is used. Set by tools/run_tests.ps1 while it runs.");
            TraceCsv = Config.Bind("Debug", "Trace Csv", false, "Write a per-frame CSV trace to BepInEx/BYEFrameBlink_trace.csv.");
            OverlayKey = Config.Bind("Debug", "Overlay Key", KeyCode.F8, "Toggle the debug overlay.");
            ForceBlinkKey = Config.Bind("Debug", "Force Blink Key", KeyCode.F9, "Hold to simulate closed eyes.");
            FreezeKey = Config.Bind("Debug", "Freeze Key", KeyCode.F5, "Freeze/unfreeze the game without the pause menu (eyes treated as open while frozen). For inspecting scenes.");
            RecenterKey = Config.Bind("Debug", "Recenter Key", KeyCode.F6, "Make the direction you face straight ahead (and reset head position). Shift + this key toggles Recenter On Scene Start.");

            OscReceiver.Start(OscPort.Value, ExtraPorts.Value);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            RenderedView.Install();
            CameraDevice.Install();
            ViewSync.Install();
            InputRedirect.PatchGameInputCalls(harmony);
            HeadTracking.TryPatchUuvr(harmony);
            HorizonLevel.TryPatchUuvr(harmony);
            StereoComfort.TryPatchUuvr(harmony);
            SimHead.Patch(harmony);
            HeadTracking.PatchRecenter(harmony);
            IntroSkip.Init();

            Log.LogInfo("Before Your Frames loaded.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(OverlayKey.Value)) OverlayVisible = !OverlayVisible;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(RecenterKey.Value) && shift) ToggleRecenterOnSceneStart();
            else if (Input.GetKeyDown(RecenterKey.Value) || OscReceiver.ConsumeRecenterRequest()) HeadTracking.Recenter();
            if (Input.GetKeyDown(ReticleToggleKey.Value)) ToggleReticle();
            if (Input.GetKeyDown(EyeSeparationKey.Value)) StereoComfort.CycleEyeSeparation();
            if (Input.GetKeyDown(LevelHorizonKey.Value)) ToggleLevelHorizon();
            if (Input.GetKeyDown(FreezeKey.Value)) Freeze.Toggle();
            Freeze.Update();
            ParticleFacing.Update();
            MenuRecenter.Update();
            CameraTilt.Update();

            DebugCommands.Update();
            SimHead.Update();
            HeadTracking.EnsureSceneHook();
            AutoPlay.Update();
            BlinkModeEnforcer.Update();
            DebugTrace.Update();
            ControllerShortcuts.TrackPauseKeys();
            ControllerShortcuts.Update();
            VrSettingsMenu.Update();
            MenuDeclutter.Update();
            SubtitleStyle.Update();
            MenuOpener.Update();
            WebcamBlock.HideEmptyFeed();
        }

        internal static void ToggleReticle()
        {
            ShowReticle.Value = !ShowReticle.Value;
            Toast.Show(ShowReticle.Value ? "Aiming dot on" : "Aiming dot off");
        }

        internal static void ToggleLevelHorizon()
        {
            LevelHorizon.Value = LevelHorizon.Value == LevelHorizonMode.Off ? LevelHorizonMode.RollAndPitch : LevelHorizonMode.Off;
            Toast.Show(LevelHorizon.Value == LevelHorizonMode.Off ? "Level horizon: off" : "Level horizon: on");
        }

        internal static void ToggleRecenterOnSceneStart()
        {
            RecenterOnSceneStart.Value = !RecenterOnSceneStart.Value;
            Toast.Show(RecenterOnSceneStart.Value ? "Recenter on scene start: on" : "Recenter on scene start: off");
        }

        internal static void TogglePositionalTracking()
        {
            PositionalTracking.Value = !PositionalTracking.Value;
            Toast.Show(PositionalTracking.Value ? "Head movement: on" : "Head movement: off");
        }

        internal static void CycleVignette()
        {
            Vignette.Value = (VignetteMode)(((int)Vignette.Value + 1) % 3);
            Toast.Show("Vignette: " + VignetteLabel);
        }

        internal static void ToggleEyeTracking()
        {
            // Three steps: eyes aim + blink -> eyes only blink, head aims (comfort) -> off (head aims, controller blinks).
            if (EyeTracking.Value == EyeTrackingMode.Off)
            {
                EyeTracking.Value = EyeTrackingMode.Auto;
                GazeAiming.Value = true;
                Toast.Show("Eye tracking: on when available");
            }
            else if (GazeAiming.Value)
            {
                GazeAiming.Value = false;
                Toast.Show("Blinks only: aim with your head");
            }
            else
            {
                EyeTracking.Value = EyeTrackingMode.Off;
                GazeAiming.Value = true;
                Toast.Show("Eye tracking off: aim with your head");
            }
        }

        internal static string VignetteLabel =>
            Vignette.Value == VignetteMode.Stretched ? "whole view" : Vignette.Value == VignetteMode.Off ? "off" : "flat";

        private void LateUpdate()
        {
            StereoComfort.LateUpdate();
            VignetteVR.LateUpdate();
            UiSharpness.LateUpdate();
            BYEFrameBlink.Performance.LateUpdate();
            ReflectionThrottle.LateUpdate(BYEFrameBlink.Performance.Active);
            BYEFrameBlink.RenderScale.LateUpdate();
            BYEFrameBlink.AutoResolution.LateUpdate();
            ReticleFollower.LateUpdate();
            Veil.LateUpdate();
            MenuGaze.LateUpdate();
            Toast.LateUpdate();
            UiOverlay.EnforceLayerMasks();
        }

        private void OnGUI()
        {
            if (OverlayVisible) DebugOverlay.Draw();
        }

        private void OnDestroy()
        {
            OscReceiver.Stop();
            DebugTrace.Close();
        }
    }

    public enum EyeCombineMode
    {
        Average,
        BothClosed,
        EitherClosed,
    }

    public enum EyeTrackingMode
    {
        Auto,
        Off,
    }

    public enum ControllerButton
    {
        None,
        Menu,
        A,
        Grip,
        Trigger,
        Thumbstick,
    }
}
