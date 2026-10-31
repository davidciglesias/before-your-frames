using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Supersampling. SteamVR picks the eye resolution (1580x1580 on the Frame here), which over the headset's field of
    /// view is about a third of a monitor's pixels per degree: wide, distant scenery (the main menu's vista) looks
    /// low-res and its foliage/shadow edges jagged. Render Scale scales the eye textures everywhere; Menu Render Scale
    /// applies in the main menu, which is cheap to draw, so it can be sharper at no cost to gameplay. In performance
    /// mode (on the headset itself) the steps go below 100% and the menu uses Render Scale too.
    /// </summary>
    internal static class RenderScale
    {
        public static readonly float[] DesktopPresets = { 1f, 1.25f, 1.5f, 1.75f };
        public static readonly float[] LowPowerPresets = { 0.8f, 1f, 1.25f };

        public static float[] Presets => Performance.Active ? LowPowerPresets : DesktopPresets;

        public static float Target
        {
            get
            {
                float s = Plugin.RenderScale.Value;
                MainMenuController mmc = MainMenuController.Instance;
                if (!Performance.Active && mmc != null && mmc.isActiveAndEnabled) s = Mathf.Max(s, Plugin.MenuRenderScale.Value);
                return Mathf.Clamp(s * AutoResolution.Factor, 0.25f, 2f);
            }
        }

        public static void LateUpdate()
        {
            if (!UnityEngine.XR.XRSettings.enabled) return;
            float target = Target;
            if (Mathf.Abs(UnityEngine.XR.XRSettings.eyeTextureResolutionScale - target) < 0.001f) return;
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = target;
            Plugin.Log.LogInfo($"Eye texture scale {target:F2} ({UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}).");
        }

        public static void Cycle()
        {
            float[] presets = Presets;
            float current = Plugin.RenderScale.Value;
            int next = 0;
            for (int i = 0; i < presets.Length; i++)
                if (Mathf.Abs(presets[i] - current) < 0.01f) next = (i + 1) % presets.Length;
            Plugin.RenderScale.Value = presets[next];
            Toast.Show("Render quality: " + Label + (presets[next] > 1f ? " (heavier on the GPU)" : ""));
        }

        public static string Label => Mathf.RoundToInt(Plugin.RenderScale.Value * 100f) + "%";
    }
}
