using System.Collections.Generic;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Billboard particles default to "View" alignment: each one is laid flat to the camera's view plane, so in a headset
    /// they turn with your head even when you stand still (the opening scene's fog followed you around). "Facing" turns
    /// each particle toward the camera position instead, so turning your head leaves them in place. Applied to every
    /// billboard particle renderer in VR, rechecked every couple of seconds for newly loaded scenes.
    /// </summary>
    internal static class ParticleFacing
    {
        private static readonly HashSet<int> Done = new HashSet<int>();
        private static float _next;

        public static void Update()
        {
            if (!Plugin.ParticlesFaceHead.Value || !UnityEngine.XR.XRSettings.enabled || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            foreach (ParticleSystemRenderer r in Object.FindObjectsOfType<ParticleSystemRenderer>())
            {
                if (r == null) continue;
                float cap = Plugin.ParticleMaxSize.Value;
                if (cap > 0f && r.maxParticleSize < cap)
                {
                    Plugin.Log.LogInfo($"[particles] {r.name}: maxParticleSize {r.maxParticleSize} -> {cap}");
                    r.maxParticleSize = cap;
                }
                if (!Done.Add(r.GetInstanceID())) continue;
                if (r.renderMode != ParticleSystemRenderMode.Billboard || r.alignment != ParticleSystemRenderSpace.View) continue;
                r.alignment = ParticleSystemRenderSpace.Facing;
                Plugin.Log.LogInfo($"[particles] {r.name}: View -> Facing (maxSize={r.maxParticleSize})");
            }
        }
    }
}
