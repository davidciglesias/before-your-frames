using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Debug freeze: stops game time, audio and head tracking without opening the pause menu, and treats the eyes as open meanwhile,
    /// so a scene can be inspected (e.g. with UnityExplorer) without blinks moving it on, with the headset off.
    /// </summary>
    internal static class Freeze
    {
        public static bool Active { get; private set; }
        public static Vector3 HeldPosition { get; private set; }
        public static Quaternion HeldRotation { get; private set; } = Quaternion.identity;
        private static float _savedScale = 1f;

        public static void Toggle()
        {
            if (!Active)
            {
                // Hold the head pose too, so the headset can be taken off while inspecting.
                HeldPosition = UnityEngine.XR.InputTracking.GetLocalPosition(UnityEngine.XR.XRNode.Head);
                HeldRotation = UnityEngine.XR.InputTracking.GetLocalRotation(UnityEngine.XR.XRNode.Head);
            }
            Active = !Active;
            if (Active)
            {
                _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else Time.timeScale = _savedScale;
            AudioListener.pause = Active;
            Toast.Show(Active ? "Frozen (eyes treated as open)" : "Unfrozen");
            Plugin.Log.LogInfo("[freeze] " + (Active ? "on" : "off"));
        }

        public static void Update()
        {
            if (Active && Time.timeScale != 0f) Time.timeScale = 0f; // the game may reset it
        }
    }
}
