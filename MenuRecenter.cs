using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace BYEFrameBlink
{
    /// <summary>
    /// Recentering happened only when a gameplay scene began, so the main menu came up wherever SteamVR's tracking
    /// "forward" is, at an angle when you started the game facing elsewhere. Recenter when the menu appears too, but
    /// only once the headset reports a real pose: at game start it can still read identity, so recentering right away
    /// did nothing.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuController), "Start")]
    internal static class MenuRecenter
    {
        private static bool _pending;

        private static void Postfix()
        {
            if (!Plugin.RecenterOnSceneStart.Value) return;
            Plugin.Log.LogInfo("[menu] main menu started; recenter once the headset is tracking");
            _pending = true;
        }

        public static void Update()
        {
            if (!_pending) return;
            if (BlinkCounter.instance != null) { _pending = false; return; } // in gameplay: scene start recenters
            if (!XRSettings.enabled) return;
            if (SimHead.Active) { _pending = false; HeadTracking.Recenter(quiet: true); return; }
            Quaternion rot = InputTracking.GetLocalRotation(XRNode.Head);
            Vector3 pos = InputTracking.GetLocalPosition(XRNode.Head);
            bool tracking = Quaternion.Angle(rot, Quaternion.identity) > 0.01f || pos.sqrMagnitude > 1e-6f;
            if (!tracking || XRDevice.userPresence == UserPresenceState.NotPresent) return;
            _pending = false;
            HeadTracking.Recenter(quiet: true);
            Plugin.Log.LogInfo("[menu] recentered once the headset was tracking");
        }
    }
}
