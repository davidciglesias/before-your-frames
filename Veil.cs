using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game darkens the view with UI images on screen-space canvases: BlinkCanvasManager's upper/lower eyelids
    /// (full-canvas images that slide in), SceneTransFadePanel, menu backdrops... UUVR shows those canvases on a
    /// ~48x27 degree panel, so in VR they became a dark rectangle with the scene visible around it.
    ///
    /// Each such image is mirrored as a head-locked sprite through UUVR's UI overlay camera, with its canvas rectangle
    /// stretched to the whole field of view (canvas edge = edge of view), using the same sprite, color and opacity;
    /// the original is hidden so the panel stays clear. Text and other HUD elements still draw on top (UUVR's panel
    /// renders after these sprites).
    /// </summary>
    internal static class Veil
    {
        private const float Distance = 0.5f; // meters in front of the eyes (overlay camera near clip is 0.3)

        // Sprites sort before UUVR's UI panel quad (order 0) by sorting order, whatever their render queue, so the
        // panel (menu text, dialogs, HUD icons) draws over the veils. A positive order hid a quit dialog under its backdrop.
        private const int BehindPanelOrder = -1000;

        private sealed class Mirror
        {
            public Image Source;
            public SpriteRenderer Copy;
            public bool IsLid;
        }

        private static readonly List<Mirror> Mirrors = new List<Mirror>();
        private static readonly Dictionary<Image, Mirror> BySource = new Dictionary<Image, Mirror>();
        private static BlinkCanvasManager _lids;
        private static float _nextScan;
        private static bool _active;

        public static float UpperEdgeFrac { get; private set; } = 1f;   // canvas fraction from center, + up
        public static float LowerEdgeFrac { get; private set; } = -1f;
        public static float UpperAlpha { get; private set; }
        public static float LowerAlpha { get; private set; }
        public static float FullAlpha { get; private set; }
        public static bool Ready => _active;
        public static int FullScreenDarkCount => Mirrors.Count - (_lids != null ? 2 : 0);
        public static bool OriginalsHidden { get; private set; }

        /// <summary>For tests: every mirrored veil draws before (under) UUVR's UI panel.</summary>
        public static bool AllBehindPanel
        {
            get
            {
                foreach (Mirror m in Mirrors)
                    if (m.Copy != null && m.Copy.sortingOrder >= 0) return false;
                return true;
            }
        }

        /// <summary>Debug: force a full-view veil of this opacity until the given time.</summary>
        public static float ForcedAlpha;
        public static float ForcedUntil;
        private static SpriteRenderer _forced;

        public static void LateUpdate()
        {
            if (!Plugin.FullViewVeil.Value || !UnityEngine.XR.XRSettings.enabled || UiOverlay.Camera == null)
            {
                if (_active) Deactivate();
                return;
            }
            _active = true;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Rescan();
            }

            float halfH = Distance * Mathf.Tan(UiOverlay.VeilHalfFovDegrees * Mathf.Deg2Rad);

            UpperAlpha = LowerAlpha = FullAlpha = 0f;
            UpperEdgeFrac = 1f;
            LowerEdgeFrac = -1f;
            bool hidden = true;

            foreach (Mirror m in Mirrors)
            {
                if (m.Source == null) { if (m.Copy != null) m.Copy.enabled = false; continue; }
                Image src = m.Source;
                Canvas canvas = src.canvas != null ? src.canvas.rootCanvas : null;
                bool on = src.isActiveAndEnabled && canvas != null && canvas.renderMode != RenderMode.WorldSpace;
                float alpha = on ? src.color.a * GroupAlpha(src) : 0f;
                if (m.Copy == null) m.Copy = UiOverlay.CreateSprite("BYE Veil " + src.name, BehindPanelOrder + Mirrors.IndexOf(m));
                m.Copy.enabled = alpha > 0.001f;

                if (on)
                {
                    // Canvas-local rectangle -> fraction of canvas from center -> meters on the view plane.
                    var canvasRt = (RectTransform)canvas.transform;
                    Rect cr = canvasRt.rect;
                    var corners = new Vector3[4];
                    src.rectTransform.GetWorldCorners(corners);
                    Vector3 a = canvasRt.InverseTransformPoint(corners[0]), b = canvasRt.InverseTransformPoint(corners[2]);
                    float fx0 = Mathf.Min(a.x, b.x) / cr.height, fx1 = Mathf.Max(a.x, b.x) / cr.height; // uniform: use height
                    float fy0 = Mathf.Min(a.y, b.y) / cr.height, fy1 = Mathf.Max(a.y, b.y) / cr.height;

                    if (m.IsLid)
                    {
                        if (src == _lids._upperLid) { UpperEdgeFrac = fy0; UpperAlpha = alpha; }
                        else { LowerEdgeFrac = fy1; LowerAlpha = alpha; }
                    }
                    else FullAlpha = Mathf.Max(FullAlpha, alpha);

                    float x0 = fx0 * 2f * halfH, x1 = fx1 * 2f * halfH, y0 = fy0 * 2f * halfH, y1 = fy1 * 2f * halfH;
                    bool extended = false;
                    // Full-width images get extended sideways so they cover any FOV.
                    if (fx1 - fx0 >= cr.width / cr.height * 0.95f) { x0 -= 10f; x1 += 10f; extended = true; }
                    // Lids/fades that reach the canvas' top/bottom edge also extend past the view's edge.
                    if (fy1 >= 0.49f) { y1 += 10f; extended = true; }
                    if (fy0 <= -0.49f) { y0 -= 10f; extended = true; }

                    // Extended images use a plain sprite (stretching a gradient over the extension would smear it).
                    Sprite sprite = !extended && src.sprite != null && src.type == Image.Type.Simple ? src.sprite : UiOverlay.WhiteSprite;
                    m.Copy.sprite = sprite;
                    Color c = src.color;
                    c.a = alpha;
                    m.Copy.color = c;
                    Vector2 size = sprite.bounds.size;
                    m.Copy.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, Distance);
                    m.Copy.transform.localRotation = Quaternion.identity;
                    m.Copy.transform.localScale = new Vector3((x1 - x0) / Mathf.Max(1e-5f, size.x), (y1 - y0) / Mathf.Max(1e-5f, size.y), 1f);
                }

                src.canvasRenderer.SetAlpha(0f); // hide the original on UUVR's panel
                hidden &= src.canvasRenderer.GetAlpha() < 0.001f;
            }
            OriginalsHidden = hidden;

            // Debug forced veil.
            bool forcedOn = Time.unscaledTime < ForcedUntil;
            if (forcedOn && _forced == null)
            {
                _forced = UiOverlay.CreateSprite("BYE Veil Forced", 31000);
                _forced.sprite = UiOverlay.WhiteSprite;
            }
            if (_forced != null)
            {
                _forced.enabled = forcedOn;
                _forced.color = new Color(0f, 0f, 0f, ForcedAlpha);
                _forced.transform.localPosition = new Vector3(0f, 0f, Distance);
                _forced.transform.localScale = new Vector3(20f, 20f, 1f);
                if (forcedOn) FullAlpha = Mathf.Max(FullAlpha, ForcedAlpha);
            }
        }

        /// <summary>Product of the CanvasGroup alphas above the graphic (what the game uses to fade groups).</summary>
        private static float GroupAlpha(Graphic g)
        {
            float a = 1f;
            Transform t = g.transform;
            while (t != null)
            {
                CanvasGroup cg = t.GetComponent<CanvasGroup>();
                if (cg != null && cg.enabled)
                {
                    a *= cg.alpha;
                    if (cg.ignoreParentGroups) break;
                }
                if (t.GetComponent<Canvas>() is Canvas c && c.isRootCanvas) break;
                t = t.parent;
            }
            return a;
        }

        private static void Rescan()
        {
            if (_lids == null)
            {
                _lids = Object.FindObjectOfType<BlinkCanvasManager>();
                if (_lids != null)
                {
                    Add(_lids._upperLid, true);
                    Add(_lids._lowerLid, true);
                }
            }

            // Include inactive objects so menus/fades are hidden before their first frame on screen.
            foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img == null || BySource.ContainsKey(img) || img.gameObject.scene.name == null) continue;
                Canvas canvas = img.canvas != null ? img.canvas.rootCanvas : img.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
                Color c = img.color;
                if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.15f) continue; // only dark fades/backdrops
                var canvasRt = (RectTransform)canvas.transform;
                Rect cr = canvasRt.rect;
                if (cr.width <= 0f || cr.height <= 0f) continue;
                var corners = new Vector3[4];
                img.rectTransform.GetWorldCorners(corners);
                Vector3 a = canvasRt.InverseTransformPoint(corners[0]), b = canvasRt.InverseTransformPoint(corners[2]);
                if (Mathf.Abs(b.x - a.x) >= cr.width * 0.9f && Mathf.Abs(b.y - a.y) >= cr.height * 0.9f) Add(img, false);
            }

            // Drop destroyed sources (scene unloads).
            for (int i = Mirrors.Count - 1; i >= 0; i--)
            {
                if (Mirrors[i].Source != null) continue;
                if (Mirrors[i].Copy != null) Object.Destroy(Mirrors[i].Copy.gameObject);
                Mirrors.RemoveAt(i);
            }
            var dead = new List<Image>();
            foreach (var kv in BySource) if (kv.Key == null) dead.Add(kv.Key);
            foreach (Image d in dead) BySource.Remove(d);
        }

        private static void Add(Image img, bool isLid)
        {
            if (img == null || BySource.ContainsKey(img)) return;
            var m = new Mirror { Source = img, IsLid = isLid };
            Mirrors.Add(m);
            BySource[img] = m;
        }

        private static void Deactivate()
        {
            _active = false;
            foreach (Mirror m in Mirrors)
            {
                if (m.Copy != null) m.Copy.enabled = false;
                if (m.Source != null) m.Source.canvasRenderer.SetAlpha(1f);
            }
        }

        /// <summary>Debug: logs every UI graphic that covers (almost) the whole canvas.</summary>
        public static void LogFullScreenGraphics(string canvasFilter = null)
        {
            foreach (Mirror m in Mirrors)
            {
                if (m.Copy == null) continue;
                Transform t = m.Copy.transform;
                Plugin.Log.LogInfo($"[cmd] mirror {(m.Source != null ? m.Source.name : "?")} lid={m.IsLid} enabled={m.Copy.enabled} sprite={(m.Copy.sprite != null ? m.Copy.sprite.name : "-")} " +
                                   $"color={m.Copy.color} local={t.localPosition:F3} scale={t.localScale:F3} lossy={t.lossyScale:F3} parent={t.parent?.name} layer={t.gameObject.layer}");
            }
            foreach (Graphic g in Resources.FindObjectsOfTypeAll<Graphic>())
            {
                if (g == null || g.gameObject.scene.name == null) continue;
                Canvas canvas = g.canvas != null ? g.canvas.rootCanvas : null;
                if (canvas == null) continue;
                var canvasRt = (RectTransform)canvas.transform;
                var corners = new Vector3[4];
                g.rectTransform.GetWorldCorners(corners);
                Vector3 a = canvasRt.InverseTransformPoint(corners[0]), b = canvasRt.InverseTransformPoint(corners[2]);
                float w = Mathf.Abs(b.x - a.x) / Mathf.Max(1f, canvasRt.rect.width);
                float h = Mathf.Abs(b.y - a.y) / Mathf.Max(1f, canvasRt.rect.height);
                if (canvasFilter != null ? canvas.name != canvasFilter : (w < 0.5f || h < 0.5f)) continue;
                string path = g.name;
                for (Transform p = g.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                Plugin.Log.LogInfo($"[cmd] graphic {g.GetType().Name} {path} active={g.isActiveAndEnabled} color={g.color} alpha={g.canvasRenderer.GetAlpha():F2} cover={w:F2}x{h:F2} canvas={canvas.name}/{canvas.renderMode}");
            }
        }
    }
}
