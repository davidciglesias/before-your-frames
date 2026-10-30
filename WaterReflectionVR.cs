using System.Reflection;
using HarmonyLib;
using LPWAsset;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Low Poly Water renders its planar reflection/refraction for the flat camera: once per frame (so the second eye
    /// reuses the first eye's image), with the mono projection, into a 256 px texture. The shader looks the image up by
    /// each eye's screen position, so in a headset it slid with your head and looked blurry.
    /// In VR this replaces that render: every eye renders its own reflection (and refraction) with that eye's view and
    /// projection, at Water Reflection Resolution, right before the eye draws the water.
    /// </summary>
    [HarmonyPatch(typeof(LPWReflection), nameof(LPWReflection.OnWillRenderObject))]
    internal static class WaterReflectionVR
    {
        private static readonly FieldInfo ParamsField = AccessTools.Field(typeof(LPWReflection), "p");
        private static readonly FieldInfo GuardField = AccessTools.Field(typeof(LPWReflection), "recursiveGuard");
        private static readonly MethodInfo CreateWaterObjects = AccessTools.Method(typeof(LPWReflection), "CreateWaterObjects");
        private static readonly MethodInfo UpdateCameraModes = AccessTools.Method(typeof(LPWReflection), "UpdateCameraModes");
        private static readonly MethodInfo GetWaterMode = AccessTools.Method(typeof(LPWReflection), "GetWaterMode");
        private static readonly MethodInfo FindHardwareWaterSupport = AccessTools.Method(typeof(LPWReflection), "FindHardwareWaterSupport");

        private static bool _rendering;

        public static int EyeRenders { get; private set; }

        private static LPWReflection _visibleWater;
        private static Camera _visibleCamera;
        private static readonly System.Collections.Generic.HashSet<Material> VisibleMaterials = new System.Collections.Generic.HashSet<Material>();
        private static int _visibleFrame = -1;
        private static bool _hooked;

        /// <summary>
        /// Called once per frame while culling (for both eyes at once in multi-pass), so it only notes that the water is
        /// visible; the per-eye render happens in Camera.onPreRender, which runs before each eye is drawn.
        /// </summary>
        private static bool Prefix(LPWReflection __instance)
        {
            Camera cam = Camera.current;
            if (!Plugin.VrWaterReflections.Value || Plugin.DisableWaterReflections.Value || cam == null || !cam.stereoEnabled) return true;
            if (_rendering || !__instance.enabled) return false;
            var renderer = __instance.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled || renderer.sharedMaterial == null) return false;
            if (!_hooked)
            {
                Camera.onPreRender += OnEyePreRender;
                _hooked = true;
            }
            if (_visibleFrame != Time.frameCount)
            {
                _visibleFrame = Time.frameCount;
                VisibleMaterials.Clear();
            }
            _visibleWater = __instance;
            _visibleCamera = cam;
            VisibleMaterials.Add(renderer.sharedMaterial);
            return false;
        }

        private static void OnEyePreRender(Camera cam)
        {
            if (_rendering || _visibleWater == null || cam != _visibleCamera || Time.frameCount - _visibleFrame > 1) return;
            if (!Plugin.VrWaterReflections.Value || Plugin.DisableWaterReflections.Value) return;
            Camera.StereoscopicEye eye = cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right
                ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
            RenderEye(_visibleWater, cam, eye);
        }

        private static void RenderEye(LPWReflection __instance, Camera cam, Camera.StereoscopicEye eye)
        {
            var p = (LPWReflectionParams)ParamsField.GetValue(null);
            if (p == null) return;
            Traverse pt = Traverse.Create(p); // its textures and support flags are internal
            _rendering = true;
            GuardField.SetValue(null, true);
            int pixelLights = QualitySettings.pixelLightCount;
            try
            {
                p.textureSize = Mathf.Clamp(Plugin.WaterReflectionResolution.Value, 64, 4096);
                pt.Field("hwSupport").SetValue(FindHardwareWaterSupport.Invoke(__instance, null));
                var mode = (WaterMode)GetWaterMode.Invoke(__instance, null);
                object[] args = { cam, null, null };
                CreateWaterObjects.Invoke(__instance, args);
                var reflCam = (Camera)args[1];
                var refrCam = (Camera)args[2];
                if (p.disablePixelLights) QualitySettings.pixelLightCount = 0;

                // The eye being drawn, from UUVR's child camera (with Eye Separation Scale), and that eye's projection.
                Matrix4x4 view;
                if (!StereoComfort.TryEyeView(cam, eye, out view)) view = cam.worldToCameraMatrix;
                Matrix4x4 proj = cam.GetStereoProjectionMatrix(eye);
                Vector3 pos = __instance.transform.position, up = __instance.transform.up;

                if ((mode & WaterMode.Reflective) == WaterMode.Reflective && reflCam != null)
                {
                    UpdateCameraModes.Invoke(__instance, new object[] { cam, reflCam });
                    var plane = new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, pos) - p.clipPlaneOffset);
                    Matrix4x4 reflView = view * ReflectionMatrix(plane);
                    reflCam.worldToCameraMatrix = reflView;
                    reflCam.projectionMatrix = Oblique(proj, CameraSpacePlane(reflView, pos, up, 1f, p.clipPlaneOffset));
                    reflCam.cullingMask = -17 & p.reflectLayers.value;
                    var reflTex = pt.Field("reflTex").GetValue<RenderTexture>();
                    reflCam.targetTexture = reflTex;
                    PlaceAt(reflCam, reflView);
                    GL.invertCulling = true;
                    reflCam.Render();
                    GL.invertCulling = false;
                    foreach (Material m in VisibleMaterials) if (m != null) m.SetTexture("_ReflectionTex", reflTex);
                }
                if ((mode & WaterMode.Refractive) == WaterMode.Refractive && refrCam != null)
                {
                    UpdateCameraModes.Invoke(__instance, new object[] { cam, refrCam });
                    refrCam.worldToCameraMatrix = view;
                    refrCam.projectionMatrix = Oblique(proj, CameraSpacePlane(view, pos, up, -1f, p.clipPlaneOffset));
                    refrCam.cullingMask = -17 & p.refractLayers.value;
                    var refrTex = pt.Field("refrTex").GetValue<RenderTexture>();
                    refrCam.targetTexture = refrTex;
                    PlaceAt(refrCam, view);
                    refrCam.Render();
                    foreach (Material m in VisibleMaterials) if (m != null) m.SetTexture("_RefractionTex", refrTex);
                }
                EyeRenders++;
            }
            finally
            {
                QualitySettings.pixelLightCount = pixelLights;
                GuardField.SetValue(null, false);
                _rendering = false;
            }

        }

        /// <summary>Moves a helper camera's transform to match a view matrix (used for culling and the skybox).</summary>
        private static void PlaceAt(Camera c, Matrix4x4 worldToCamera)
        {
            Matrix4x4 camToWorld = worldToCamera.inverse;
            c.transform.position = camToWorld.MultiplyPoint(Vector3.zero);
            // Unity view space looks down -Z.
            c.transform.rotation = Quaternion.LookRotation(-camToWorld.GetColumn(2), camToWorld.GetColumn(1));
        }

        private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float sideSign, float offset)
        {
            Vector3 p = worldToCamera.MultiplyPoint(pos + normal * offset);
            Vector3 n = worldToCamera.MultiplyVector(normal).normalized * sideSign;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
        }

        /// <summary>Projection with its near plane replaced by a clip plane (Lengyel's oblique frustum).</summary>
        private static Matrix4x4 Oblique(Matrix4x4 proj, Vector4 clipPlane)
        {
            Vector4 q = proj.inverse * new Vector4(Mathf.Sign(clipPlane.x), Mathf.Sign(clipPlane.y), 1f, 1f);
            Vector4 c = clipPlane * (2f / Vector4.Dot(clipPlane, q));
            proj[2] = c.x - proj[3];
            proj[6] = c.y - proj[7];
            proj[10] = c.z - proj[11];
            proj[14] = c.w - proj[15];
            return proj;
        }

        private static Matrix4x4 ReflectionMatrix(Vector4 plane)
        {
            var m = new Matrix4x4();
            m.m00 = 1f - 2f * plane[0] * plane[0];
            m.m01 = -2f * plane[0] * plane[1];
            m.m02 = -2f * plane[0] * plane[2];
            m.m03 = -2f * plane[3] * plane[0];
            m.m10 = -2f * plane[1] * plane[0];
            m.m11 = 1f - 2f * plane[1] * plane[1];
            m.m12 = -2f * plane[1] * plane[2];
            m.m13 = -2f * plane[3] * plane[1];
            m.m20 = -2f * plane[2] * plane[0];
            m.m21 = -2f * plane[2] * plane[1];
            m.m22 = 1f - 2f * plane[2] * plane[2];
            m.m23 = -2f * plane[3] * plane[2];
            m.m33 = 1f;
            return m;
        }
    }
}
