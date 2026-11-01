using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's realtime reflection probes re-render cube maps every frame; on a Steam Frame they cost more than the
    /// rest of the frame in the heaviest scenes (measured 12.3 ms, half rate, against 9 ms and full rate without them).
    /// In performance mode, Performance Reflections = Off stops their live updates; the scene keeps its last
    /// reflections (water looks a little flatter). A probe that never rendered has no image at all (water turns
    /// purple), so after each scene load they stay live for a moment and are rendered once before being paused.
    /// Time-slicing or lowering their resolution didn't help, and changing the resolution of a live probe turned water
    /// purple too.
    /// </summary>
    internal static class ReflectionThrottle
    {
        private const float LiveAfterLoad = 2f;   // seconds of live reflections after a scene loads
        private static float _liveUntil;
        private static bool _hooked, _rendered;

        public static void LateUpdate(bool active)
        {
            if (!_hooked)
            {
                _hooked = true;
                SceneManager.sceneLoaded += (s, m) => { _liveUntil = Time.unscaledTime + LiveAfterLoad; _rendered = false; };
            }
            bool settling = Time.unscaledTime < _liveUntil;
            bool realtime = !active || Plugin.PerformanceReflections.Value == ReflectionMode.Full || settling;
            if (QualitySettings.realtimeReflectionProbes != realtime) QualitySettings.realtimeReflectionProbes = realtime;

            // Near the end of the live window, make sure every realtime probe has an image (some only render when a
            // script asks), so pausing them leaves their last reflections rather than none.
            if (active && settling && !_rendered && _liveUntil - Time.unscaledTime < LiveAfterLoad * 0.5f)
            {
                _rendered = true;
                int n = 0;
                foreach (ReflectionProbe p in Object.FindObjectsOfType<ReflectionProbe>())
                {
                    if (p == null || !p.isActiveAndEnabled || p.mode != ReflectionProbeMode.Realtime) continue;
                    p.RenderProbe();
                    n++;
                }
                if (n > 0) Plugin.Log.LogInfo($"Rendered {n} reflection probe(s) once before pausing them.");
            }
        }
    }
}
