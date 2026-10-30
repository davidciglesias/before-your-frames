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

        public static int PlayingWebcamTextures()
        {
            int n = 0;
            foreach (WebCamTexture t in Resources.FindObjectsOfTypeAll<WebCamTexture>())
                if (t.isPlaying) n++;
            return n;
        }
    }
}
