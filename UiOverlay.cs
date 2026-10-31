using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Access to UUVR's UI overlay camera (UiOverlayRenderMode/UuvrPoseDriver): it renders after the scene with depth
    /// cleared, only sees the VR UI layer, and is rotated by the head. Anything parented to it on that layer is
    /// head-locked and drawn on top of the world in both eyes.
    /// </summary>
    internal static class UiOverlay
    {
        private static Camera _camera;
        private static float _nextSearch;
        private static Sprite _white;

        private static float _headsetHalfFov = -1f;

        /// <summary>
        /// Half the vertical field of view that full-view layers (eyelids, fades, vignette) are mapped to: Veil Half FOV
        /// Degrees, or the headset's own reach if that's bigger. A Steam Frame sees 60 degrees above center (49 below),
        /// so with the 50 degree default the open upper eyelid showed as a black band across the top of the view.
        /// </summary>
        public static float VeilHalfFovDegrees
        {
            get
            {
                if (_headsetHalfFov < 0f) _headsetHalfFov = MeasureHeadsetHalfFov();
                return Mathf.Max(Plugin.VeilHalfFovDegrees.Value, _headsetHalfFov);
            }
        }

        private static float MeasureHeadsetHalfFov()
        {
            try
            {
                Valve.VR.CVRSystem vr = Valve.VR.OpenVR.System;
                if (vr == null) return 0f;
                float most = 0f;
                foreach (Valve.VR.EVREye eye in new[] { Valve.VR.EVREye.Eye_Left, Valve.VR.EVREye.Eye_Right })
                {
                    float l = 0, r = 0, t = 0, b = 0;
                    vr.GetProjectionRaw(eye, ref l, ref r, ref t, ref b);
                    most = Mathf.Max(most, Mathf.Abs(t), Mathf.Abs(b));
                }
                float deg = most > 0f ? Mathf.Atan(most) * Mathf.Rad2Deg + 2f : 0f; // a little margin past the edge
                Plugin.Log.LogInfo($"Headset reaches {deg - 2f:F1} degrees vertically; full-view layers use {Mathf.Max(Plugin.VeilHalfFovDegrees.Value, deg):F1}.");
                return deg;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not read the headset field of view: " + e.Message);
                return 0f;
            }
        }

        public static Camera Camera
        {
            get
            {
                if (_camera != null) return _camera;
                if (Time.unscaledTime < _nextSearch) return null;
                _nextSearch = Time.unscaledTime + 1f;
                foreach (Camera c in Camera.allCameras)
                {
                    Transform p = c.transform.parent;
                    if (p != null && p.name == "UiOverlayRenderMode")
                    {
                        _camera = c;
                        Plugin.Log.LogInfo($"Found UUVR UI overlay camera (layer {Layer}).");
                        break;
                    }
                }
                return _camera;
            }
        }

        /// <summary>The VR UI layer (the overlay camera culls everything else).</summary>
        public static int Layer
        {
            get
            {
                if (_camera == null) return 0;
                int mask = _camera.cullingMask;
                for (int i = 0; i < 32; i++)
                    if ((mask & (1 << i)) != 0) return i;
                return 0;
            }
        }

        public static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply();
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f); // 1x1 world unit
                return _white;
            }
        }

        private static int _ownLayer = -1;

        /// <summary>
        /// Layer for our head-locked sprites. Not the VR UI layer: UUVR's VrUiCaptureCamera also renders that layer into
        /// the UI panel texture, which showed our veils a second time as rectangles on the panel. An unnamed, unused layer
        /// that only the overlay camera renders (see EnforceLayerMasks).
        /// </summary>
        public static int OwnLayer
        {
            get
            {
                if (_ownLayer >= 0) return _ownLayer;
                int ui = Layer;
                for (int i = 30; i >= 8; i--)
                {
                    if (i != ui && string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _ownLayer = i; break; }
                }
                if (_ownLayer < 0) _ownLayer = ui;
                Plugin.Log.LogInfo($"Using layer {_ownLayer} for head-locked overlays (UUVR UI layer is {ui}).");
                return _ownLayer;
            }
        }

        /// <summary>Only the overlay camera may render OwnLayer. Run every frame (UUVR resets masks on setting changes).</summary>
        public static bool EnforceLayerMasks()
        {
            if (_camera == null || _ownLayer < 0) return false;
            int bit = 1 << _ownLayer;
            bool exclusive = true;
            foreach (Camera c in AllCameras())
            {
                if (c == null) continue;
                if (c == _camera) c.cullingMask |= bit;
                else if ((c.cullingMask & bit) != 0) { c.cullingMask &= ~bit; exclusive = false; }
            }
            return exclusive;
        }

        /// <summary>True when no camera other than the overlay camera renders our layer (for tests).</summary>
        public static bool OwnLayerExclusive
        {
            get
            {
                if (_camera == null || _ownLayer < 0) return false;
                int bit = 1 << _ownLayer;
                if ((_camera.cullingMask & bit) == 0) return false;
                foreach (Camera c in AllCameras())
                    if (c != null && c != _camera && (c.cullingMask & bit) != 0) return false;
                return true;
            }
        }

        private static Camera[] _allCameras = new Camera[0];
        private static float _nextCameraScan;

        /// <summary>Includes disabled cameras (UUVR renders its UI capture camera manually).</summary>
        private static Camera[] AllCameras()
        {
            if (Time.unscaledTime >= _nextCameraScan)
            {
                _nextCameraScan = Time.unscaledTime + 1f;
                _allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            }
            return _allCameras;
        }

        /// <summary>Creates a head-locked sprite under the overlay camera. Call only when Camera != null.</summary>
        public static SpriteRenderer CreateSprite(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.layer = OwnLayer;
            EnforceLayerMasks();
            go.transform.SetParent(Camera.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            return sr;
        }
    }
}
