using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Eye-gaze direction relative to the head, from frameeyeosc's EyeLeftX/Y + EyeRightX/Y. Smoothed, and frozen while
    /// the eyes are closed (and briefly after), since gaze readings are garbage during a blink.
    /// </summary>
    internal static class Gaze
    {
        private static int _lastFrame = -1;
        private static Vector2 _smoothed;      // degrees: x = yaw (+right), y = pitch (+up)
        private static float _frozenUntil;
        private static bool _hasValue;

        public static bool Active { get; private set; }
        public static Vector2 Degrees => _smoothed;

        /// <summary>Rotation from head-forward to gaze direction, or identity when gaze is off/unavailable.</summary>
        public static Quaternion LocalRotation
        {
            get
            {
                Sample();
                return Active ? Quaternion.Euler(-_smoothed.y, _smoothed.x, 0f) : Quaternion.identity;
            }
        }

        private static void Sample()
        {
            int frame = Time.frameCount;
            if (frame == _lastFrame) return;
            _lastFrame = frame;

            bool enabled = Plugin.GazeAiming.Value;
            bool fresh = OscReceiver.SecondsSinceGazeData < Plugin.DataTimeoutSeconds.Value;
            if (!enabled || !fresh)
            {
                Active = false;
                _hasValue = false;
                return;
            }
            Active = true;

            float now = Time.realtimeSinceStartup;
            if (BlinkState.RawClosed || BlinkState.Closed)
            {
                _frozenUntil = now + Plugin.GazeHoldAfterBlink.Value;
                if (_hasValue) return;
            }
            if (now < _frozenUntil && _hasValue) return;

            OscReceiver.GetGaze(out float x, out float y);
            float scale = Plugin.GazeDegreesPerUnit.Value;
            var target = new Vector2(
                x * scale * (Plugin.GazeInvertX.Value ? -1f : 1f) + Plugin.GazeOffsetYaw.Value,
                y * scale * (Plugin.GazeInvertY.Value ? -1f : 1f) + Plugin.GazeOffsetPitch.Value);

            if (!_hasValue)
            {
                _smoothed = target;
                _hasValue = true;
                return;
            }

            float tau = Plugin.GazeSmoothingSeconds.Value;
            float k = tau <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau);
            _smoothed = Vector2.Lerp(_smoothed, target, k);
        }

        /// <summary>
        /// Replacement for Camera.ViewportPointToRay in game code. Center-of-view rays (the game's "what am I looking at"
        /// check) are rebuilt from the pose the headset actually renders: camera base * head rotation * eye gaze.
        /// Unity's own ray ignores the VR head here (UUVR's RelativeMatrix mode only swaps render matrices, and the
        /// ray comes out along the camera transform), so without this the game aimed where its camera faced, not where
        /// you look. Everything else passes through unchanged.
        /// </summary>
        public static Ray ViewportPointToRay(Camera cam, Vector3 pos)
        {
            Ray ray = cam.ViewportPointToRay(pos);
            if (Mathf.Abs(pos.x - 0.5f) > 0.001f || Mathf.Abs(pos.y - 0.5f) > 0.001f) return ray;

            Quaternion gaze = LocalRotation;
            Transform head = UnityEngine.XR.XRSettings.enabled ? FindHead(cam) : null;
            if (head != null && head.parent != null)
            {
                // VrChildCamera sits under VrCameraOffset (identity offset from the game camera, or horizon-aligned).
                Quaternion baseRot = head.parent.rotation;
                Vector3 origin = head.parent.TransformPoint(head.localPosition);
                LastGazeRay = new Ray(origin, baseRot * HeadTracking.ReadHeadRotation() * gaze * Vector3.forward);
            }
            else
            {
                if (!Active) return ray;
                Vector3 up = cam.cameraToWorldMatrix.MultiplyVector(Vector3.up);
                LastGazeRay = new Ray(ray.origin, Quaternion.LookRotation(ray.direction, up) * gaze * Vector3.forward);
            }
            LastCamera = cam;
            return LastGazeRay;
        }

        /// <summary>
        /// Replacement for Camera.ScreenToWorldPoint in game code. The screen center (the connect-the-dots line follows
        /// it) becomes the point at that depth along the aim ray; everything else passes through unchanged.
        /// </summary>
        public static Vector3 ScreenToWorldPoint(Camera cam, Vector3 pos)
        {
            if (Mathf.Abs(pos.x - Screen.width / 2) > 1.01f || Mathf.Abs(pos.y - Screen.height / 2) > 1.01f)
                return cam.ScreenToWorldPoint(pos);
            Ray ray = ViewportPointToRay(cam, new Vector3(0.5f, 0.5f, 0f));
            return ray.origin + ray.direction * pos.z;
        }

        private static Camera _headCam;
        private static Transform _head;

        /// <summary>UUVR's VrChildCamera for this camera (VrCameraOffset/VrChildCamera under it), cached.</summary>
        private static Transform FindHead(Camera cam)
        {
            if (cam == _headCam && _head != null) return _head;
            _headCam = cam;
            _head = cam.transform.Find("VrCameraOffset/VrChildCamera");
            return _head;
        }

        public static Ray LastGazeRay { get; private set; }
        public static Camera LastCamera { get; private set; }
    }
}
