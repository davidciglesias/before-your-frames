using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    [BepInPlugin(Guid, "BYE Frame Blink", "0.1.0")]
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

        // Gaze
        internal static ConfigEntry<bool> GazeAiming;
        internal static ConfigEntry<float> GazeDegreesPerUnit;
        internal static ConfigEntry<float> GazeSmoothingSeconds;
        internal static ConfigEntry<float> GazeHoldAfterBlink;
        internal static ConfigEntry<bool> GazeInvertX;
        internal static ConfigEntry<bool> GazeInvertY;
        internal static ConfigEntry<float> GazeOffsetYaw;
        internal static ConfigEntry<float> GazeOffsetPitch;
        internal static ConfigEntry<bool> GazeMoveReticle;
        internal static ConfigEntry<float> AimAssistDegrees;

        // VR presentation
        internal static ConfigEntry<bool> FullViewVeil;
        internal static ConfigEntry<bool> BlinkMenus;
        internal static ConfigEntry<bool> SkipIntro;
        internal static ConfigEntry<bool> IntroSeen;
        internal static ConfigEntry<float> MenuBlinkSeconds;
        internal static ConfigEntry<float> VeilHalfFovDegrees;

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
            SimulateHead = Config.Bind("Head Tracking", "Simulate Head", false,
                "Debug: replace the real HMD pose with a simulated one (keyboard IJKL/UO move, arrows rotate, or OSC /bye/head).");

            GazeAiming = Config.Bind("Gaze", "Gaze Aiming", true, "Aim at interactables with your eyes instead of the center of your view.");
            GazeDegreesPerUnit = Config.Bind("Gaze", "Degrees Per Unit", 45f, "Gaze angle for an OSC value of 1.0 (frameeyeosc/VRCFT: 45).");
            GazeSmoothingSeconds = Config.Bind("Gaze", "Smoothing Seconds", 0.08f, "Time constant of the gaze smoothing filter. Higher = steadier but laggier.");
            GazeHoldAfterBlink = Config.Bind("Gaze", "Hold After Blink Seconds", 0.2f, "Keep the last gaze direction this long after the eyes reopen.");
            GazeInvertX = Config.Bind("Gaze", "Invert X", false, "Flip horizontal gaze.");
            GazeInvertY = Config.Bind("Gaze", "Invert Y", false, "Flip vertical gaze.");
            GazeOffsetYaw = Config.Bind("Gaze", "Offset Yaw", 0f, "Degrees added to horizontal gaze (+ right) to correct a constant aim error.");
            GazeOffsetPitch = Config.Bind("Gaze", "Offset Pitch", 0f, "Degrees added to vertical gaze (+ up) to correct a constant aim error.");
            GazeMoveReticle = Config.Bind("Gaze", "Move Reticle", true, "Draw the game's reticle where you're aiming (head + gaze) instead of on the fixed UI panel.");
            AimAssistDegrees = Config.Bind("Gaze", "Aim Assist Degrees", 3f,
                "If the aim ray misses, also pick interactables within this cone (degrees). Bigger hitbox, no visual change. 0 = off.");

            SkipIntro = Config.Bind("VR", "Skip Intro Logos", true,
                "Go straight to the main menu at startup (skips the logo splashes and the EULA notice) once the intro has been seen.");
            IntroSeen = Config.Bind("VR", "Intro Seen", false,
                "Set automatically the first time the main menu is reached. Set false to see the intro once more.");
            BlinkMenus = Config.Bind("VR", "Blink Menus", true,
                "Use the menus with your eyes: look at a button (head + gaze) and do a deliberate blink to press it.");
            MenuBlinkSeconds = Config.Bind("VR", "Menu Blink Seconds", 0.3f,
                "How long the eyes must stay closed for a blink to press a menu button (natural blinks are ~0.1-0.15 s).");
            FullViewVeil = Config.Bind("VR", "Full View Veil", true,
                "Show the game's eyelids and black fades across your whole view instead of only on the floating UI panel.");
            VeilHalfFovDegrees = Config.Bind("VR", "Veil Half FOV Degrees", 50f,
                "Half the vertical field of view the eyelids are stretched to. Lower = lids reach the center sooner.");

            TestMode = Config.Bind("Debug", "Test Mode", false,
                "Ignore OSC from other machines (the headset); only localhost test input is used. Set by tools/run_tests.ps1 while it runs.");
            TraceCsv = Config.Bind("Debug", "Trace Csv", false, "Write a per-frame CSV trace to BepInEx/BYEFrameBlink_trace.csv.");
            OverlayKey = Config.Bind("Debug", "Overlay Key", KeyCode.F8, "Toggle the debug overlay.");
            ForceBlinkKey = Config.Bind("Debug", "Force Blink Key", KeyCode.F9, "Hold to simulate closed eyes.");
            RecenterKey = Config.Bind("Debug", "Recenter Key", KeyCode.F6, "Recenter positional tracking.");

            OscReceiver.Start(OscPort.Value);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            InputRedirect.PatchGameInputCalls(harmony);
            HeadTracking.TryPatchUuvr(harmony);
            SimHead.Patch(harmony);
            IntroSkip.Init();

            Log.LogInfo("BYE Frame Blink loaded.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(OverlayKey.Value)) OverlayVisible = !OverlayVisible;
            if (Input.GetKeyDown(RecenterKey.Value) || OscReceiver.ConsumeRecenterRequest()) HeadTracking.Recenter();

            DebugCommands.Update();
            SimHead.Update();
            AutoPlay.Update();
            BlinkModeEnforcer.Update();
            DebugTrace.Update();
        }

        private void LateUpdate()
        {
            ReticleFollower.LateUpdate();
            Veil.LateUpdate();
            MenuGaze.LateUpdate();
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
}
