using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// In click-to-blink mode the game reads blinks via Input.GetKey/GetKeyDown/GetKeyUp(KeyCode.Mouse0)
    /// (BlinkCounter, EyesClosedMechanic, DeathLoop, InteractableDialogueBlender, ...).
    /// Instead of hooking UnityEngine.Input itself (tiny wrappers that the JIT may inline into callers),
    /// we transpile every Assembly-CSharp method that calls them so the call goes through our wrappers,
    /// which OR the real mouse button with the eye-tracking "eyes closed" state.
    /// </summary>
    internal static class InputRedirect
    {
        private static readonly MethodInfo GetKeyOrig = AccessTools.Method(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) });
        private static readonly MethodInfo GetKeyDownOrig = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
        private static readonly MethodInfo GetKeyUpOrig = AccessTools.Method(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) });

        private static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new Dictionary<MethodInfo, MethodInfo>
        {
            { GetKeyOrig, AccessTools.Method(typeof(InputRedirect), nameof(GetKey)) },
            { GetKeyDownOrig, AccessTools.Method(typeof(InputRedirect), nameof(GetKeyDown)) },
            { GetKeyUpOrig, AccessTools.Method(typeof(InputRedirect), nameof(GetKeyUp)) },
            // Center-of-view "what am I looking at" rays -> eye gaze. Instance method (this, pos) maps onto static (cam, pos).
            {
                AccessTools.Method(typeof(Camera), nameof(Camera.ViewportPointToRay), new[] { typeof(Vector3) }),
                AccessTools.Method(typeof(Gaze), nameof(Gaze.ViewportPointToRay))
            },
            // Screen-center world points (connect-the-dots line end) -> along the same head + gaze ray.
            {
                AccessTools.Method(typeof(Camera), nameof(Camera.ScreenToWorldPoint), new[] { typeof(Vector3) }),
                AccessTools.Method(typeof(Gaze), nameof(Gaze.ScreenToWorldPoint))
            },
            // Webcam preview / antivirus-stim feeds -> no-op while Block Webcam is on.
            {
                AccessTools.Method(typeof(WebCamTexture), nameof(WebCamTexture.Play), Type.EmptyTypes),
                AccessTools.Method(typeof(WebcamBlock), nameof(WebcamBlock.Play))
            },
        };

        private const KeyCode BlinkKey = KeyCode.Mouse0;

        public static int PatchedMethodCount { get; private set; }
        public static string[] PatchedTypes { get; private set; } = new string[0];

        public static bool GetKey(KeyCode key) => Input.GetKey(key) || (key == BlinkKey && BlinkState.Closed);
        public static bool GetKeyDown(KeyCode key)
        {
            // A controller press can reach the game twice: our menu shortcut and Steam Input's keyboard emulation
            // (Escape on the Frame's right controller), which opened the pause menu and closed it again at once.
            if ((key == KeyCode.Escape || key == KeyCode.P) && ControllerShortcuts.JustToggled) return false;
            return Input.GetKeyDown(key) || (key == BlinkKey && BlinkState.ClosedThisFrame);
        }
        public static bool GetKeyUp(KeyCode key) => Input.GetKeyUp(key) || (key == BlinkKey && BlinkState.OpenedThisFrame);

        public static void PatchGameInputCalls(Harmony harmony)
        {
            var sw = Stopwatch.StartNew();
            Assembly game = typeof(BlinkCounter).Assembly;
            var transpiler = new HarmonyMethod(AccessTools.Method(typeof(InputRedirect), nameof(Transpiler)));
            int patched = 0;
            var typeNames = new SortedSet<string>();

            foreach (MethodBase method in FindCallers(game))
            {
                try
                {
                    harmony.Patch(method, transpiler: transpiler);
                    patched++;
                    Type owner = method.DeclaringType;
                    while (owner?.DeclaringType != null) owner = owner.DeclaringType; // coroutine state machines
                    if (owner != null) typeNames.Add(owner.Name);
                    Plugin.Log.LogDebug($"Redirected input in {method.DeclaringType?.FullName}.{method.Name}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Could not patch {method.DeclaringType?.FullName}.{method.Name}: {e.Message}");
                }
            }

            PatchedMethodCount = patched;
            PatchedTypes = typeNames.ToArray();
            Plugin.Log.LogInfo($"Redirected Input.GetKey*/ViewportPointToRay/WebCamTexture.Play in {patched} game methods ({sw.ElapsedMilliseconds} ms): {string.Join(", ", typeNames.ToArray())}");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction ins in instructions)
            {
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
                    && ins.operand is MethodInfo mi
                    && Replacements.TryGetValue(mi, out MethodInfo replacement))
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = replacement;
                }
                yield return ins;
            }
        }

        /// <summary>Finds methods whose IL contains a call to one of the target Input methods.</summary>
        private static IEnumerable<MethodBase> FindCallers(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                       BindingFlags.Static | BindingFlags.DeclaredOnly;
            var tokenCache = new Dictionary<int, bool>();
            Module module = assembly.ManifestModule;

            foreach (Type type in types)
            {
                if (type.IsGenericTypeDefinition) continue;
                IEnumerable<MethodBase> methods;
                try
                {
                    methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags).Cast<MethodBase>());
                }
                catch { continue; }

                foreach (MethodBase m in methods)
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    byte[] il;
                    try { il = m.GetMethodBody()?.GetILAsByteArray(); }
                    catch { continue; }
                    if (il == null) continue;
                    if (CallsTarget(il, module, tokenCache)) yield return m;
                }
            }
        }

        private static bool CallsTarget(byte[] il, Module module, Dictionary<int, bool> cache)
        {
            // Byte-level scan for "call <token>" (0x28) / "callvirt <token>" (0x6F).
            // False positives are harmless: they just resolve to other members.
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;
                int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                int table = (int)((uint)token >> 24);
                if (table != 0x0A && table != 0x06) continue; // MemberRef or MethodDef
                if (!cache.TryGetValue(token, out bool hit))
                {
                    try
                    {
                        hit = module.ResolveMethod(token) is MethodInfo mi && Replacements.ContainsKey(mi);
                    }
                    catch { hit = false; }
                    cache[token] = hit;
                }
                if (hit) return true;
            }
            return false;
        }
    }

    /// <summary>Counts blinks the game itself accepted, for the trace/overlay.</summary>
    [HarmonyPatch(typeof(BlinkCounter), nameof(BlinkCounter.BlinkConfirmed))]
    internal static class BlinkConfirmedCounter
    {
        public static int Count;

        private static void Postfix() => Count++;
    }

    /// <summary>Keeps the game in its mouse-click blink mode so the webcam tracker is never consulted.</summary>
    internal static class BlinkModeEnforcer
    {
        private static float _nextCheck;

        public static void Update()
        {
            if (!Plugin.ForceClickBlinkMode.Value || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;

            BlinkCounter counter = UnityEngine.Object.FindObjectOfType<BlinkCounter>();
            if (counter != null && counter._detectionMode != BlinkCounter.DetectionMode.DebugKeyOnly)
            {
                Plugin.Log.LogInfo($"Switching BlinkCounter from {counter._detectionMode} to DebugKeyOnly (click/eye-tracking blinks).");
                counter._detectionMode = BlinkCounter.DetectionMode.DebugKeyOnly;
            }

            PlayerSettingsSaveData save = PlayerSettingsSaveData.Instance;
            if (save != null && save.settings != null && save.settings.useBlinks)
            {
                save.settings.useBlinks = false;
            }
        }
    }
}
