using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BYEFrameBlink
{
    internal enum VignetteMode { Stretched, Off, Original }

    /// <summary>
    /// The game ramps the post-processing vignette up for mood at some moments (LerpVignette). Drawn by the flat
    /// camera's post stack it only darkens the edges of each eye's image at the flat game's framing. With
    /// Vignette = Stretched the flat effect is skipped and the same vignette (the Uber shader's classic formula, with
    /// the profile's live intensity/smoothness/roundness/color) is drawn head-locked across the whole field of view
    /// instead: the screen's edge maps to Veil Half FOV Degrees, like the eyelids.
    /// </summary>
    internal static class VignetteVR
    {
        private const float Distance = 0.5f;
        private const int Size = 96;               // texture spans uv -0.5..1.5, so the darkening carries on past the view's edge
        private const int SortingOrder = -2000;    // under the eyelid veils (-1000...) and UUVR's UI panel (0)

        private static SpriteRenderer _sprite;
        private static Texture2D _tex;
        private static Vector4 _baked = new Vector4(-1f, 0f, 0f, 0f);
        private static Vector3 _bakedShape;

        public static bool OverlayShown => _sprite != null && _sprite.enabled;

        private static bool Suppressing => UnityEngine.XR.XRSettings.enabled && Plugin.Vignette.Value != VignetteMode.Original;

        [HarmonyPatch(typeof(VignetteComponent), nameof(VignetteComponent.active), MethodType.Getter)]
        private static class SkipFlatVignette
        {
            private static void Postfix(VignetteComponent __instance, ref bool __result)
            {
                // Masked vignettes (a texture) aren't redrawn, so leave those to the game.
                if (__result && Suppressing && __instance.model.settings.mode == VignetteModel.Mode.Classic) __result = false;
            }
        }

        /// <summary>For tests: whether the flat post-processing vignette would draw this frame.</summary>
        public static bool FlatActive()
        {
            Camera cam = Camera.main;
            var pp = cam != null ? cam.GetComponent<PostProcessingBehaviour>() : null;
            var component = pp != null ? Traverse.Create(pp).Field("m_Vignette").GetValue<VignetteComponent>() : null;
            return component != null && component.model != null && component.active;
        }

        /// <summary>Debug: "/bye/vignette 0.5" turns the current vignette on at that intensity; "restore" undoes it.</summary>
        public static void Force(string arg)
        {
            VignetteModel model = CurrentModel();
            if (model == null) { Plugin.Log.LogWarning("[cmd] vignette: no post-processing profile"); return; }
            if (arg == "restore")
            {
                if (_saved.HasValue) { model.enabled = _savedEnabled; model.settings = _saved.Value; _saved = null; }
                Plugin.Log.LogInfo("[cmd] vignette restored");
                return;
            }
            if (!float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float intensity)) return;
            if (!_saved.HasValue) { _saved = model.settings; _savedEnabled = model.enabled; }
            VignetteModel.Settings s = model.settings;
            s.mode = VignetteModel.Mode.Classic;
            s.intensity = intensity;
            model.settings = s;
            model.enabled = intensity > 0f;
            Plugin.Log.LogInfo($"[cmd] vignette intensity {intensity}");
        }

        private static VignetteModel.Settings? _saved;
        private static bool _savedEnabled;

        public static VignetteModel CurrentModel()
        {
            Camera cam = Camera.main;
            var pp = cam != null ? cam.GetComponent<PostProcessingBehaviour>() : null;
            return pp != null && pp.enabled && pp.profile != null ? pp.profile.vignette : null;
        }

        public static void LateUpdate()
        {
            VignetteModel model = CurrentModel();
            bool show = Plugin.Vignette.Value == VignetteMode.Stretched && UnityEngine.XR.XRSettings.enabled && UiOverlay.Camera != null &&
                        model != null && model.enabled && model.settings.mode == VignetteModel.Mode.Classic && model.settings.intensity > 0.001f;
            if (!show)
            {
                if (_sprite != null) _sprite.enabled = false;
                return;
            }

            if (_sprite == null)
            {
                _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _sprite = UiOverlay.CreateSprite("BYE Vignette", SortingOrder);
                _sprite.sprite = Sprite.Create(_tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            }

            VignetteModel.Settings s = model.settings;
            float aspect = UnityEngine.XR.XRSettings.eyeTextureHeight > 0
                ? (float)UnityEngine.XR.XRSettings.eyeTextureWidth / UnityEngine.XR.XRSettings.eyeTextureHeight : 1f;
            // Same parameters VignetteComponent.Prepare hands the Uber shader.
            var p = new Vector4(s.intensity * 3f, s.smoothness * 5f, (1f - s.roundness) * 6f + s.roundness, s.rounded ? 1f : 0f);
            var shape = new Vector3(s.center.x, s.center.y, aspect);
            if ((p - _baked).sqrMagnitude > 1e-8f || (shape - _bakedShape).sqrMagnitude > 1e-8f) Bake(p, s.center, aspect);

            float halfH = Distance * Mathf.Tan(Plugin.VeilHalfFovDegrees.Value * Mathf.Deg2Rad);
            _sprite.enabled = true;
            _sprite.color = new Color(s.color.r, s.color.g, s.color.b, 1f);
            _sprite.transform.localPosition = new Vector3(0f, 0f, Distance);
            _sprite.transform.localRotation = Quaternion.identity;
            // uv 0..1 spans 2*halfH vertically (2*halfH*aspect across); the texture spans twice that.
            _sprite.transform.localScale = new Vector3(4f * halfH * aspect, 4f * halfH, 1f);
        }

        private static void Bake(Vector4 p, Vector2 center, float aspect)
        {
            _baked = p;
            _bakedShape = new Vector3(center.x, center.y, aspect);
            var px = new Color32[Size * Size];
            float aspectScale = Mathf.Lerp(1f, aspect, p.w);
            for (int y = 0; y < Size; y++)
            {
                float v = -0.5f + 2f * (y + 0.5f) / Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = -0.5f + 2f * (x + 0.5f) / Size;
                    float dx = Mathf.Abs(u - center.x) * p.x * aspectScale, dy = Mathf.Abs(v - center.y) * p.x;
                    dx = Mathf.Pow(dx, p.z);
                    dy = Mathf.Pow(dy, p.z);
                    float clear = Mathf.Pow(Mathf.Clamp01(1f - (dx * dx + dy * dy)), p.y);
                    px[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt((1f - clear) * 255f));
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }
    }
}
