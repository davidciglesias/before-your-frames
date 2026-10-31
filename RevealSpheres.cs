using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Some scenes switch the room's look (e.g. day to night) inside world-space spheres (FogOfWar feeds their centers
    /// and radii to the dissolve shader as _Positions). They're sized to fill a flat screen's view, so in a headset's
    /// wider view their edges show as a band. Reveal Sphere Scale enlarges the radii sent to the shader in VR.
    /// </summary>
    [HarmonyPatch(typeof(FogOfWar), "UpdateSpheres")]
    internal static class RevealSpheres
    {
        private static readonly AccessTools.FieldRef<FogOfWar, Vector4[]> Positions = AccessTools.FieldRefAccess<FogOfWar, Vector4[]>("positions");
        private static Vector4[] _scaled;

        private static void Postfix(FogOfWar __instance)
        {
            float k = Plugin.RevealSphereScale.Value;
            Vector4[] src = Positions(__instance);
            if (!UnityEngine.XR.XRSettings.enabled || Mathf.Approximately(k, 1f) || src == null) return;
            if (_scaled == null || _scaled.Length != src.Length) _scaled = new Vector4[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                _scaled[i] = src[i];
                _scaled[i].w *= k;
            }
            Shader.SetGlobalVectorArray("_Positions", _scaled);
        }
    }
}
