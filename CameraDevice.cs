using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// The in-game camera device (InteractableCamera, its viewfinder and the camera that takes the photo) parents itself
    /// to Camera.main at a fixed pose. In VR the game camera doesn't turn with your head, so the device stayed where it
    /// first appeared and pointed away from what you aim at. Each frame, just before rendering, put it on the view you
    /// see: game camera * head pose * eye gaze. Also scale it up so it sits farther away at the same apparent size
    /// (at the game's distance it is too close to focus on in a headset).
    /// </summary>
    internal static class CameraDevice
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Application.onBeforeRender += Place;
        }

        private static void Place()
        {
            if (!UnityEngine.XR.XRSettings.enabled || !Plugin.CameraDeviceFollow.Value) return;
            InteractableCamera dev = Object.FindObjectOfType<InteractableCamera>();
            if (dev == null) return;
            Camera main = Camera.main;
            if (main == null) return;
            Transform t = dev.transform;
            if (t.parent != main.transform) return; // not (yet) attached to the view as the game does it

            Transform head = main.transform.Find("VrCameraOffset/VrChildCamera");
            Quaternion headRot = head != null && head.parent != null
                ? Quaternion.Inverse(main.transform.rotation) * head.parent.rotation * HeadTracking.ReadHeadRotation()
                : HeadTracking.ReadHeadRotation();
            Vector3 headPos = head != null ? main.transform.InverseTransformPoint(head.position) : Vector3.zero;
            Quaternion gaze = Plugin.CameraDeviceFollowGaze.Value ? Gaze.LocalRotation : Quaternion.identity;

            t.localPosition = headPos;
            t.localRotation = headRot * gaze;
            // Prefer the aim ray itself (the reticle's ray, built from the pose actually rendered): the tracking pose
            // read here can sit several degrees off it on a Steam Frame, which put the viewfinder well beside the dot.
            if (Plugin.CameraDeviceFollowGaze.Value && Gaze.LastCamera == main && Gaze.LastGazeRay.direction.sqrMagnitude > 0.5f)
            {
                Vector3 up = main.transform.rotation * headRot * Vector3.up;
                t.rotation = Quaternion.LookRotation(Gaze.LastGazeRay.direction, up);
            }
            Sharpen(dev.GetComponent<Camera>());
            SpriteUpscaler.Apply(t);
            float d = Mathf.Clamp(Plugin.CameraDeviceDistance.Value, 0.5f, 10f), s = Mathf.Clamp(Plugin.CameraDeviceSize.Value, 0.5f, 4f);
            t.localScale = new Vector3(d * s, d * s, d);
        }

        /// <summary>
        /// The viewfinder shows a 960x540 render texture, sized for a corner of a flat screen; drawn across much of a
        /// headset's view it is very blurry. Re-create that texture (the material keeps referencing the same object) wider.
        /// </summary>
        private static void Sharpen(Camera cam)
        {
            RenderTexture rt = cam != null ? cam.targetTexture : null;
            int w = Plugin.CameraDeviceResolution.Value;
            if (rt == null || w <= 0 || rt.width >= w) return;
            int h = Mathf.RoundToInt(w * (float)rt.height / rt.width);
            Plugin.Log.LogInfo($"[camdevice] viewfinder texture {rt.width}x{rt.height} -> {w}x{h}");
            cam.targetTexture = null;
            rt.Release();
            rt.width = w;
            rt.height = h;
            rt.antiAliasing = Mathf.Max(rt.antiAliasing, 2);
            rt.Create();
            cam.targetTexture = rt;
        }
    }
}
