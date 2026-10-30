using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// Debug commands received over OSC (/bye/...), run on the main thread. Lets the game be driven and inspected
    /// from a script while it's rendering to the headset (the desktop window doesn't update in VR).
    ///   /bye/shot [name]                 save the left-eye image to BepInEx/BYEFrameBlink_shots/
    ///   /bye/buttons                     log active UI buttons and their labels
    ///   /bye/click "label"               invoke the onClick of the active button whose label matches
    ///   /bye/invoke "Type.Method"        call a parameterless method on the first object of that type (or a static)
    ///   /bye/scenes                      log loaded scenes
    /// </summary>
    internal static class DebugCommands
    {
        private static readonly Queue<KeyValuePair<string, string>> Pending = new Queue<KeyValuePair<string, string>>();

        public static void Enqueue(string address, string arg)
        {
            lock (Pending) Pending.Enqueue(new KeyValuePair<string, string>(address, arg));
        }

        public static void Update()
        {
            while (true)
            {
                KeyValuePair<string, string> cmd;
                lock (Pending)
                {
                    if (Pending.Count == 0) return;
                    cmd = Pending.Dequeue();
                }
                try { Run(cmd.Key, cmd.Value); }
                catch (Exception e) { Plugin.Log.LogError($"Debug command {cmd.Key} failed: {e}"); }
            }
        }

        private static void Run(string address, string arg)
        {
            switch (address)
            {
                case "/bye/shot": Screenshot(arg); break;
                case "/bye/overlayshot": OverlayShot(arg); break;
                case "/bye/followers": Followers(arg); break;
                case "/bye/buttons": ListButtons(); break;
                case "/bye/menucands": MenuGaze.DumpCandidates(); break;
                case "/bye/click": Click(arg); break;
                case "/bye/invoke": Invoke(arg); break;
                case "/bye/addrs":
                    foreach (var kv in OscReceiver.GetSeenAddresses().OrderBy(k => k.Key))
                        Plugin.Log.LogInfo($"[cmd] osc {kv.Key} = {kv.Value:F3}");
                    break;
                case "/bye/inspect": Inspect(arg); break;
                case "/bye/fields": Fields(arg); break;
                case "/bye/ias":
                    // Every interactable in the scene with the state of its icon (what the aim ray can actually hit).
                    foreach (InteractableObject ia in UnityEngine.Object.FindObjectsOfType<InteractableObject>())
                    {
                        InteractableIcon icon = ia._interactableIcon;
                        Collider ic = icon != null ? icon.GetComponent<Collider>() : null;
                        Plugin.Log.LogInfo($"[cmd] ia {ia.name} state={ia._interactState} count={ia._interactCount}/{ia._maxInteractions} tag={ia.tag} " +
                                           $"icon={(icon == null ? "none" : (icon.gameObject.activeInHierarchy ? "active" : "inactive"))} iconCollider={(ic == null ? "none" : ic.enabled.ToString())} " +
                                           $"gaze={(ia.GetComponent<GazeInteract>() != null)} pos={(icon != null ? icon.transform.position : ia.transform.position):F2}");
                    }
                    break;
                case "/bye/rayall":
                {
                    // Everything the game's aim ray passes through (its layer mask), nearest first.
                    InteractableManager rim = InteractableManager.instance;
                    if (rim == null || rim._mainCamera == null) break;
                    Ray ray = Gaze.ViewportPointToRay(rim._mainCamera, new Vector3(0.5f, 0.5f, 0f));
                    Plugin.Log.LogInfo($"[cmd] rayall origin={ray.origin:F3} dir={ray.direction:F3} camPos={rim._mainCamera.transform.position:F3} near={rim._mainCamera.nearClipPlane}");
                    foreach (RaycastHit h in Physics.RaycastAll(ray, 1000f, rim._layerMask).OrderBy(h => h.distance))
                        Plugin.Log.LogInfo($"[cmd]   {h.distance:F3} m  {PathOf(h.collider.transform)}  layer={h.collider.gameObject.layer} tag={h.collider.tag} collider={h.collider.GetType().Name}");
                    break;
                }
                case "/bye/status": StatusDump.Write(arg); break;
                case "/bye/goto":
                {
                    // Jump to a scene by (partial) prefab name: the next scene change loads it, and the sequence continues from there.
                    GameManager gm = GameManager.instance;
                    if (gm == null || gm._scenePrefabSequence == null || BlinkCounter.instance == null) { Plugin.Log.LogWarning("[cmd] goto: not in gameplay"); break; }
                    var seq = gm._scenePrefabSequence._scenePrefabs;
                    int idx = seq.FindIndex(n => string.Equals(n, arg, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) idx = seq.FindIndex(n => n.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0);
                    // Drop the scene the game already preloaded (and any preload still running), or the next scene change
                    // after the jump loads that one and the sequence plays a scene twice.
                    var gmt = HarmonyLib.Traverse.Create(gm);
                    var preload = gmt.Field("preloadCrt").GetValue<Coroutine>();
                    if (preload != null) gm.StopCoroutine(preload);
                    gmt.Field("preloadCrt").SetValue(null);
                    gmt.Field("loadingStageScene").SetValue(null);
                    gmt.Field("immediateLoadScene").SetValue(null);
                    if (idx > 0)
                    {
                        gm._currentSceneId = idx;
                        gm._overrideSceneId = idx;
                        gm.branchingScene = "";
                        Plugin.Log.LogInfo($"[cmd] goto '{seq[idx]}' (sequence index {idx})");
                    }
                    else
                    {
                        gm.branchingScene = arg; // not in the main sequence (inserted/branch scene): load by prefab name
                        Plugin.Log.LogInfo($"[cmd] goto '{arg}' as branching scene");
                    }
                    BlinkCounter.instance.ForceEndBlink();
                    break;
                }
                case "/bye/gazetargets":
                {
                    InteractableManager gim = InteractableManager.instance;
                    var lines = new System.Collections.Generic.List<string>();
                    var cols = GazeTargets.Collect(gim != null ? gim._layerMask.value : ~0, lines);
                    foreach (string l in lines) Plugin.Log.LogInfo("[cmd] gaze listener: " + l);
                    Plugin.Log.LogInfo($"[cmd] gazetargets: {cols.Count} collider(s); current gaze object = " +
                                       (gim != null && gim.currentGazeObject != null ? gim.currentGazeObject.name : "none"));
                    break;
                }
                case "/bye/aimat":
                {
                    GameObject go = GameObject.Find(arg);
                    InteractableManager imgr = UnityEngine.Object.FindObjectOfType<InteractableManager>();
                    Camera cam = imgr != null && imgr._mainCamera != null ? imgr._mainCamera : Camera.main;
                    if (go == null || cam == null)
                    {
                        Plugin.Log.LogWarning($"[cmd] aimat: '{arg}' or camera not found");
                        break;
                    }
                    Component target = (Component)go.GetComponent<InteractableObject>() ?? go.transform;
                    Vector3 p = AutoPlay.AimPoint(target);
                    AutoPlay.AimAt(cam, p);
                    Collider col = go.GetComponentInChildren<Collider>();
                    Plugin.Log.LogInfo($"[cmd] aimat '{arg}' point={p:F3} camera={cam.name} collider={(col != null ? col.name + " layer " + col.gameObject.layer + " tag " + col.tag + " enabled " + col.enabled : "none")} sim={SimHead.Rotation.eulerAngles:F1}");
                    break;
                }
                case "/bye/menuaim":
                {
                    // "label" aims at the button's centre; "label|N" aims N degrees below its bottom edge.
                    string[] parts = (arg ?? "").Split('|');
                    float below = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f;
                    if (MenuGaze.TryAimAt(parts[0], out Quaternion rot, below))
                    {
                        SimHead.SetRotation(rot);
                        Plugin.Log.LogInfo($"[cmd] menuaim '{arg}' -> {rot.eulerAngles:F1}");
                    }
                    else Plugin.Log.LogWarning($"[cmd] menuaim: no button '{arg}'");
                    break;
                }
                case "/bye/autoplay":
                    if (arg == "on") AutoPlay.Set(true);
                    else if (arg == "off") AutoPlay.Set(false);
                    else if (arg != null && arg.StartsWith("avoid ")) AutoPlay.Avoid.Add(arg.Substring(6).Trim());
                    else if (arg == "avoid-clear") AutoPlay.Avoid.Clear();
                    else if (arg != null && arg.StartsWith("pause-on ")) AutoPlay.PauseOn = arg.Substring(9).Trim();
                    else if (arg == "pause") AutoPlay.Paused = true;
                    else if (arg == "resume") AutoPlay.Resume();
                    else if (arg == "thorough on") AutoPlay.Thorough = true;
                    else if (arg == "thorough off") AutoPlay.Thorough = false;
                    Plugin.Log.LogInfo($"[cmd] autoplay {(AutoPlay.Enabled ? "on" : "off")}: {AutoPlay.Summary}");
                    break;
                case "/bye/graphics": Veil.LogFullScreenGraphics(string.IsNullOrEmpty(arg) ? null : arg); break;
                case "/bye/veiltest":
                    Veil.ForcedAlpha = 0.6f;
                    Veil.ForcedUntil = Time.unscaledTime + 2f;
                    Plugin.Log.LogInfo($"[cmd] veil test: camera={(UiOverlay.Camera != null ? UiOverlay.Camera.name : "none")} layer={UiOverlay.Layer}");
                    break;
                case "/bye/cfg": SetConfig(arg); break;
                case "/bye/scenes":
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        Plugin.Log.LogInfo($"[cmd] scene {i}: {SceneManager.GetSceneAt(i).name}");
                    break;
                default: Plugin.Log.LogWarning($"[cmd] unknown command {address}"); break;
            }
        }

        /// <summary>
        /// Renders only UUVR's UI overlay camera (UI panel + our head-locked sprites: veils, reticle, cursor, toasts) in
        /// mono to a PNG, black where nothing is drawn. Shows what covers the view, which the eye screenshots can miss.
        /// </summary>
        private static void OverlayShot(string name)
        {
            Camera cam = UiOverlay.Camera;
            if (cam == null) { Plugin.Log.LogWarning("[cmd] overlayshot: no overlay camera"); return; }
            string dir = Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, (string.IsNullOrEmpty(name) ? "overlay" : name) + ".png");
            var rt = new RenderTexture(1600, 900, 24);
            RenderTexture prevTarget = cam.targetTexture, prevActive = RenderTexture.active;
            StereoTargetEyeMask prevEye = cam.stereoTargetEye;
            CameraClearFlags prevClear = cam.clearFlags;
            Color prevBg = cam.backgroundColor;
            float prevFov = cam.fieldOfView;
            try
            {
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.5f, 0f, 0.5f, 1f); // magenta-ish = nothing drawn
                cam.fieldOfView = 90f;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                Plugin.Log.LogInfo($"[cmd] overlayshot -> {path}");
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.stereoTargetEye = prevEye;
                cam.clearFlags = prevClear;
                cam.backgroundColor = prevBg;
                cam.fieldOfView = prevFov;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        private static readonly Dictionary<Transform, Pose> FollowerSnap = new Dictionary<Transform, Pose>();

        /// <summary>
        /// "snap" records every light, particle system and their scripts' transforms; "diff" logs the ones that moved
        /// since (turn the head in between to find scene objects that follow the view/aim).
        /// </summary>
        private static void Followers(string mode)
        {
            var transforms = new HashSet<Transform>();
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) transforms.Add(l.transform);
            foreach (ParticleSystem p in UnityEngine.Object.FindObjectsOfType<ParticleSystem>()) transforms.Add(p.transform);
            if (mode == "list")
            {
                foreach (ParticleSystemRenderer r in UnityEngine.Object.FindObjectsOfType<ParticleSystemRenderer>())
                    Plugin.Log.LogInfo($"[cmd] particles {r.name} enabled={r.enabled} renderMode={r.renderMode} alignment={r.alignment} material={(r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "none")}");
                foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                    Plugin.Log.LogInfo($"[cmd] light {l.name} type={l.type} parent={(l.transform.parent != null ? l.transform.parent.name : "-")} flare={(l.flare != null)}");
                foreach (LensFlare f in UnityEngine.Object.FindObjectsOfType<LensFlare>())
                    Plugin.Log.LogInfo($"[cmd] lensflare {f.name}");
                Camera main = Camera.main;
                if (main != null)
                    Plugin.Log.LogInfo($"[cmd] main camera components: {string.Join(",", main.GetComponents<Behaviour>().Select(c => c.GetType().Name + (c.enabled ? "" : "(off)")).ToArray())}");
                foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
                    string shader = r.sharedMaterial.shader.name;
                    if (shader.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0 || shader.IndexOf("Billboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        r.name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        r.GetComponents<MonoBehaviour>().Any(m => m != null && m.GetType().Name.IndexOf("Billboard", StringComparison.OrdinalIgnoreCase) >= 0 || m != null && m.GetType().Name.IndexOf("LookAt", StringComparison.OrdinalIgnoreCase) >= 0))
                        Plugin.Log.LogInfo($"[cmd] renderer {r.name} shader={shader} scripts={string.Join(",", r.GetComponents<MonoBehaviour>().Where(m => m != null).Select(m => m.GetType().Name).ToArray())}");
                }
                return;
            }
            if (mode == "snap")
            {
                FollowerSnap.Clear();
                foreach (Transform t in transforms) FollowerSnap[t] = new Pose(t.position, t.rotation);
                Plugin.Log.LogInfo($"[cmd] followers snap: {FollowerSnap.Count} lights/particles");
                return;
            }
            foreach (var kv in FollowerSnap)
            {
                Transform t = kv.Key;
                if (t == null) continue;
                float moved = Vector3.Distance(t.position, kv.Value.position);
                float turned = Quaternion.Angle(t.rotation, kv.Value.rotation);
                if (moved < 0.01f && turned < 0.5f) continue;
                string path = t.name;
                for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                string comps = string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name).ToArray());
                string parentComps = t.parent == null ? "" : string.Join(",", t.parent.GetComponents<MonoBehaviour>().Select(c => c.GetType().Name).ToArray());
                Plugin.Log.LogInfo($"[cmd] follower {path} moved={moved:F2} turned={turned:F1} comps=[{comps}] parentScripts=[{parentComps}]");
            }
        }

        private static void Screenshot(string name)
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "BYEFrameBlink_shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, (string.IsNullOrEmpty(name) ? "shot" : name) + ".png");
            if (UnityEngine.XR.XRSettings.enabled)
                ScreenCapture.CaptureScreenshot(path, ScreenCapture.StereoScreenCaptureMode.LeftEye);
            else
                ScreenCapture.CaptureScreenshot(path);
            Plugin.Log.LogInfo($"[cmd] screenshot -> {path}");
        }

        private static string Label(Component c)
        {
            var text = c.GetComponentInChildren<Text>(true);
            if (text != null && !string.IsNullOrEmpty(text.text)) return text.text.Trim();
            var tmp = c.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
            return c.gameObject.name;
        }

        private static void ListButtons()
        {
            foreach (Button b in UnityEngine.Object.FindObjectsOfType<Button>())
                Plugin.Log.LogInfo($"[cmd] button '{Label(b)}' ({b.gameObject.name}) interactable={b.interactable}");
        }

        private static void Click(string label)
        {
            if (string.IsNullOrEmpty(label)) return;
            Button match = UnityEngine.Object.FindObjectsOfType<Button>()
                .FirstOrDefault(b => string.Equals(Label(b), label, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(b.gameObject.name, label, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                Plugin.Log.LogWarning($"[cmd] no active button '{label}'");
                return;
            }
            Plugin.Log.LogInfo($"[cmd] clicking '{Label(match)}' ({match.gameObject.name})");
            match.onClick.Invoke();
        }

        /// <summary>Logs hierarchy, canvas and placement of the first object of a type (or a GameObject by name).</summary>
        private static void Inspect(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            Type type = typeof(BlinkCounter).Assembly.GetType(name) ?? HarmonyLib.AccessTools.TypeByName(name);
            Transform t = null;
            if (type != null && typeof(Component).IsAssignableFrom(type))
                t = (UnityEngine.Object.FindObjectOfType(type) as Component)?.transform;
            if (t == null) t = GameObject.Find(name)?.transform;
            if (t == null)
            {
                Plugin.Log.LogWarning($"[cmd] inspect: '{name}' not found");
                return;
            }

            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            Plugin.Log.LogInfo($"[cmd] inspect {path} active={t.gameObject.activeInHierarchy} layer={t.gameObject.layer}");
            Plugin.Log.LogInfo($"[cmd]   pos={t.position:F3} local={t.localPosition:F3} rot={t.eulerAngles:F1} scale={t.lossyScale:F4}");
            if (t is RectTransform rt)
                Plugin.Log.LogInfo($"[cmd]   rect anchored={rt.anchoredPosition:F1} size={rt.rect.size:F1} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax}");
            Canvas canvas = t.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Canvas root = canvas.rootCanvas;
                Plugin.Log.LogInfo($"[cmd]   canvas={root.name} mode={root.renderMode} camera={(root.worldCamera != null ? root.worldCamera.name : "-")} planeDist={root.planeDistance} scale={root.scaleFactor}");
            }
            Camera main = Camera.main;
            if (main != null)
                Plugin.Log.LogInfo($"[cmd]   Camera.main={main.name} pos={main.transform.position:F3} fov={main.fieldOfView:F1} dist={Vector3.Distance(main.transform.position, t.position):F3}");
            foreach (Component c in t.GetComponents<Component>())
                Plugin.Log.LogInfo($"[cmd]   component {c.GetType().FullName}");
        }

        /// <summary>
        /// "/bye/fields name": logs every field of every MonoBehaviour on a GameObject (found by name even when inactive),
        /// with object references resolved to name/active/position, plus its direct children.
        /// </summary>
        private static void Fields(string name)
        {
            Transform[] matches = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(x => x.gameObject.scene.IsValid() && string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.gameObject.activeInHierarchy).Take(6).ToArray();
            if (matches.Length == 0) { Plugin.Log.LogWarning($"[cmd] fields: '{name}' not found"); return; }
            foreach (Transform t in matches) FieldsOf(t);
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static void FieldsOf(Transform t)
        {
            Plugin.Log.LogInfo($"[cmd] fields {PathOf(t)} activeSelf={t.gameObject.activeSelf} activeInHierarchy={t.gameObject.activeInHierarchy} pos={t.position:F3} layer={t.gameObject.layer} tag={t.tag}");
            foreach (Transform c in t) Plugin.Log.LogInfo($"[cmd]   child {c.name} active={c.gameObject.activeSelf} pos={c.position:F3}");
            foreach (Collider col in t.GetComponentsInChildren<Collider>(true))
                Plugin.Log.LogInfo($"[cmd]   collider {col.name} enabled={col.enabled} active={col.gameObject.activeInHierarchy} layer={col.gameObject.layer} tag={col.tag} center={col.bounds.center:F3}");
            foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                Plugin.Log.LogInfo($"[cmd]   {mb.GetType().Name} enabled={mb.enabled}");
                for (Type ty = mb.GetType(); ty != null && ty != typeof(MonoBehaviour); ty = ty.BaseType)
                    foreach (FieldInfo f in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        object v;
                        try { v = f.GetValue(mb); } catch { continue; }
                        Plugin.Log.LogInfo($"[cmd]     {f.Name} = {Describe(v)}");
                    }
            }
        }

        private static string Describe(object v, int depth = 0)
        {
            switch (v)
            {
                case null: return "null";
                case GameObject go: return go == null ? "null(destroyed)" : $"'{go.name}' active={go.activeInHierarchy} pos={go.transform.position:F3}";
                case Component c: return c == null ? "null(destroyed)" : $"{c.GetType().Name}@'{c.name}' active={c.gameObject.activeInHierarchy}";
                case UnityEngine.Object o: return o == null ? "null(destroyed)" : $"{o.GetType().Name} '{o.name}'";
                case UnityEngine.Events.UnityEventBase ev:
                    var calls = new List<string>();
                    for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                    {
                        UnityEngine.Object tgt = ev.GetPersistentTarget(i);
                        calls.Add($"{(tgt != null ? tgt.GetType().Name + "@'" + tgt.name + "'" : "null")}.{ev.GetPersistentMethodName(i)}");
                    }
                    return "event[" + string.Join(", ", calls.ToArray()) + "]";
                case string s: return $"\"{s}\"";
                case System.Collections.IEnumerable list when depth == 0:
                    var items = new List<string>();
                    foreach (object e in list) { if (items.Count >= 8) { items.Add("..."); break; } items.Add(Describe(e, 1)); }
                    return "[" + string.Join(", ", items.ToArray()) + "]";
                default:
                    Type ty = v.GetType();
                    if (depth == 0 && ty.IsValueType && !ty.IsPrimitive && !ty.IsEnum && ty.Namespace == null)
                        return "{" + string.Join(", ", ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .Select(f => f.Name + "=" + Describe(f.GetValue(v), 1)).ToArray()) + "}";
                    return v.ToString();
            }
        }

        /// <summary>"pluginGuid|Section|Key|value": sets a BepInEx config entry of any loaded plugin, live.</summary>
        private static void SetConfig(string spec)
        {
            string[] parts = spec?.Split('|');
            if (parts == null || parts.Length != 4)
            {
                Plugin.Log.LogWarning("[cmd] cfg expects guid|Section|Key|value");
                return;
            }
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(parts[0], out var info))
            {
                Plugin.Log.LogWarning($"[cmd] cfg: plugin {parts[0]} not loaded");
                return;
            }
            var config = info.Instance.Config;
            var def = new BepInEx.Configuration.ConfigDefinition(parts[1], parts[2]);
            if (!config.ContainsKey(def))
            {
                Plugin.Log.LogWarning($"[cmd] cfg: {parts[1]}/{parts[2]} not found in {parts[0]}");
                return;
            }
            var entry = config[def];
            entry.BoxedValue = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(parts[3], entry.SettingType);
            Plugin.Log.LogInfo($"[cmd] cfg {parts[0]} {parts[1]}/{parts[2]} = {entry.BoxedValue}");
        }

        /// <summary>"Type.Method" or "Type.Method:arg" (one bool/int/float/string argument).</summary>
        private static void Invoke(string target)
        {
            string argText = null;
            int colon = target?.IndexOf(':') ?? -1;
            if (colon > 0)
            {
                argText = target.Substring(colon + 1);
                target = target.Substring(0, colon);
            }
            int dot = target?.LastIndexOf('.') ?? -1;
            if (dot <= 0) return;
            string typeName = target.Substring(0, dot), methodName = target.Substring(dot + 1);
            Type type = typeof(BlinkCounter).Assembly.GetType(typeName) ?? HarmonyLib.AccessTools.TypeByName(typeName);
            MethodInfo method = null;
            object[] args = null;
            if (type != null && argText == null)
            {
                method = HarmonyLib.AccessTools.Method(type, methodName, Type.EmptyTypes);
            }
            else if (type != null)
            {
                foreach (MethodInfo m in HarmonyLib.AccessTools.GetDeclaredMethods(type))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (m.Name != methodName || ps.Length != 1) continue;
                    try
                    {
                        args = new[] { Convert.ChangeType(argText, ps[0].ParameterType, System.Globalization.CultureInfo.InvariantCulture) };
                        method = m;
                        break;
                    }
                    catch { }
                }
            }
            if (method == null)
            {
                Plugin.Log.LogWarning($"[cmd] method {target} not found");
                return;
            }
            object instance = null;
            if (!method.IsStatic)
            {
                instance = UnityEngine.Object.FindObjectOfType(type);
                if (instance == null)
                {
                    Plugin.Log.LogWarning($"[cmd] no active {typeName}");
                    return;
                }
            }
            Plugin.Log.LogInfo($"[cmd] invoking {target}");
            method.Invoke(instance, args);
        }
    }
}
