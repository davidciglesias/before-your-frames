using System;
using UnityEngine;
using UnityEngine.XR;

namespace BYEFrameBlink
{
    internal enum AutoResolutionMode { Auto, On, Off }

    /// <summary>
    /// Dynamic resolution. Every couple of seconds, reads SteamVR's own measurement of the game's GPU time per frame
    /// and scales the eye textures (RenderScale applies Factor on top of Render Scale): down a step when the GPU nears
    /// the headset's frame budget or frames get reused, back up when there's headroom. A missed frame means SteamVR
    /// shows the previous one warped, which with this game's camera setup looks like the view shaking, so a slightly
    /// softer image is the better trade. It changes the texture size, not XRSettings.renderViewportScale: with a
    /// partial viewport the game's post-processing samples its color grading wrongly (night scenes turned red).
    /// Resizing reallocates the eye textures, so it happens in steps, with a pause after each. Auto = on in
    /// performance mode.
    /// </summary>
    internal static class AutoResolution
    {
        private const float Step = 0.1f;
        private const int Window = 60;
        private const float Interval = 2f, SettleAfterChange = 3f;

        private static float _next;
        private static float _factor = 1f;
        private static float _budgetMs;
        private static Valve.VR.Compositor_FrameTiming[] _timings;

        public static float LastGpuMs { get; private set; }
        public static int LastReused { get; private set; }

        public static bool Active
        {
            get
            {
                AutoResolutionMode m = Plugin.AutoResolution.Value;
                return XRSettings.enabled && (m == AutoResolutionMode.On || (m == AutoResolutionMode.Auto && Performance.Active));
            }
        }

        /// <summary>Multiplier on Render Scale (1 = no reduction).</summary>
        public static float Factor => Active ? _factor : 1f;

        public static string Label => Plugin.AutoResolution.Value == AutoResolutionMode.Auto
            ? "auto (" + (Active ? "on" : "off") + ")"
            : Active ? "on" : "off";

        public static void LateUpdate()
        {
            if (!Active || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;

            if (!Measure(out float gpu, out int reused)) return;
            LastGpuMs = gpu;
            LastReused = reused;
            float budget = Budget();
            float min = Mathf.Clamp(Plugin.AutoResolutionMin.Value, 0.3f, 1f);

            float target = _factor;
            if (gpu > budget * 1.15f || reused > Window / 3) target -= Step * 2f;
            else if (gpu > budget * 0.9f || reused > 3) target -= Step;
            else if (gpu < budget * 0.65f && reused == 0) target += Step;
            target = Mathf.Clamp(target, min, 1f);
            if (Mathf.Abs(target - _factor) < 0.001f) return;
            _factor = target;
            _next = Time.unscaledTime + SettleAfterChange; // the new size's timings need a moment to show
        }

        private static bool Measure(out float gpuMs, out int reused)
        {
            gpuMs = 0f;
            reused = 0;
            try
            {
                Valve.VR.CVRCompositor comp = Valve.VR.OpenVR.Compositor;
                if (comp == null) return false;
                if (_timings == null)
                {
                    _timings = new Valve.VR.Compositor_FrameTiming[Window];
                    uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Valve.VR.Compositor_FrameTiming));
                    for (int i = 0; i < Window; i++) _timings[i].m_nSize = size;
                }
                uint n = comp.GetFrameTimings(_timings);
                if (n == 0) return false;
                for (int i = 0; i < n; i++)
                {
                    gpuMs += _timings[i].m_flTotalRenderGpuMs;
                    if (_timings[i].m_nNumFramePresents > 1) reused++;
                }
                gpuMs /= n;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Auto resolution: no frame timing (" + e.Message + "); turning it off for this session.");
                Plugin.AutoResolution.Value = AutoResolutionMode.Off;
                return false;
            }
        }

        /// <summary>Frame budget in ms from the headset's refresh rate (72 Hz on a Steam Frame by default).</summary>
        private static float Budget()
        {
            if (_budgetMs > 0f) return _budgetMs;
            float hz = XRDevice.refreshRate;
            if (hz < 30f) hz = 90f;
            _budgetMs = 1000f / hz;
            Plugin.Log.LogInfo($"Auto resolution: {hz:F0} Hz, {_budgetMs:F1} ms per frame.");
            return _budgetMs;
        }
    }
}
