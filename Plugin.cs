using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    [BepInPlugin(Guid, "BYE Frame Blink", "0.4.0")]
    [BepInDependency("raicuparta.uuvr-legacy", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "byeframeblink";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // OSC
        internal static ConfigEntry<int> OscPort;
        internal static ConfigEntry<string> LeftLidAddress;
        internal static ConfigEntry<string> RightLidAddress;
        internal static ConfigEntry<bool> LidValuesInverted;

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
        internal static ConfigEntry<AutoResolutionMode> AutoResolution;
        internal static ConfigEntry<bool> AimFromRenderedView;
        internal static ConfigEntry<float> AutoResolutionMin;
        internal static ConfigEntry<bool> BlinkMenus;
        internal static ConfigEntry<bool> SkipIntro;
        internal static ConfigEntry<bool> IntroSeen;
        internal static ConfigEntry<float> MenuBlinkSeconds;
        internal static ConfigEntry<float> MenuSnapDegrees;
        internal static ConfigEntry<float> MenuPanelScale;
        internal static ConfigEntry<float> VeilHalfFovDegrees;
        internal static ConfigEntry<bool> ShowReticle;
        internal static ConfigEntry<KeyCode> ReticleToggleKey;
        internal static ConfigEntry<bool> ReticleAtTargetDepth;
        internal static ConfigEntry<float> EyeSeparationScale;
        internal static ConfigEntry<KeyCode> EyeSeparationKey;
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

            OscPort = Config.Bind("OSC", "Port", 9000, "UDP port to listen on for OSC eyelid data (frameeyeosc 'VRChat direct' mode uses 9000).");
            LeftLidAddress = Config.Bind("OSC", "Left Lid Address", "/avatar/parameters/FT/v2/EyeLidLeft",
                "OSC address for the left eyelid. Any address ending in this value's last path segment also matches.");
            RightLidAddress = Config.Bind("OSC", "Right Lid Address", "/avatar/parameters/FT/v2/EyeLidRight",
                "OSC address for the right eyelid. Any address ending in this value's last path segment also matches.");
            LidValuesInverted = Config.Bind("OSC", "Lid Values Inverted", false,
                "Set true if the source sends 0 = open, 1 = shut (e.g. Steam Link's LeftEyeLid/RightEyeLid). frameeyeosc sends 0 = shut.");

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
            SimulateHead = Config.Bind("Head Tracking", "Simulate Head", false,
                "Debug: replace the real HMD pose with a simulated one (keyboard IJKL/UO move, arrows rotate, or OSC /bye/head).");

            GazeAiming = Config.Bind("Gaze", "Gaze Aiming", true, "Aim at interactables with your eyes instead of the center of your view.");
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
            MenuPanelScale = Config.Bind("VR", "Menu Panel Scale", 1.5f,
                "How much bigger the menu panel is drawn while a menu is open (spreads the options apart). 1 = UUVR's normal size.");
            FullViewVeil = Config.Bind("VR", "Full View Veil", true,
                "Show the game's eyelids and black fades across your whole view instead of only on the floating UI panel.");
            RenderScale = Config.Bind("VR", "Render Scale", 1f,
                "Renders the game at this multiple of SteamVR's resolution (supersampling): sharper, but heavier on the GPU. 1 = SteamVR's choice, below 1 is blurrier but lighter (0.25 to 2). Also in vr settings ('render quality').");
            MenuRenderScale = Config.Bind("VR", "Menu Render Scale", 1.5f,
                "Render scale in the main menu, whose wide scenery looks low-res at SteamVR's resolution. It's cheap to draw, so it can be higher than Render Scale.");
            Performance = Config.Bind("VR", "Performance Mode", PerformanceMode.Auto,
                "For running the game on the headset itself (Proton on a Steam Frame): Performance MSAA, medium shadows, no ambient occlusion, bloom, grain or FXAA, render quality steps 80/100/125%, and Menu Render Scale ignored. Auto = on under Proton/Wine, off on Windows.");
            PerformanceMsaa = Config.Bind("VR", "Performance MSAA", 2,
                "Anti-aliasing samples (0, 2, 4 or 8) in performance mode. Smooths jagged edges; 2 keeps a steady frame rate on a Steam Frame, 4 costs about 4 ms more per frame.");
            PerformanceHdr = Config.Bind("VR", "Performance HDR", false,
                "Keep HDR rendering in performance mode. Off by default: with MSAA it is the biggest GPU cost on the headset itself (the head tracking shakes when frames are missed).");
            AutoResolution = Config.Bind("VR", "Auto Resolution", AutoResolutionMode.Auto,
                "Lower the render resolution a little in heavy moments so no frame is missed (a missed frame makes the view shake), and raise it again when there's headroom. Auto = on in performance mode; On also helps on PCs with slower GPUs.");
            AutoResolutionMin = Config.Bind("VR", "Auto Resolution Min", 0.6f,
                "The lowest fraction of Render Scale that Auto Resolution may drop to.");
            SmoothUi = Config.Bind("VR", "Smooth UI", true,
                "Filter the floating menu/HUD panel properly (mipmaps) so its text stays crisp in the headset instead of looking jagged and shimmering.");
            VeilHalfFovDegrees = Config.Bind("VR", "Veil Half FOV Degrees", 50f,
                "Half the vertical field of view the eyelids are stretched to (at least the headset's own, read from SteamVR). Lower = lids reach the center sooner.");
            ShowReticle = Config.Bind("VR", "Show Reticle", true,
                "Show the aiming dot during gameplay. Toggle in game with 'Reticle Toggle Key'. The menu cursor is always shown.");
            ReticleToggleKey = Config.Bind("VR", "Reticle Toggle Key", KeyCode.F10, "Show/hide the gameplay aiming dot.");
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
            RecenterKey = Config.Bind("Debug", "Recenter Key", KeyCode.F6, "Make the direction you face straight ahead (and reset head position). Shift + this key toggles Recenter On Scene Start.");

            OscReceiver.Start(OscPort.Value);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            RenderedView.Install();
            InputRedirect.PatchGameInputCalls(harmony);
            HeadTracking.TryPatchUuvr(harmony);
            StereoComfort.TryPatchUuvr(harmony);
            SimHead.Patch(harmony);
            HeadTracking.PatchRecenter(harmony);
            IntroSkip.Init();

            Log.LogInfo("BYE Frame Blink loaded.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(OverlayKey.Value)) OverlayVisible = !OverlayVisible;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(RecenterKey.Value) && shift) ToggleRecenterOnSceneStart();
            else if (Input.GetKeyDown(RecenterKey.Value) || OscReceiver.ConsumeRecenterRequest()) HeadTracking.Recenter();
            if (Input.GetKeyDown(ReticleToggleKey.Value)) ToggleReticle();
            if (Input.GetKeyDown(EyeSeparationKey.Value)) StereoComfort.CycleEyeSeparation();

            DebugCommands.Update();
            SimHead.Update();
            HeadTracking.EnsureSceneHook();
            AutoPlay.Update();
            BlinkModeEnforcer.Update();
            DebugTrace.Update();
            ControllerShortcuts.Update();
            VrSettingsMenu.Update();
            WebcamBlock.HideEmptyFeed();
        }

        internal static void ToggleReticle()
        {
            ShowReticle.Value = !ShowReticle.Value;
            Toast.Show(ShowReticle.Value ? "Aiming dot on" : "Aiming dot off");
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
            EyeTracking.Value = EyeTracking.Value == EyeTrackingMode.Off ? EyeTrackingMode.Auto : EyeTrackingMode.Off;
            Toast.Show(EyeTracking.Value == EyeTrackingMode.Off ? "Eye tracking off: aim with your head" : "Eye tracking: on when available");
        }

        internal static string VignetteLabel =>
            Vignette.Value == VignetteMode.Stretched ? "whole view" : Vignette.Value == VignetteMode.Off ? "off" : "flat";

        private void LateUpdate()
        {
            StereoComfort.LateUpdate();
            VignetteVR.LateUpdate();
            UiSharpness.LateUpdate();
            BYEFrameBlink.Performance.LateUpdate();
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
