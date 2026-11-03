using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// A "vr settings" entry in the main menu and the pause menu. It's a copy of that menu's own Settings button,
    /// placed right below it (the buttons under it move down a step), so it looks and sounds like the game's and works
    /// with gaze + blink like any other option. Opening it swaps that menu's buttons for our options, laid out in the
    /// same column; "back" (or Escape, or the controller Menu button) swaps them back.
    /// </summary>
    internal static class VrSettingsMenu
    {
        private sealed class Row
        {
            public Button Button;
            public Text Text;
            public TMPro.TMP_Text Tmp;
            public Func<string> Label;
            public float BaseSize = -1f; // font size as built, before any shrinking to fit
        }

        /// <summary>Text at this fraction of its built size (long translations shrink to fit the view).</summary>
        private static void SetTextScale(Row r, float f)
        {
            if (r.Text != null)
            {
                if (r.BaseSize < 0f) r.BaseSize = r.Text.fontSize;
                int size = Mathf.Max(1, Mathf.RoundToInt(r.BaseSize * f));
                if (r.Text.fontSize != size) r.Text.fontSize = size;
            }
            else if (r.Tmp != null)
            {
                if (r.BaseSize < 0f) r.BaseSize = r.Tmp.fontSize;
                r.Tmp.fontSize = r.BaseSize * f;
            }
        }

        private sealed class Context
        {
            public GameObject Host;
            public Button Template;
            public Button Entry;
            public bool IsSettings; // our "settings" page (subtitles), standing in for the game's in VR
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<GameObject> Hidden = new List<GameObject>();
            public UnityEngine.Object Audio;
        }

        private static Context _main, _pause, _mainS, _pauseS, _open;
        private static float _nextScan;

        public static bool IsOpen => _open != null;

        private static IEnumerable<Context> All => new[] { _main, _pause, _mainS, _pauseS }.Where(c => c != null);

        /// <summary>One of our own settings rows (laid out by us, from the spacing of the menu's buttons).</summary>
        public static bool IsRow(Selectable s) => All.Any(c => c.Rows.Any(r => r.Button == s));
        public static int Entries => (_main?.Entry != null ? 1 : 0) + (_pause?.Entry != null ? 1 : 0);
        public static int OpenRows => _open == null ? 0 : _open.Rows.Count(r => r.Button != null && r.Button.gameObject.activeInHierarchy);

        public static void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                MainMenuController mmc = MainMenuController.Instance;
                PauseMenuController pmc = PauseMenuController.instance;
                Ensure(ref _main, mmc != null ? mmc.mainMenu : null, false);
                Ensure(ref _pause, pmc != null ? pmc.pauseMenu : null, false);
                Ensure(ref _mainS, mmc != null ? mmc.mainMenu : null, true);
                Ensure(ref _pauseS, pmc != null ? pmc.pauseMenu : null, true);
                // In VR the game's own settings page (dropdowns, webcam and window options) is replaced by ours.
                bool vr = UnityEngine.XR.XRSettings.enabled;
                foreach (Context c in All)
                    if (c.Entry != null) SetLabel(new Row { Text = c.Entry.GetComponentInChildren<Text>(true), Tmp = c.Entry.GetComponentInChildren<TMPro.TMP_Text>(true) }, c.IsSettings ? MenuText.SubtitlesEntry : MenuText.VrSettingsEntry);
                foreach (Context c in new[] { _mainS, _pauseS })
                {
                    if (c == null || c.Template == null || c.Entry == null) continue;
                    if (c.Template.gameObject.activeSelf == vr && _open != c) c.Template.gameObject.SetActive(!vr);
                    if (c.Entry.gameObject.activeSelf != vr && _open == null) c.Entry.gameObject.SetActive(vr);
                }
            }
            if (_open == null) return;

            if (!ContextAlive(_open)) Close(false);
            else if (Input.GetKeyUp(KeyCode.Escape) && (_open == _main || _open == _mainS)) Close(true);
            else foreach (Row r in _open.Rows) SetLabel(r, r.Label());
        }

        private static bool ContextAlive(Context c)
        {
            if (c.Host == null || !c.Host.activeInHierarchy) return false;
            if (c == _pause || c == _pauseS) return PauseMenuController.instance != null && PauseMenuController.instance.activeMenu == PauseMenuController.ActiveMenu.Pause;
            return MainMenuController.Instance != null && MainMenuController.Instance.activeMenu == MainMenuController.ActiveMenu.Main;
        }

        /// <summary>Back out of the VR settings (to the menu they were opened from). False if they weren't open.</summary>
        public static bool Back()
        {
            if (_open == null) return false;
            Close(true);
            return true;
        }

        private static void Ensure(ref Context ctx, GameObject host, bool settings)
        {
            if (host == null) return;
            if (ctx != null && ctx.Host == host && ctx.Entry != null) return;
            Button[] buttons = host.GetComponentsInChildren<Button>(true).Where(b => b.transform.parent == host.transform).ToArray();
            Button template = buttons.FirstOrDefault(b => b.name == "Settings Button")
                ?? buttons.FirstOrDefault(b => b.name.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0
                                               && !b.name.StartsWith("VR ") && !b.name.StartsWith("BYE "));
            if (template == null) return;

            // Measure the game's own layout, not our two-column spread of it (side by side, rows share a height, which
            // read as no spacing and put our entry on top of another when the menu came back after a scene).
            MenuSpread.RestoreAll();
            var c = new Context { Host = host, Template = template, Audio = FindAudio(template), IsSettings = settings };
            var t = (RectTransform)template.transform;
            if (settings)
            {
                // Ours in the game's place; the game's is hidden while in VR (Update).
                c.Entry = Clone(c, "BYE Subtitles Button", MenuText.SubtitlesEntry, () => Open(c), false);
                ((RectTransform)c.Entry.transform).anchoredPosition = t.anchoredPosition;
                c.Entry.transform.SetSiblingIndex(t.GetSiblingIndex());
                c.Entry.gameObject.SetActive(false);
                ctx = c;
                Plugin.Log.LogInfo($"Added our subtitles page to {host.name}.");
                return;
            }
            List<RectTransform> column = Column(c);
            float step = Step(column);

            c.Entry = Clone(c, "VR Settings Button", MenuText.VrSettingsEntry, () => Open(c), false);
            var e = (RectTransform)c.Entry.transform;
            e.anchoredPosition = t.anchoredPosition + new Vector2(0f, step);
            e.SetSiblingIndex(t.GetSiblingIndex() + 1);
            foreach (RectTransform r in column)
                if (r.anchoredPosition.y < t.anchoredPosition.y - 0.01f) r.anchoredPosition += new Vector2(0f, step);

            ctx = c;
            Plugin.Log.LogInfo($"Added 'vr settings' to {host.name}.");
        }

        /// <summary>The menu's active buttons (its column), top to bottom, without ours.</summary>
        private static List<RectTransform> Column(Context c) =>
            c.Template.transform.parent.GetComponentsInChildren<Selectable>(false)
                .Where(s => s.transform.parent == c.Template.transform.parent && s != c.Entry && !c.Rows.Any(r => r.Button == s))
                .Select(s => (RectTransform)s.transform)
                .OrderByDescending(r => r.anchoredPosition.y)
                .ToList();

        private static float Step(List<RectTransform> column)
        {
            var gaps = new List<float>();
            for (int i = 1; i < column.Count; i++)
            {
                float g = column[i].anchoredPosition.y - column[i - 1].anchoredPosition.y;
                if (Mathf.Abs(g) > 1f) gaps.Add(g); // buttons at one height (side by side) are no spacing
            }
            if (gaps.Count == 0) return -40f;
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static void Open(Context c)
        {
            if (_open != null) return;
            MenuSpread.RestoreAll(); // lay our rows out from the game's own spacing
            List<RectTransform> column = Column(c);
            column.Add((RectTransform)c.Entry.transform);
            column = column.OrderByDescending(r => r.anchoredPosition.y).ToList();
            float step = Step(column);
            Vector2 top = column.Count > 0 ? column[0].anchoredPosition : ((RectTransform)c.Template.transform).anchoredPosition;
            if (c.Rows.Count == 0) BuildRows(c);
            // Two columns, so the options get the same roomy spacing as the other menus (one column of all of them
            // ran off the menu), and back sits on its own below them like the game's back buttons.
            float gameStep = step;
            int options = c.Rows.Count - 1, perColumn = (options + 1) / 2;
            // Spread the rows over the height the single column took at the game's spacing (it just fit), so the
            // options are far apart: in two columns there are half as many rows. Back sits 1.5 rows below.
            if (UnityEngine.XR.XRSettings.enabled)
                step = gameStep * Mathf.Max(Mathf.Clamp(Plugin.MenuSpacing.Value, 1f, 2.5f), (c.Rows.Count - 1) / (perColumn - 1 + 1.5f));
            float width = Mathf.Max(((RectTransform)c.Template.transform).rect.width, 200f);
            // Second column's text starts a margin after the first column's widest text ends. Spans account for the
            // labels' alignment: the main menu's are left-aligned, the pause menu's centred.
            for (int i = 0; i < c.Rows.Count; i++) { SetLabel(c.Rows[i], c.Rows[i].Label()); SetTextScale(c.Rows[i], 1f); }
            float dx = Measure(c, perColumn, options, width, out float total);
            // Too wide for the comfortable middle of the view (long translations): shrink the text to fit.
            float avail = UsableWidth(c);
            if (avail > 0f && total > avail)
            {
                float f = Mathf.Max(0.6f, avail / total);
                foreach (Row r in c.Rows) SetTextScale(r, f);
                dx = Measure(c, perColumn, options, width, out total);
                Plugin.Log.LogInfo($"VR settings text at {f:P0} to fit the view.");
            }

            c.Hidden.Clear();
            foreach (RectTransform r in column)
            {
                if (!r.gameObject.activeSelf) continue;
                r.gameObject.SetActive(false);
                c.Hidden.Add(r.gameObject);
            }
            for (int i = 0; i < c.Rows.Count; i++)
            {
                Row row = c.Rows[i];
                Vector2 at = i == options
                    ? top + new Vector2(0f, step * (perColumn + 0.5f))
                    : top + new Vector2(i < perColumn ? 0f : dx, step * (i % perColumn));
                ((RectTransform)row.Button.transform).anchoredPosition = at;
                SetLabel(row, row.Label());
                row.Button.gameObject.SetActive(true);
            }
            KeepOnCanvas(c, perColumn, options);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _open = c;
            Plugin.Log.LogInfo($"VR settings opened ({perColumn} rows per column, second column at +{dx:F0}).");
        }

        /// <summary>Gap between the first column's widest text and the second column, in canvas units.</summary>
        private const float ColumnMargin = 100f;

        /// <summary>The page stays out of this fraction of the canvas on each side: its edges are at the edge of the view.</summary>
        private const float EdgeFraction = 0.1f;

        /// <summary>The second column's offset; total = the two columns' text plus the gap, in the rows' local units.</summary>
        private static float Measure(Context c, int perColumn, int options, float fallback, out float total)
        {
            float firstLeft = float.MaxValue, firstRight = float.MinValue, secondLeft = float.MaxValue, secondRight = float.MinValue;
            for (int i = 0; i < options; i++)
            {
                Vector2 span = TextSpan(c.Rows[i]);
                if (i < perColumn) { firstLeft = Mathf.Min(firstLeft, span.x); firstRight = Mathf.Max(firstRight, span.y); }
                else { secondLeft = Mathf.Min(secondLeft, span.x); secondRight = Mathf.Max(secondRight, span.y); }
            }
            if (firstRight == float.MinValue || secondLeft == float.MaxValue) { total = 0f; return fallback; }
            total = (firstRight - firstLeft) + ColumnMargin + (secondRight - secondLeft);
            return firstRight - secondLeft + ColumnMargin;
        }

        /// <summary>The canvas width minus the edges to keep clear, in the rows' local units (0 if unknown).</summary>
        private static float UsableWidth(Context c)
        {
            Canvas canvas = c.Host.GetComponentInParent<Canvas>();
            if (canvas == null || c.Rows.Count == 0) return 0f;
            var root = (RectTransform)canvas.rootCanvas.transform;
            Transform parent = c.Rows[0].Button.transform.parent;
            return root.rect.width * (1f - 2f * EdgeFraction) * root.lossyScale.x / Mathf.Max(1e-5f, parent.lossyScale.x);
        }

        /// <summary>
        /// Menus sit at different places on their canvas (the pause menu further right), so the page can run past an
        /// edge: measure all text against the canvas and slide the rows back inside.
        /// </summary>
        private static void KeepOnCanvas(Context c, int perColumn, int options)
        {
            Canvas canvas = c.Host.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = (RectTransform)canvas.rootCanvas.transform;
            Transform parent = c.Rows[0].Button.transform.parent;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < c.Rows.Count; i++)
            {
                var rt = (RectTransform)c.Rows[i].Button.transform;
                Vector2 span = TextSpan(c.Rows[i]);
                min = Mathf.Min(min, root.InverseTransformPoint(rt.TransformPoint(new Vector3(span.x, 0f, 0f))).x);
                max = Mathf.Max(max, root.InverseTransformPoint(rt.TransformPoint(new Vector3(span.y, 0f, 0f))).x);
            }
            float edge = root.rect.width * EdgeFraction;
            float lo = root.rect.xMin + edge, hi = root.rect.xMax - edge, shift = 0f;
            if (max - min > hi - lo) shift = (lo + hi) * 0.5f - (min + max) * 0.5f; // still too wide: centre it
            else if (max > hi) shift = hi - max;
            else if (min < lo) shift = lo - min;
            if (Mathf.Abs(shift) < 0.5f) return;
            float local = shift * root.lossyScale.x / Mathf.Max(1e-5f, parent.lossyScale.x);
            foreach (Row r in c.Rows) ((RectTransform)r.Button.transform).anchoredPosition += new Vector2(local, 0f);
            Plugin.Log.LogInfo($"VR settings moved {local:F0} to stay on the menu.");
        }

        private static float TextWidth(Row r) =>
            r.Tmp != null ? r.Tmp.preferredWidth : r.Text != null ? r.Text.preferredWidth : 0f;

        /// <summary>Left and right of a menu button's visible text, in its own local x (for MenuSpread's columns).</summary>
        internal static Vector2 TextSpanOf(Selectable s) =>
            TextSpan(new Row { Button = s as Button, Text = s.GetComponentInChildren<Text>(true), Tmp = s.GetComponentInChildren<TMPro.TMP_Text>(true) });

        /// <summary>Left and right of a row's visible text, in its button's local x.</summary>
        private static Vector2 TextSpan(Row r)
        {
            var button = (RectTransform)r.Button.transform;
            RectTransform tr = r.Tmp != null ? r.Tmp.rectTransform : r.Text != null ? r.Text.rectTransform : button;
            float a = 0f;
            if (r.Tmp != null)
            {
                string h = r.Tmp.alignment.ToString(); // older TMP: one enum, e.g. TopLeft, Center, MidlineRight
                a = h.EndsWith("Right") ? 1f : h.EndsWith("Left") ? 0f : 0.5f;
            }
            else if (r.Text != null)
            {
                int col = (int)r.Text.alignment % 3; // UpperLeft, UpperCenter, UpperRight, MiddleLeft, ...
                a = col * 0.5f;
            }
            // Text that doesn't wrap (our rows overflow) runs past its rect, so measure all of it; wrapped text stays inside.
            bool wraps = r.Tmp != null ? r.Tmp.enableWordWrapping : r.Text != null && r.Text.horizontalOverflow == HorizontalWrapMode.Wrap;
            float w = wraps ? Mathf.Min(TextWidth(r), tr.rect.width) : TextWidth(r);
            float left = tr.rect.xMin + a * (tr.rect.width - w);
            float l = button.InverseTransformPoint(tr.TransformPoint(new Vector3(left, 0f, 0f))).x;
            float rr = button.InverseTransformPoint(tr.TransformPoint(new Vector3(left + w, 0f, 0f))).x;
            return new Vector2(l, rr);
        }

        private static void Close(bool restore)
        {
            Context c = _open;
            _open = null;
            if (c == null) return;
            foreach (Row r in c.Rows) if (r.Button != null) r.Button.gameObject.SetActive(false);
            // Put the menu's own buttons back even when it was closed under us (resumed), so it's whole next time.
            foreach (GameObject g in c.Hidden) if (g != null) g.SetActive(true);
            c.Hidden.Clear();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Plugin.Log.LogInfo(restore ? "VR settings closed." : "VR settings closed with their menu.");
        }

        private static void BuildRows(Context c)
        {
            if (c.IsSettings) { BuildSettingsRows(c); return; }
            BuildVrRows(c);
        }

        /// <summary>Our "settings": each subtitle choice (also the text language) as its own option, so it's one look and blink.</summary>
        private static void BuildSettingsRows(Context c)
        {
            foreach (string option in Subtitles.Options)
            {
                string o = option;
                AddRow(c, "BYE Subtitles " + o, () => Subtitles.Caption(o), () => Subtitles.Set(o));
            }
            AddRow(c, "BYE Settings Back Button", () => MenuText.Back, () => Close(true), true);
        }

        private static void BuildVrRows(Context c)
        {
            AddRow(c, "VR Eye Tracking Button", () => MenuText.Row("eye tracking", Plugin.EyeTracking.Value == EyeTrackingMode.Off ? "off (head aim)" : !Plugin.GazeAiming.Value ? "blinks only (head aim)" : OscReceiver.ActiveSourceLabel ?? "auto (no data)"), Plugin.ToggleEyeTracking);
            AddRow(c, "VR World Size Button", () => MenuText.Row("world size", StereoComfort.EyeSeparationLabel), StereoComfort.CycleEyeSeparation);
            AddRow(c, "VR Aiming Dot Button", () => MenuText.Row("aiming dot", OnOff(Plugin.ShowReticle.Value)), Plugin.ToggleReticle);
            AddRow(c, "VR Recenter Start Button", () => MenuText.Row("recenter each scene", OnOff(Plugin.RecenterOnSceneStart.Value)), Plugin.ToggleRecenterOnSceneStart);
            AddRow(c, "VR Recenter Button", () => MenuText.Row("recenter view now", null), () => HeadTracking.Recenter());
            AddRow(c, "VR Render Quality Button", () => MenuText.Row("render quality (in game)", RenderScale.Label), RenderScale.Cycle);
            AddRow(c, "VR Performance Button", () => MenuText.Row("performance mode", Performance.Label), Performance.Cycle);
            AddRow(c, "VR Level Horizon Button", () => MenuText.Row("level horizon", OnOff(Plugin.LevelHorizon.Value != LevelHorizonMode.Off)), Plugin.ToggleLevelHorizon);
            AddRow(c, "VR Back Button", () => MenuText.Back, () => Close(true), true);
        }

        private static void AddRow(Context c, string name, Func<string> label, Action action, bool isBack = false)
        {
            Button b = Clone(c, name, label(), action, isBack);
            b.gameObject.SetActive(false);
            c.Rows.Add(new Row
            {
                Button = b,
                Text = b.GetComponentInChildren<Text>(true),
                Tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true),
                Label = label,
            });
        }

        private static Button Clone(Context c, string name, string label, Action action, bool isBack)
        {
            GameObject go = UnityEngine.Object.Instantiate(c.Template.gameObject, c.Template.transform.parent, false);
            go.name = name;
            // The game's localization puts the original caption ("settings") back every time the text is enabled.
            foreach (LocalizationWorldText loc in go.GetComponentsInChildren<LocalizationWorldText>(true)) UnityEngine.Object.DestroyImmediate(loc);
            Button b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.interactable = true;
            UnityEngine.Object audio = c.Audio;
            b.onClick.AddListener(() =>
            {
                PlaySound(audio, isBack ? "ButtonSelectBack" : "ButtonSelect");
                action();
            });
            Text text = go.GetComponentInChildren<Text>(true);
            if (text != null) text.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetLabel(new Row { Text = text, Tmp = go.GetComponentInChildren<TMPro.TMP_Text>(true) }, label);
            return b;
        }

        private static void SetLabel(Row r, string label)
        {
            if (r.Text != null && r.Text.text != label) r.Text.text = label;
            else if (r.Text == null && r.Tmp != null && r.Tmp.text != label) r.Tmp.text = label;
        }

        private static string OnOff(bool on) => on ? "on" : "off";

        /// <summary>The menu sound helper the game's own button calls on click (so ours sound the same).</summary>
        private static UnityEngine.Object FindAudio(Button template)
        {
            for (int i = 0; i < template.onClick.GetPersistentEventCount(); i++)
            {
                UnityEngine.Object target = template.onClick.GetPersistentTarget(i);
                if (target != null && target.GetType().Name == "UIAudioHelper") return target;
            }
            return null;
        }

        private static void PlaySound(UnityEngine.Object audio, string method)
        {
            if (audio == null) return;
            try { HarmonyLib.AccessTools.Method(audio.GetType(), method, Type.EmptyTypes)?.Invoke(audio, null); }
            catch (Exception e) { Plugin.Log.LogWarning($"Menu sound {method} failed: {e.Message}"); }
        }
    }
}
