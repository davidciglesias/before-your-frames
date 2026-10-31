using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// The orientation each camera was last rendered with, read from its view matrix just before rendering (UUVR's
    /// RelativeMatrix mode sets it there from the headset pose). Aiming from this keeps the aim ray on what's drawn
    /// even when the tracking pose read during Update differs from the render pose: on a Steam Frame running the game
    /// itself they're about 7 degrees apart. Also keeps the left eye's matrices of the world and overlay cameras, so a
    /// debug capture can redraw exactly what that eye saw.
    /// </summary>
    internal static class RenderedView
    {
        private static bool _installed;
        private static Camera _cam;
        private static Quaternion _rotation;
        private static int _frame = -10;

        public static Matrix4x4 MainView, MainProj, OverlayView, OverlayProj;
        public static bool HasMain, HasOverlay;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Camera.onPreRender += OnPreRender;
        }

        private static void OnPreRender(Camera c)
        {
            if (!UnityEngine.XR.XRSettings.enabled) return;
            bool left = c.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left;
            if (c == UiOverlay.Camera && left)
            {
                OverlayView = c.worldToCameraMatrix;
                OverlayProj = c.projectionMatrix;
                HasOverlay = true;
                return;
            }
            if (c != Camera.main) return;
            if (left)
            {
                MainView = c.worldToCameraMatrix;
                MainProj = c.projectionMatrix;
                HasMain = true;
            }
            Matrix4x4 toWorld = c.worldToCameraMatrix.inverse;
            Vector3 forward = -toWorld.GetColumn(2); // view space looks down -z
            Vector3 up = toWorld.GetColumn(1);
            if (forward.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return;
            _cam = c;
            _rotation = Quaternion.LookRotation(forward, up);
            _frame = Time.frameCount;
        }

        /// <summary>The rendered orientation of this camera from the last frame or two, if there is one.</summary>
        public static bool TryGet(Camera cam, out Quaternion rotation)
        {
            rotation = _rotation;
            return Plugin.AimFromRenderedView.Value && cam == _cam && Time.frameCount - _frame <= 2;
        }
    }
}
