using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BYEFrameBlink
{
    /// <summary>
    /// Things that make the flat game's camera uncomfortable to look through in a headset.
    /// - Eye separation: many scenes frame things a few centimetres from the camera, closer than eyes can converge on.
    ///   UUVR's RelativeMatrix mode sets the game camera's view matrix per eye from its child camera's stereo matrices;
    ///   we pull each eye's matrix toward the midpoint of the two, which shrinks the stereo baseline (the world looks
    ///   bigger and close objects farther away). Both eyes share a rotation, so the element-wise blend is exact.
    /// - Depth of field: the game blurs everything but its focus point; in VR your eyes do their own focusing.
    /// - Squint: when a scene-ending blink is ready the game raises the eyelids a little, which the full-view veil shows
    ///   as a grey band at the edge of the view.
    /// </summary>
    internal static class StereoComfort
    {
        private static readonly float[] Presets = { 1f, 0.8f, 0.6f, 0.4f };
        private static readonly string[] PresetLabels = { "real", "a bit bigger", "bigger (default)", "much bigger" };

        private static AccessTools.FieldRef<object, Camera> _childCamera;
        private static Func<object, Camera> _parentCamera;
        private static Type _vrCameraType;
        public static bool Patched { get; private set; }

        public static void TryPatchUuvr(Harmony harmony)
        {
            Type vrCamera = AccessTools.TypeByName("Uuvr.VrCamera.VrCamera");
            var method = vrCamera == null ? null : AccessTools.Method(vrCamera, "UpdateRelativeMatrix");
            var parent = vrCamera == null ? null : AccessTools.PropertyGetter(vrCamera, "ParentCamera");
            if (method == null || parent == null || AccessTools.Field(vrCamera, "_childCamera") == null)
            {
                Plugin.Log.LogWarning("UUVR's VrCamera.UpdateRelativeMatrix not found; Eye Separation Scale disabled.");
                return;
            }
            _childCamera = AccessTools.FieldRefAccess<Camera>(vrCamera, "_childCamera");
            _vrCameraType = vrCamera;
            _parentCamera = o => (Camera)parent.Invoke(o, null);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(StereoComfort), nameof(UpdateRelativeMatrixPostfix)));
            Patched = true;
        }


        /// <summary>
        /// The view matrix the headset uses for one eye of a UUVR VR camera (incl. Eye Separation Scale), for effects
        /// that render their own helper views. False if the camera isn't driven by UUVR's RelativeMatrix child camera.
        /// </summary>
        public static bool TryEyeView(Camera parent, Camera.StereoscopicEye eye, out Matrix4x4 view)
        {
            view = Matrix4x4.identity;
            if (_vrCameraType == null || parent == null) return false;
            Component vr = parent.GetComponent(_vrCameraType);
            Camera child = vr != null ? _childCamera(vr) : null;
            if (child == null) return false;
            Matrix4x4 left = child.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 right = child.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            Matrix4x4 own = eye == Camera.StereoscopicEye.Left ? left : right;
            float k = Mathf.Clamp(Plugin.EyeSeparationScale.Value, 0.05f, 2f);
            for (int i = 0; i < 16; i++)
            {
                float center = (left[i] + right[i]) * 0.5f;
                view[i] = center + (own[i] - center) * k;
            }
            return true;
        }
        private static Matrix4x4 _mainLeft, _mainRight;
        private static int _mainLeftFrame = -10, _mainRightFrame = -10;

        private static void UpdateRelativeMatrixPostfix(object __instance)
        {
            ApplyEyeSeparation(__instance);
            SyncUiCamera(__instance);
        }

        /// <summary>
        /// The game's UI Camera (it draws e.g. the camera device's viewfinder) gets its own head pose from UUVR, which on
        /// a Steam Frame lands several degrees off the main camera's, so its overlays sat beside what they frame. Draw it
        /// with exactly the main camera's view for the same eye (the main camera renders first: lower depth).
        /// </summary>
        private static void SyncUiCamera(object instance)
        {
            Camera parent = _parentCamera(instance);
            Camera main = Camera.main;
            if (parent == null || main == null || !parent.stereoEnabled) return;
            bool left = parent.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left;
            if (parent == main)
            {
                if (left) { _mainLeft = main.worldToCameraMatrix; _mainLeftFrame = Time.frameCount; }
                else { _mainRight = main.worldToCameraMatrix; _mainRightFrame = Time.frameCount; }
                return;
            }
            if (!Plugin.SyncUiCamera.Value || parent.name != "UI Camera" || parent.transform.parent != main.transform) return;
            if ((left ? _mainLeftFrame : _mainRightFrame) != Time.frameCount) return;
            parent.worldToCameraMatrix = left ? _mainLeft : _mainRight;
        }

        private static void ApplyEyeSeparation(object __instance)
        {
            float k = Mathf.Clamp(Plugin.EyeSeparationScale.Value, 0.05f, 2f);
            if (Mathf.Abs(k - 1f) < 0.001f) return;
            Camera parent = _parentCamera(__instance);
            Camera child = _childCamera(__instance);
            if (parent == null || child == null || !parent.stereoEnabled) return;

            Matrix4x4 left = child.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 right = child.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            Matrix4x4 eye = parent.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left ? left : right;
            // UUVR only sets the matrix in RelativeMatrix mode; leave other modes (and our own earlier edit) alone.
            if (parent.worldToCameraMatrix != eye) return;

            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                float center = (left[i] + right[i]) * 0.5f;
                m[i] = center + (eye[i] - center) * k;
            }
            parent.worldToCameraMatrix = m;
        }

        public static string EyeSeparationLabel
        {
            get
            {
                for (int i = 0; i < Presets.Length; i++)
                    if (Mathf.Abs(Presets[i] - Plugin.EyeSeparationScale.Value) < 0.01f) return PresetLabels[i];
                return Plugin.EyeSeparationScale.Value.ToString("0.##");
            }
        }

        public static void CycleEyeSeparation()
        {
            float current = Plugin.EyeSeparationScale.Value;
            int next = 0;
            for (int i = 0; i < Presets.Length; i++)
                if (Mathf.Abs(Presets[i] - current) < 0.01f) next = (i + 1) % Presets.Length;
            Plugin.EyeSeparationScale.Value = Presets[next];
            Toast.Show("World size: " + PresetLabels[next]);
        }

        public static void LateUpdate()
        {
            if (!UnityEngine.XR.XRSettings.enabled) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            var pp = cam.GetComponent<PostProcessingBehaviour>();
            if (pp != null && pp.profile != null)
            {
                // Scenes clone their profile and some scripts re-enable DoF, so check every frame (it's a field read).
                if (Plugin.DisableDepthOfField.Value && pp.profile.depthOfField.enabled) pp.profile.depthOfField.enabled = false;
                // Screen-space reflections (water) are traced with the flat camera's projection, so in the headset
                // they slide around with your head instead of staying on the surface.
                if (Plugin.DisableScreenSpaceReflections.Value && pp.profile.screenSpaceReflection.enabled)
                    pp.profile.screenSpaceReflection.enabled = false;            }

            UpdateWater();

            // Screen-space light shafts/haze, computed from the game camera's transform and frustum, which don't turn
            // with your head in UUVR's RelativeMatrix mode: in the headset the effect sticks to your view.
            var volumetric = cam.GetComponent<VolumetricLightRenderer>();
            if (volumetric != null && volumetric.enabled == Plugin.DisableVolumetricLights.Value)
                volumetric.enabled = !Plugin.DisableVolumetricLights.Value;
        }

        private static readonly string[] WaterScreenProps = { "_EdgeBlend", "_LightAbs" };
        private static readonly Dictionary<Material, float[]> WaterOriginals = new Dictionary<Material, float[]>();
        private static float _nextWaterScan;
        private static float _waterRefreshAt = -1f, _lastEyeScale = -1f;
        private static readonly HashSet<int> SeenWater = new HashSet<int>();
        private static bool Log(string s) { Plugin.Log.LogInfo(s); return true; }

        /// <summary>
        /// On the Steam Frame the water's reflection images could come out purple for a whole scene (seen in one),
        /// while turning its reflections off and on again (fresh images) fixed it. So do that a moment after a new
        /// piece of water appears and after the eye resolution changes (auto resolution).
        /// </summary>
        private static void RefreshWaterIfStale()
        {
            // Water that wasn't there before (scenes switch theirs on after loading, not only at load).
            foreach (LPWAsset.LPWReflection refl in UnityEngine.Object.FindObjectsOfType<LPWAsset.LPWReflection>())
                if (refl != null && refl.enabled && SeenWater.Add(refl.GetInstanceID()) && _waterRefreshAt < 0f && Log("New water: " + refl.name))
                    _waterRefreshAt = Time.unscaledTime + 1.5f;
            float scale = UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
            if (_lastEyeScale >= 0f && Mathf.Abs(scale - _lastEyeScale) > 1e-3f && _waterRefreshAt < 0f) _waterRefreshAt = Time.unscaledTime + 1f;
            _lastEyeScale = scale;
            if (_waterRefreshAt < 0f || Time.unscaledTime < _waterRefreshAt) return;
            _waterRefreshAt = -1f;
            int n = 0;
            foreach (LPWAsset.LPWReflection refl in UnityEngine.Object.FindObjectsOfType<LPWAsset.LPWReflection>())
            {
                if (refl == null || !refl.enabled) continue;
                refl.enabled = false; // drops its reflection images
                refl.enabled = true;  // and makes new ones on the next render
                n++;
            }
            if (n > 0) Plugin.Log.LogInfo($"Refreshed {n} water reflection(s).");
        }

        public static int WaterMaterialsChanged
        {
            get
            {
                int n = 0;
                foreach (var kv in WaterOriginals)
                    foreach (float v in kv.Value) if (!float.IsNaN(v)) { n++; break; }
                return n;
            }
        }

        /// <summary>
        /// Low Poly Water: edge foam (_EdgeBlend) and light absorption (_LightAbs) sample a depth texture by screen
        /// position, and LPWDepthEffect/LPWReflection render depth, reflection and refraction from the flat camera's pose,
        /// so in the headset those parts slide with your head. Each can be turned off (and back on) on its own.
        /// </summary>
        private static void UpdateWater()
        {
            if (Time.unscaledTime < _nextWaterScan) return;
            _nextWaterScan = Time.unscaledTime + 0.5f;
            RefreshWaterIfStale();
            bool[] off = { Plugin.DisableWaterEdgeBlend.Value, Plugin.DisableWaterLightAbsorption.Value };
            foreach (LPWAsset.LowPolyWaterScript water in UnityEngine.Object.FindObjectsOfType<LPWAsset.LowPolyWaterScript>())
            {
                foreach (LPWAsset.LPWReflection refl in water.GetComponentsInChildren<LPWAsset.LPWReflection>(true))
                {
                    bool reflOff = Plugin.DisableWaterReflections.Value;
                    if (refl.enabled == !reflOff) continue;
                    refl.enabled = !reflOff;
                    // Disabled, it would leave its last frame in the material, still looked up by screen position (so
                    // still stuck to the view): swap in plain colours. Re-enabled, it sets its own textures again.
                    Material m = refl.GetComponent<Renderer>() != null ? refl.GetComponent<Renderer>().sharedMaterial : null;
                    if (reflOff && m != null)
                    {
                        if (m.HasProperty("_ReflectionTex")) m.SetTexture("_ReflectionTex", SolidTexture(RenderSettings.ambientSkyColor));
                        if (m.HasProperty("_RefractionTex"))
                            m.SetTexture("_RefractionTex", SolidTexture(m.HasProperty("_Color") ? m.GetColor("_Color") : new Color(0.1f, 0.2f, 0.3f)));
                    }
                }
                foreach (Behaviour b in water.GetComponentsInChildren<LPWAsset.LPWDepthEffect>(true)) b.enabled = !(off[0] && off[1]);
                foreach (Renderer r in water.GetComponentsInChildren<Renderer>(true))
                {
                    Material m = r.sharedMaterial;
                    if (m == null) continue;
                    if (!WaterOriginals.TryGetValue(m, out float[] originals))
                    {
                        originals = new float[WaterScreenProps.Length];
                        for (int i = 0; i < originals.Length; i++) originals[i] = float.NaN;
                        WaterOriginals[m] = originals;
                    }
                    for (int i = 0; i < WaterScreenProps.Length; i++)
                    {
                        string prop = WaterScreenProps[i];
                        if (!m.HasProperty(prop)) continue;
                        if (off[i] && float.IsNaN(originals[i]))
                        {
                            originals[i] = m.GetFloat(prop);
                            m.SetFloat(prop, 0f);
                        }
                        else if (!off[i] && !float.IsNaN(originals[i]))
                        {
                            m.SetFloat(prop, originals[i]);
                            originals[i] = float.NaN;
                        }
                    }
                }
            }
        }

        private static readonly Dictionary<Color, Texture2D> Solids = new Dictionary<Color, Texture2D>();

        private static Texture2D SolidTexture(Color c)
        {
            c.a = 1f;
            if (Solids.TryGetValue(c, out Texture2D t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            Solids[c] = t;
            return t;
        }

        [HarmonyPatch(typeof(BlinkCanvasManager), nameof(BlinkCanvasManager.Squint))]
        private static class NoSquint
        {
            private static bool _suppressed;

            private static bool Prefix(bool _toSquint)
            {
                if (_toSquint)
                {
                    _suppressed = !Plugin.SquintWhenBlinkReady.Value && UnityEngine.XR.XRSettings.enabled;
                    return !_suppressed;
                }
                // Skip the matching un-squint too, so no stray "Open" trigger is left on the lid animator.
                if (_suppressed)
                {
                    _suppressed = false;
                    return false;
                }
                return true;
            }
        }
    }
}
