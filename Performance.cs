using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BYEFrameBlink
{
    internal enum PerformanceMode { Auto, On, Off }
    internal enum ReflectionMode { Full, Off }

    /// <summary>
    /// For running on the headset itself (Proton + FEX on the Steam Frame's mobile chip), where the game's
    /// desktop settings (8x MSAA, very high shadows with 4 cascades, ambient occlusion, bloom, grain) overheat it.
    /// Auto = on under Wine/Proton. When on: Performance MSAA (2x), no HDR, lighter shadows, no ambient occlusion/bloom/grain/FXAA,
    /// render quality steps 80/100/120%, and Menu Render Scale ignored (menus stay at 100% or more).
    /// </summary>
    internal static class Performance
    {
        public const float ShadowDistance = 40f;

        public static readonly bool OnWine = DetectWine();
        private static float _nextApply;

        public static bool Active
        {
            get
            {
                PerformanceMode m = Plugin.Performance.Value;
                return m == PerformanceMode.On || (m == PerformanceMode.Auto && OnWine);
            }
        }

        /// <summary>Performance MSAA rounded down to what Unity accepts (0, 2, 4, 8). On the Frame's tile-based GPU
        /// 2x with HDR off keeps a steady 72 fps; 4x costs about 4 ms more per frame.</summary>
        public static int Msaa
        {
            get
            {
                int v = Plugin.PerformanceMsaa.Value;
                return v >= 8 ? 8 : v >= 4 ? 4 : v >= 2 ? 2 : 0;
            }
        }

        public static string Label => Plugin.Performance.Value == PerformanceMode.Auto
            ? "auto (" + (Active ? "on" : "off") + ")"
            : Active ? "on" : "off";

        public static void LateUpdate()
        {
            // The game sets its quality level from its own options (and on load), so re-assert now and then.
            if (!Active || Time.unscaledTime < _nextApply) return;
            _nextApply = Time.unscaledTime + 1f;
            // HDR targets with MSAA cost the headset's GPU most of all (measured: 18 ms -> 8-12 ms per frame with HDR
            // off and 2x MSAA, against a 13.9 ms budget at 72 Hz); cameras come and go with scenes, so check them all.
            if (!Plugin.PerformanceHdr.Value)
                foreach (Camera c in Camera.allCameras)
                    if (c.allowHDR) c.allowHDR = false;
            int msaa = Msaa;
            if (QualitySettings.antiAliasing != msaa) QualitySettings.antiAliasing = msaa;
            if (QualitySettings.shadowResolution > ShadowResolution.Medium) QualitySettings.shadowResolution = ShadowResolution.Medium;
            if (QualitySettings.shadowCascades > 2) QualitySettings.shadowCascades = 2;
            if (QualitySettings.shadowDistance > ShadowDistance) QualitySettings.shadowDistance = ShadowDistance;
        }

        public static void Cycle()
        {
            Plugin.Performance.Value = Plugin.Performance.Value == PerformanceMode.Auto
                ? (OnWine ? PerformanceMode.Off : PerformanceMode.On)
                : Plugin.Performance.Value == PerformanceMode.On ? PerformanceMode.Off : PerformanceMode.Auto;
            Toast.Show("Performance mode: " + Label + (Active ? "" : " (takes full effect after a restart)"));
        }

        [HarmonyPatch(typeof(AmbientOcclusionComponent), nameof(AmbientOcclusionComponent.active), MethodType.Getter)]
        private static class NoAmbientOcclusion { private static void Postfix(ref bool __result) { if (__result && Active) __result = false; } }

        [HarmonyPatch(typeof(BloomComponent), nameof(BloomComponent.active), MethodType.Getter)]
        private static class NoBloom { private static void Postfix(ref bool __result) { if (__result && Active) __result = false; } }

        [HarmonyPatch(typeof(GrainComponent), nameof(GrainComponent.active), MethodType.Getter)]
        private static class NoGrain { private static void Postfix(ref bool __result) { if (__result && Active) __result = false; } }

        [HarmonyPatch(typeof(FxaaComponent), nameof(FxaaComponent.active), MethodType.Getter)]
        private static class NoFxaa { private static void Postfix(ref bool __result) { if (__result && Active) __result = false; } }

        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

        /// <summary>Wine's ntdll exports wine_get_version; Windows' doesn't.</summary>
        private static bool DetectWine()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch { return false; }
        }
    }
}
