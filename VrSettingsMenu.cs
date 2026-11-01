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
        }

        private sealed class Context
        {
            public GameObject Host;
            public Button Template;
            public Button Entry;
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<GameObject> Hidden = new List<GameObject>();
            public UnityEngine.Object Audio;
        }

        private static Context _main, _pause, _open;
        private static float _nextScan;

        public static bool IsOpen => _open != null;

        /// <summary>One of our own settings rows (laid out by us, from the spacing of the menu's buttons).</summary>
        public static bool IsRow(Selectable s) =>
            (_main != null && _main.Rows.Any(r => r.Button == s)) || (_pause != null && _pause.Rows.Any(r => r.Button == s));
        public static int Entries => (_main?.Entry != null ? 1 : 0) + (_pause?.Entry != null ? 1 : 0);
        public static int OpenRows => _open == null ? 0 : _open.Rows.Count(r => r.Button != null && r.Button.gameObject.activeInHierarchy);

        public static void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                MainMenuController mmc = MainMenuController.Instance;
                PauseMenuController pmc = PauseMenuController.instance;
                Ensure(ref _main, mmc != null ? mmc.mainMenu : null);
                Ensure(ref _pause, pmc != null ? pmc.pauseMenu : null);
            }
            if (_open == null) return;

            if (!ContextAlive(_open)) Close(false);
            else if (Input.GetKeyUp(KeyCode.Escape) && _open == _main) Close(true);
            else foreach (Row r in _open.Rows) SetLabel(r, r.Label());
        }

        private static bool ContextAlive(Context c)
        {
            if (c.Host == null || !c.Host.activeInHierarchy) return false;
            if (c == _pause) return PauseMenuController.instance != null && PauseMenuController.instance.activeMenu == PauseMenuController.ActiveMenu.Pause;
            return MainMenuController.Instance != null && MainMenuController.Instance.activeMenu == MainMenuController.ActiveMenu.Main;
        }

        /// <summary>Back out of the VR settings (to the menu they were opened from). False if they weren't open.</summary>
        public static bool Back()
        {
            if (_open == null) return false;
            Close(true);
            return true;
        }

        private static void Ensure(ref Context ctx, GameObject host)
        {
            if (host == null) return;
            if (ctx != null && ctx.Host == host && ctx.Entry != null) return;
            Button template = host.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.name.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0 && b.transform.parent == host.transform);
            if (template == null) return;

            var c = new Context { Host = host, Template = template, Audio = FindAudio(template) };
            List<RectTransform> column = Column(c);
            float step = Step(column);
            var t = (RectTransform)template.transform;

            c.Entry = Clone(c, "VR Settings Button", "vr settings", () => Open(c), false);
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
            for (int i = 1; i < column.Count; i++) gaps.Add(column[i].anchoredPosition.y - column[i - 1].anchoredPosition.y);
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
            if (UnityEngine.XR.XRSettings.enabled) step *= Mathf.Clamp(Plugin.MenuSpacing.Value, 1f, 2.5f);
            int options = c.Rows.Count - 1, perColumn = (options + 1) / 2;
            float width = Mathf.Max(((RectTransform)c.Template.transform).rect.width, 200f);
            // Second column's text starts a margin after the first column's widest text ends. Spans account for the
            // labels' alignment: the main menu's are left-aligned, the pause menu's centred.
            for (int i = 0; i < options; i++) SetLabel(c.Rows[i], c.Rows[i].Label());
            float firstRight = float.MinValue, secondLeft = float.MaxValue;
            for (int i = 0; i < options; i++)
            {
                Vector2 span = TextSpan(c.Rows[i]);
                if (i < perColumn) firstRight = Mathf.Max(firstRight, span.y);
                else secondLeft = Mathf.Min(secondLeft, span.x);
            }
            float dx = firstRight > float.MinValue && secondLeft < float.MaxValue ? firstRight - secondLeft + ColumnMargin : width;

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
        private const float ColumnMargin = 60f;

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
            float lo = root.rect.xMin + ColumnMargin, hi = root.rect.xMax - ColumnMargin, shift = 0f;
            if (max > hi) shift = hi - max;
            if (min + shift < lo) shift = lo - min;
            if (Mathf.Abs(shift) < 0.5f) return;
            float local = shift * root.lossyScale.x / Mathf.Max(1e-5f, parent.lossyScale.x);
            foreach (Row r in c.Rows) ((RectTransform)r.Button.transform).anchoredPosition += new Vector2(local, 0f);
            Plugin.Log.LogInfo($"VR settings moved {local:F0} to stay on the menu.");
        }

        private static float TextWidth(Row r) =>
            r.Tmp != null ? r.Tmp.preferredWidth : r.Text != null ? r.Text.preferredWidth : 0f;

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
            float w = Mathf.Min(TextWidth(r), tr.rect.width);
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
            AddRow(c, "VR Eye Tracking Button", () => "eye tracking: " + (Plugin.EyeTracking.Value == EyeTrackingMode.Off ? "off (head aim)" : "auto"), Plugin.ToggleEyeTracking);
            AddRow(c, "VR World Size Button", () => "world size: " + StereoComfort.EyeSeparationLabel, StereoComfort.CycleEyeSeparation);
            AddRow(c, "VR Aiming Dot Button", () => "aiming dot: " + OnOff(Plugin.ShowReticle.Value), Plugin.ToggleReticle);
            AddRow(c, "VR Recenter Start Button", () => "recenter each scene: " + OnOff(Plugin.RecenterOnSceneStart.Value), Plugin.ToggleRecenterOnSceneStart);
            AddRow(c, "VR Recenter Button", () => "recenter view now", () => HeadTracking.Recenter());
            AddRow(c, "VR Render Quality Button", () => "render quality (in game): " + RenderScale.Label, RenderScale.Cycle);
            AddRow(c, "VR Performance Button", () => "performance mode: " + Performance.Label, Performance.Cycle);
            AddRow(c, "VR Auto Resolution Button", () => "auto resolution: " + AutoResolution.Label, AutoResolution.Cycle);
            AddRow(c, "VR Back Button", () => "back", () => Close(true), true);
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
