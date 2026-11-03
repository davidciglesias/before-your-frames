using System;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Levels the VR view when the game's own camera is pitched or rolled. The game frames some scenes with the camera
    /// tilted (e.g. pitched 8-15 degrees down); in VR that tilts the whole world's horizon. UUVR's "Align To Horizon"
    /// removes all pitch and roll, which would leave scenes framed far up or down needing a stiff neck. Here roll is
    /// always removed and pitch is removed up to a limit, fading back to the game's framing by twice that limit, so a
    /// slightly tilted camera reads level and a camera aimed at the ceiling or floor still looks there.
    /// Done in a postfix on UUVR's VrCameraOffset.UpdateTransform: the offset sits between the game camera and the head
    /// cameras, so tracking, gaze and ViewSync all see the leveled pose.
    /// </summary>
    internal static class HorizonLevel
    {
        public static bool Patched { get; private set; }

        public static void TryPatchUuvr(Harmony harmony)
        {
            Type offset = AccessTools.TypeByName("Uuvr.VrCamera.VrCameraOffset");
            var method = offset == null ? null : AccessTools.Method(offset, "UpdateTransform");
            if (method == null)
            {
                Plugin.Log.LogWarning("UUVR's VrCameraOffset.UpdateTransform not found; horizon leveling disabled.");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(typeof(HorizonLevel), nameof(UpdateTransformPostfix))));
            Patched = true;
        }

        private static void UpdateTransformPostfix(MonoBehaviour __instance)
        {
            if (Plugin.LevelHorizon.Value == LevelHorizonMode.Off) return;
            Transform t = __instance.transform;
            Transform parent = t.parent;
            if (parent == null) return;
            t.localRotation = Quaternion.Inverse(parent.rotation) * Leveled(parent.rotation);
        }

        /// <summary>World rotation the view should have for a game camera at <paramref name="cam"/>.</summary>
        internal static Quaternion Leveled(Quaternion cam)
        {
            Vector3 e = cam.eulerAngles; // Unity: roll (z), then pitch (x), then yaw (y)
            float pitch = Mathf.DeltaAngle(0f, e.x);
            if (Plugin.LevelHorizon.Value == LevelHorizonMode.RollAndPitch)
                pitch = KeptPitch(pitch, Plugin.LevelPitchDegrees.Value);
            return Quaternion.Euler(pitch, e.y, 0f);
        }

        /// <summary>Pitch left after leveling: 0 up to the limit, then back to the full pitch by twice the limit.</summary>
        internal static float KeptPitch(float pitch, float limit)
        {
            float a = Mathf.Abs(pitch);
            if (limit <= 0f || a >= 2f * limit) return pitch;
            return a <= limit ? 0f : Mathf.Sign(pitch) * 2f * (a - limit);
        }

        public static string Describe()
        {
            Camera cam = Camera.main;
            if (cam == null) return "no main camera";
            Vector3 e = cam.transform.eulerAngles;
            Vector3 v = Leveled(cam.transform.rotation).eulerAngles;
            Transform off = cam.transform.Find("VrCameraOffset");
            string actual = off == null ? "no offset" : $"offset world pitch={-Mathf.DeltaAngle(0f, off.eulerAngles.x):F1} roll={Mathf.DeltaAngle(0f, off.eulerAngles.z):F1}";
            return $"mode={Plugin.LevelHorizon.Value} limit={Plugin.LevelPitchDegrees.Value:F0} patched={Patched} " +
                   $"game pitch={-Mathf.DeltaAngle(0f, e.x):F1} roll={Mathf.DeltaAngle(0f, e.z):F1} -> view pitch={-Mathf.DeltaAngle(0f, v.x):F1} roll={Mathf.DeltaAngle(0f, v.z):F1}, {actual}";
        }
    }

    public enum LevelHorizonMode
    {
        Off,
        Roll,
        RollAndPitch,
    }
}
