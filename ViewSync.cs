using System.Collections.Generic;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// UUVR's RelativeMatrix mode draws each eye by swapping the camera's view matrix, but leaves the camera's transform
    /// where the game put it. Anything Unity derives from the transform at render time then disagrees with what you see:
    /// particle billboards (the opening scene's fog turned with your head), and shader inputs like the camera position
    /// and orientation (a night-tint band that followed your head). Child mode avoids that but drops the game camera's
    /// effects. Instead, only while a camera renders, move its transform onto the head pose (counter-moving UUVR's
    /// VrCameraOffset so the head camera, and so the view matrix, stay exactly put), and restore it right after.
    /// Game code (Update/LateUpdate) never sees the change.
    /// </summary>
    internal static class ViewSync
    {
        private struct Saved
        {
            public Transform Cam, Offset;
            public Vector3 CamPos, OffPos;
            public Quaternion CamRot, OffRot;
        }

        private static readonly Dictionary<Camera, Saved> Active = new Dictionary<Camera, Saved>();
        private static bool _installed;
        public static int AppliedFrame { get; private set; } = -1;
        public static bool LogNext;

        /// <summary>Debug (bye_sim viewsync): log how far the camera transform is from the view actually drawn, for one left-eye render.</summary>
        public static void Check(Camera c)
        {
            if (!LogNext || c != Camera.main || c.stereoActiveEye != Camera.MonoOrStereoscopicEye.Left) return;
            LogNext = false;
            Matrix4x4 toWorld = c.worldToCameraMatrix.inverse;
            Vector3 viewFwd = -(Vector3)toWorld.GetColumn(2), viewPos = toWorld.GetColumn(3);
            Plugin.Log.LogInfo($"[cmd] viewsync on={Plugin.ViewSyncEnabled.Value} applied={Active.ContainsKey(c)} transform-vs-view angle={Vector3.Angle(c.transform.forward, viewFwd):F2} pos={Vector3.Distance(c.transform.position, viewPos):F3} viewFwd={viewFwd:F3}");
        }

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Camera.onPreCull += OnPreCull;
            Camera.onPostRender += OnPostRender;
            Camera.onPreRender += Check;
        }

        private static void OnPreCull(Camera c)
        {
            if (!Plugin.ViewSyncEnabled.Value || !UnityEngine.XR.XRSettings.enabled || Active.ContainsKey(c)) return;
            if (c != Camera.main && c.name != "UI Camera") return;
            Transform offset = c.transform.Find("VrCameraOffset");
            Transform head = offset != null ? offset.Find("VrChildCamera") : null;
            if (head == null || !head.gameObject.activeInHierarchy) return;
            Camera headCam = head.GetComponent<Camera>();
            if (headCam == null || headCam.cullingMask != 0) return; // Child mode: the head camera draws, nothing to do

            Transform t = c.transform;
            var s = new Saved
            {
                Cam = t, Offset = offset,
                CamPos = t.localPosition, CamRot = t.localRotation,
                OffPos = offset.localPosition, OffRot = offset.localRotation,
            };
            Vector3 headPos = head.position;
            Quaternion headRot = head.rotation;
            Matrix4x4 headLocal = Matrix4x4.TRS(head.localPosition, head.localRotation, Vector3.one); // head in offset space

            t.SetPositionAndRotation(headPos, headRot);
            // Keep the head where it was: offset world = headWorld * inverse(headLocal).
            Matrix4x4 offWorld = Matrix4x4.TRS(headPos, headRot, Vector3.one) * headLocal.inverse;
            offset.SetPositionAndRotation(offWorld.GetColumn(3), offWorld.rotation);
            Active[c] = s;
            AppliedFrame = Time.frameCount;
        }

        private static void OnPostRender(Camera c)
        {
            if (!Active.TryGetValue(c, out Saved s)) return;
            Active.Remove(c);
            if (s.Cam == null || s.Offset == null) return;
            s.Offset.localPosition = s.OffPos;
            s.Offset.localRotation = s.OffRot;
            s.Cam.localPosition = s.CamPos;
            s.Cam.localRotation = s.CamRot;
        }
    }
}
