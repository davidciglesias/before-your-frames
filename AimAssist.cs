using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Bigger "hitbox" for the game's look-at check, without changing any visuals. InteractableManager.Update does
    /// Physics.Raycast(aimRay, out hit, 1000, mask) and treats hits tagged _iaTag as interactable. If that center ray
    /// doesn't hit an interactable, we also try rings of rays inside a small cone around it and report the interactable
    /// closest (in angle) to the aim direction.
    /// </summary>
    [HarmonyPatch(typeof(InteractableManager), "Update")]
    internal static class AimAssist
    {
        private static readonly MethodInfo RaycastOrig = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
            new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) });

        public static int AssistedHits;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(typeof(AimAssist), nameof(Raycast));
            int n = 0;
            foreach (CodeInstruction ins in instructions)
            {
                if (ins.opcode == OpCodes.Call && ins.operand is MethodInfo mi && mi == RaycastOrig)
                {
                    ins.operand = replacement;
                    n++;
                }
                yield return ins;
            }
            if (n == 0) Plugin.Log.LogWarning("Aim assist: InteractableManager.Update raycast not found.");
        }

        public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance, int layerMask)
        {
            bool any = Physics.Raycast(ray, out hit, maxDistance, layerMask);
            float cone = Plugin.AimAssistDegrees.Value;
            if (cone <= 0f || (any && IsInteractable(hit))) return any;

            Quaternion basis = Quaternion.LookRotation(ray.direction);
            const int rings = 3;
            for (int r = 1; r <= rings; r++)
            {
                float angle = cone * r / rings;
                int spokes = 8 * r;
                for (int s = 0; s < spokes; s++)
                {
                    float around = 360f * s / spokes;
                    Vector3 dir = basis * (Quaternion.Euler(0f, 0f, around) * Quaternion.Euler(angle, 0f, 0f) * Vector3.forward);
                    if (Physics.Raycast(new Ray(ray.origin, dir), out RaycastHit h, maxDistance, layerMask) && IsInteractable(h))
                    {
                        hit = h;
                        AssistedHits++;
                        return true;
                    }
                }
            }
            return any;
        }

        /// <summary>
        /// Self-test: a 0.2 m interactable cube 10 m in front of a far-away origin; rays aimed 2 and 6 degrees off it.
        /// Returns (bare 2deg hit, assisted 2deg hit, assisted 6deg hit). Expected with a 3 degree cone: false, true, false.
        /// </summary>
        public static bool[] SelfTest()
        {
            InteractableManager im = InteractableManager.instance;
            if (im == null) return null;
            int mask = im._layerMask.value;
            int layer = 0;
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { layer = i; break; }

            var origin = new Vector3(5000f, 5000f, 5000f); // far from any level geometry
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cube.name = "BYE AimTest Cube";
                cube.layer = layer;
                cube.tag = im._iaTag;
                cube.transform.position = origin + Vector3.forward * 10f;
                cube.transform.localScale = Vector3.one * 0.2f; // ~1.1 degrees across at 10 m
                Physics.SyncTransforms();

                Ray Off(float deg) => new Ray(origin, Quaternion.Euler(0f, deg, 0f) * Vector3.forward);
                bool bare = Physics.Raycast(Off(2f), out _, 1000f, mask);
                bool near = Raycast(Off(2f), out RaycastHit h1, 1000f, mask) && h1.transform == cube.transform;
                bool far = Raycast(Off(6f), out RaycastHit h2, 1000f, mask) && h2.transform == cube.transform;
                return new[] { bare, near, far };
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }

        private static bool IsInteractable(RaycastHit h)
        {
            InteractableManager im = InteractableManager.instance;
            string tag = im != null ? im._iaTag : "Interactable";
            return h.transform != null && h.transform.tag == tag;
        }
    }
}
