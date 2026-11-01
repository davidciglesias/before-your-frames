using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's realtime reflection probes re-render cube maps every frame; on a Steam Frame they cost more than the
    /// rest of the frame in the heaviest scenes (measured 12.3 ms, half rate, against 9 ms and full rate without them).
    /// In performance mode, Performance Reflections = Off stops their live updates; the scene keeps its last
    /// reflections (water looks a little flatter). Time-slicing or lowering their resolution didn't help, and changing
    /// the resolution of a live probe turned water purple.
    /// </summary>
    internal static class ReflectionThrottle
    {
        public static void LateUpdate(bool active)
        {
            bool realtime = !active || Plugin.PerformanceReflections.Value == ReflectionMode.Full;
            if (QualitySettings.realtimeReflectionProbes != realtime) QualitySettings.realtimeReflectionProbes = realtime;
        }
    }
}
