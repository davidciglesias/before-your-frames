using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Keeps the game from turning the webcam on. Blinks come from the headset, so it's never needed.
    /// - UlseeBlinkDetect_v2.InitializeTrackerAndCheckKeyNow opens the camera natively (ULS_UnityTrackerInit). Skipping it
    ///   is safe: UpdateEyes bails out until the tracker's preview callback sets initDone, which then never happens.
    /// - WebcamAvStim / WebcamTextureStarter(Selectable) call WebCamTexture.Play(); those calls are IL-redirected here.
    /// </summary>
    [HarmonyPatch(typeof(UlseeBlinkDetect_v2), "InitializeTrackerAndCheckKeyNow")]
    internal static class WebcamBlock
    {
        public static int TrackerInitsBlocked;
        public static int PlaysBlocked;
        public static int DisconnectWatchersSkipped;

        private static bool Prefix()
        {
            if (!Plugin.BlockWebcam.Value) return true;
            TrackerInitsBlocked++;
            Plugin.Log.LogInfo("Blocked the webcam face tracker from starting (Block Webcam = true).");
            return false;
        }

        /// <summary>Replacement for WebCamTexture.Play() in game code.</summary>
        public static void Play(WebCamTexture texture)
        {
            if (!Plugin.BlockWebcam.Value)
            {
                texture.Play();
                return;
            }
            PlaysBlocked++;
            if (PlaysBlocked == 1) Plugin.Log.LogInfo("Blocked WebCamTexture.Play() (Block Webcam = true).");
        }

        /// <summary>
        /// CheckCamConnection fires CamNotConnected when WebCamTexture.devices is empty, which shows the game's
        /// "No Webcam Detected" warning on PCs without a webcam. Blinks come from the headset, so report it as connected.
        /// </summary>
        [HarmonyPatch(typeof(CheckCamConnection), "OnEnable")]
        private static class NoCamWarning
        {
            private static bool Prefix(CheckCamConnection __instance)
            {
                if (!Plugin.BlockWebcam.Value) return true;
                Plugin.Log.LogInfo($"Skipped the webcam connection check on '{__instance.name}' (Block Webcam = true).");
                __instance.CamConnected?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// UlseeBlinkDetect_v2.Start runs a coroutine that checks every 2.5 s whether its webcam is still plugged in and
        /// otherwise shows "Webcam detection lost. Please exit the game..." (and pauses the tracker). With the webcam
        /// blocked there is no camera to lose, so never start it.
        /// </summary>
        [HarmonyPatch(typeof(UlseeBlinkDetect_v2), "WatchForWebcamDisconnect1")]
        private static class NoDisconnectWatch
        {
            private static bool Prefix(ref System.Collections.IEnumerator __result)
            {
                if (!Plugin.BlockWebcam.Value) return true;
                __result = Enumerable.Empty<object>().GetEnumerator();
                DisconnectWatchersSkipped++;
                Plugin.Log.LogInfo("Skipped the webcam disconnect watcher (Block Webcam = true).");
                return false;
            }
        }

        /// <summary>With no webcam running, the game's on-screen webcam preview is an empty blue box in the corner.</summary>
        public static void HideEmptyFeed()
        {
            if (!Plugin.BlockWebcam.Value) return;
            PauseMenuController pmc = PauseMenuController.instance;
            if (pmc != null && pmc.webcamFeed != null && pmc.webcamFeed.activeSelf) pmc.webcamFeed.SetActive(false);
        }

        public static bool FeedShown =>
            PauseMenuController.instance != null && PauseMenuController.instance.webcamFeed != null && PauseMenuController.instance.webcamFeed.activeInHierarchy;

        public static int PlayingWebcamTextures()
        {
            int n = 0;
            foreach (WebCamTexture t in Resources.FindObjectsOfTypeAll<WebCamTexture>())
                if (t.isPlaying) n++;
            return n;
        }
    }
}
