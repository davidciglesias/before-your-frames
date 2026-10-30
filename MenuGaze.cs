using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// Blink-driven menus (main menu, pause menu, settings...), so no mouse or controller is needed.
    /// The aim ray (head rotation * eye gaze) is intersected with UUVR's UI panel (fixed in tracking space, 2 m ahead),
    /// and the clickable control nearest to it, within Menu Snap Degrees, is hovered: eye tracking is a degree or two
    /// off at best, so pointing exactly at a small button is too hard. While a menu is open the panel is also made
    /// bigger (Menu Panel Scale), which spreads the options apart. A deliberate blink (eyes closed at least
    /// MenuBlinkSeconds) clicks what was hovered when the eyes closed. Short natural blinks are ignored.
    /// Only active outside gameplay (no BlinkCounter yet, or the pause menu is open).
    /// </summary>
    internal static class MenuGaze
    {
        private const float PanelDistance = 2f;
        private const float BasePanelWidth = 1.8f;   // UUVR VrUiManager quad width (meters)
        private const float StickyDegrees = 0.5f;    // a new target must be this much closer to take over (more feels stuck)

        private static PointerEventData _ped;
        private static readonly List<RaycastResult> Results = new List<RaycastResult>();
        private static readonly List<GameObject> Candidates = new List<GameObject>();
        private static float _nextCandidateScan;
        private static GameObject _hover;
        private static GameObject _pressTarget;
        private static float _closedAt = -1f;
        private static SpriteRenderer _cursor;
        private static float _nextGameplayCheck;
        private static bool _inGameplay;
        private static Transform _panelQuad;

        public static bool Active { get; private set; }
        public static string HoverName => _hover != null ? _hover.name : "";
        public static int Clicks { get; private set; }
        public static Vector2 ScreenPoint { get; private set; }
        public static float HoverDegrees { get; private set; }

        /// <summary>Current width of UUVR's UI panel (meters at 2 m), including our menu enlargement.</summary>
        public static float PanelWidth { get; private set; } = BasePanelWidth;

        public static void LateUpdate()
        {
            Active = Plugin.BlinkMenus.Value && UnityEngine.XR.XRSettings.enabled && EventSystem.current != null && !InGameplay();
            ScalePanel(Active ? Mathf.Clamp(Plugin.MenuPanelScale.Value, 0.5f, 3f) : 1f);
            if (!Active)
            {
                SetHover(null);
                if (_cursor != null) _cursor.enabled = false;
                _closedAt = -1f;
                return;
            }

            // Aim ray in tracking space -> point on UUVR's UI panel -> screen point.
            Quaternion gaze = Gaze.LocalRotation;
            Vector3 dir = HeadTracking.ReadHeadRotation() * gaze * Vector3.forward;
            GameObject target = null;
            if (dir.z > 0.2f)
            {
                Vector3 onPanel = dir * (PanelDistance / dir.z);
                ScreenPoint = new Vector2(
                    (onPanel.x / PanelWidth + 0.5f) * Screen.width,
                    (onPanel.y / PanelHeight + 0.5f) * Screen.height);
                if (_ped == null) _ped = new PointerEventData(EventSystem.current);
                target = Nearest(dir);
            }
            SetHover(target);
            UpdateCursor(gaze, target != null);

            // Deliberate blink = click on what was hovered when the eyes closed.
            float now = Time.unscaledTime;
            if (BlinkState.ClosedThisFrame)
            {
                _closedAt = now;
                _pressTarget = _hover;
            }
            if (BlinkState.OpenedThisFrame && _closedAt >= 0f)
            {
                float held = now - _closedAt;
                _closedAt = -1f;
                GameObject clicked = _pressTarget != null ? _pressTarget : _hover;
                if (held >= Plugin.MenuBlinkSeconds.Value && clicked != null && clicked.activeInHierarchy)
                {
                    Clicks++;
                    Plugin.Log.LogInfo($"Menu blink-click on {clicked.name} (held {held:F2} s).");
                    _ped.pointerPress = clicked;
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerClickHandler);
                }
            }
        }

        private static float PanelHeight => PanelWidth * Screen.height / Screen.width;

        /// <summary>The clickable control closest (in degrees) to the aim direction, within Menu Snap Degrees.</summary>
        private static GameObject Nearest(Vector3 aim)
        {
            if (Time.unscaledTime >= _nextCandidateScan) ScanCandidates();
            float snap = Mathf.Max(0f, Plugin.MenuSnapDegrees.Value);
            GameObject best = null;
            float bestAngle = float.MaxValue, hoverAngle = float.MaxValue;
            foreach (GameObject go in Candidates)
            {
                if (go == null || !go.activeInHierarchy) continue;
                float angle = AngleTo((RectTransform)go.transform, aim);
                if (go == _hover) hoverAngle = angle;
                if (angle < bestAngle) { bestAngle = angle; best = go; }
            }
            // Back buttons reach half as far again, so getting out of a menu you opened by mistake is easy. Only the
            // reach: the nearest option still wins, or Back would steal options listed right next to it.
            float reach = best != null && IsBack(best) ? snap * 1.5f : snap;
            float hoverReach = _hover != null && IsBack(_hover) ? snap * 1.5f : snap;
            if (_hover != null && hoverAngle <= hoverReach && hoverAngle <= bestAngle + StickyDegrees)
            {
                HoverDegrees = hoverAngle;
                return _hover;
            }
            HoverDegrees = bestAngle;
            return bestAngle <= reach ? best : null;
        }

        private static bool IsBack(GameObject go) => go.name.IndexOf("back", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Clickable controls that are actually on top at their own centre (so buttons behind a dialog don't count).
        /// Refreshed a few times a second.
        /// </summary>
        private static void ScanCandidates()
        {
            _nextCandidateScan = Time.unscaledTime + 0.25f;
            Candidates.Clear();
            foreach (Selectable s in Selectable.allSelectables)
            {
                if (s == null || !s.IsInteractable() || !s.isActiveAndEnabled || IsToolUi(s.gameObject)) continue;
                // Dropdowns can't be used by gaze (their list needs a second precise pick) and an open one lays an
                // invisible full-screen blocker over the menu, so leave them to the mouse.
                if (s is Dropdown || s is TMPro.TMP_Dropdown) continue;
                GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(s.gameObject);
                if (handler == null || handler != s.gameObject) continue;
                var rt = (RectTransform)s.transform;
                _ped.Reset();
                _ped.position = ScreenOf(rt, rt.rect.center);
                Results.Clear();
                EventSystem.current.RaycastAll(_ped, Results);
                foreach (RaycastResult r in Results)
                {
                    if (IsToolUi(r.gameObject)) continue; // invisible in VR, so it mustn't block the game's menus
                    if (r.gameObject.name == "Blocker" && r.gameObject.GetComponent<Canvas>() != null) continue; // open dropdown's click-catcher
                    if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(r.gameObject) == s.gameObject) Candidates.Add(s.gameObject);
                    break; // only the topmost game hit counts
                }
            }
        }

        /// <summary>
        /// UI of modding tools (UnityExplorer / UniverseLib windows) on the desktop window. It isn't shown in the
        /// headset, but it does take UI raycasts.
        /// </summary>
        private static bool IsToolUi(GameObject go)
        {
            Canvas canvas = go.GetComponentInParent<Canvas>();
            if (canvas == null) return false;
            string root = canvas.rootCanvas.name;
            return root.IndexOf("unityexplorer", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   root.IndexOf("UniverseLib", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Debug: why each selectable is or isn't a menu target.</summary>
        public static void DumpCandidates()
        {
            if (EventSystem.current == null) return;
            if (_ped == null) _ped = new PointerEventData(EventSystem.current);
            _ped.Reset();
            _ped.position = ScreenPoint;
            Results.Clear();
            EventSystem.current.RaycastAll(_ped, Results);
            Plugin.Log.LogInfo($"[cmd] menu screen={Screen.width}x{Screen.height} gazePoint={ScreenPoint:F0} hits={Results.Count} top={(Results.Count > 0 ? Results[0].gameObject.name : "none")} " +
                               $"eventSystem={EventSystem.current.name} raycasters={Object.FindObjectsOfType<BaseRaycaster>().Length} hover={HoverName}");
            foreach (BaseRaycaster r in Object.FindObjectsOfType<BaseRaycaster>())
                Plugin.Log.LogInfo($"[cmd] raycaster {r.name} {r.GetType().Name} enabled={r.isActiveAndEnabled} cam={(r.eventCamera != null ? r.eventCamera.name : "none")}");
            foreach (Selectable s in Selectable.allSelectables)
            {
                if (s == null) continue;
                var rt = (RectTransform)s.transform;
                Vector2 c = ScreenOf(rt, rt.rect.center);
                _ped.Reset();
                _ped.position = c;
                Results.Clear();
                EventSystem.current.RaycastAll(_ped, Results);
                GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(s.gameObject);
                Canvas canvas = s.GetComponentInParent<Canvas>();
                Plugin.Log.LogInfo($"[cmd] sel {s.name} ({s.GetType().Name}) interactable={s.IsInteractable()} active={s.isActiveAndEnabled} " +
                                   $"handler={(handler != null ? handler.name : "none")} center={c:F0} top={(Results.Count > 0 ? Results[0].gameObject.name : "none")} " +
                                   $"canvas={(canvas != null ? canvas.rootCanvas.name + "/" + canvas.rootCanvas.renderMode : "none")} target={Candidates.Contains(s.gameObject)}");
            }
        }

        private static Vector2 ScreenOf(RectTransform rt, Vector2 local)
        {
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(local));
        }

        /// <summary>Angle between the aim and the nearest point of the control's rectangle (0 when inside).</summary>
        private static float AngleTo(RectTransform rt, Vector3 aim)
        {
            Vector2 a = ScreenOf(rt, rt.rect.min), b = ScreenOf(rt, rt.rect.max);
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            var p = new Vector2(Mathf.Clamp(ScreenPoint.x, min.x, max.x), Mathf.Clamp(ScreenPoint.y, min.y, max.y));
            return Vector3.Angle(aim, PanelDirection(p));
        }

        private static Vector3 PanelDirection(Vector2 screen) =>
            new Vector3((screen.x / Screen.width - 0.5f) * PanelWidth, (screen.y / Screen.height - 0.5f) * PanelHeight, PanelDistance);

        /// <summary>Resizes UUVR's UI panel quad around its centre (it stays 2 m away), keeping its mirror sign.</summary>
        private static void ScalePanel(float factor)
        {
            if (_panelQuad == null)
            {
                GameObject quad = GameObject.Find("VrUiQuad");
                if (quad == null) return;
                _panelQuad = quad.transform;
            }
            float width = BasePanelWidth * factor;
            float height = width * Screen.height / Screen.width;
            Vector3 s = _panelQuad.localScale;
            if (Mathf.Abs(s.x - width) > 1e-4f || Mathf.Abs(Mathf.Abs(s.y) - height) > 1e-4f)
                _panelQuad.localScale = new Vector3(width, s.y < 0 ? -height : height, s.z);
            PanelWidth = width;
        }

        private static bool InGameplay()
        {
            PauseMenuController pause = PauseMenuController.instance;
            if (pause != null && pause.activeMenu != PauseMenuController.ActiveMenu.None) return false;
            if (Time.unscaledTime >= _nextGameplayCheck)
            {
                _nextGameplayCheck = Time.unscaledTime + 0.5f;
                _inGameplay = Object.FindObjectOfType<BlinkCounter>() != null;
            }
            return _inGameplay;
        }

        private static void SetHover(GameObject target)
        {
            if (target == _hover) return;
            if (_hover != null && _ped != null) ExecuteEvents.Execute(_hover, _ped, ExecuteEvents.pointerExitHandler);
            _hover = target;
            if (_hover != null && _ped != null) ExecuteEvents.Execute(_hover, _ped, ExecuteEvents.pointerEnterHandler);
        }

        /// <summary>
        /// Gaze dot for menus. While an option is selected the dot jumps to it (a magnet), so you can see what a blink
        /// will press even when eye tracking is a bit off or coarse; otherwise it follows the gaze.
        /// </summary>
        private static void UpdateCursor(Quaternion gaze, bool overButton)
        {
            if (UiOverlay.Camera == null) return;
            if (_cursor == null)
            {
                _cursor = UiOverlay.CreateSprite("BYE Menu Cursor", 30002);
                _cursor.sprite = UiOverlay.WhiteSprite;
            }
            _cursor.enabled = true;
            float size = overButton ? 0.022f : 0.014f; // meters at 2 m (~0.6 / 0.4 degrees)
            _cursor.color = overButton ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
            Vector3 local = gaze * Vector3.forward;
            if (overButton && _hover != null)
            {
                // Selected option's centre: panel (tracking) space -> head space of the overlay camera.
                var rt = (RectTransform)_hover.transform;
                Vector3 onPanel = PanelDirection(ScreenOf(rt, rt.rect.center)).normalized;
                local = Quaternion.Inverse(HeadTracking.ReadHeadRotation()) * onPanel;
            }
            _cursor.transform.localPosition = local * PanelDistance * 0.99f;
            _cursor.transform.localRotation = Quaternion.LookRotation(local, Vector3.up) * Quaternion.Euler(0f, 0f, 45f);
            _cursor.transform.localScale = new Vector3(size, size, 1f);
        }

        private static Selectable FindButton(string label)
        {
            foreach (Selectable b in Selectable.allSelectables)
            {
                var text = b.GetComponentInChildren<Text>(true);
                var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                string l = text != null ? text.text : tmp != null ? tmp.text : b.name;
                if (string.Equals(l?.Trim(), label, System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.name, label, System.StringComparison.OrdinalIgnoreCase)) return b;
            }
            return null;
        }

        /// <summary>
        /// Test helper: simulated-head rotation that aims (no gaze) at a button: its centre, or with
        /// degreesBelowEdge > 0 that many degrees below its bottom edge (to check the snap radius).
        /// </summary>
        public static bool TryAimAt(string label, out Quaternion headRotation, float degreesBelowEdge = 0f)
        {
            headRotation = Quaternion.identity;
            Selectable b = FindButton(label);
            if (b == null) return false;
            var rt = (RectTransform)b.transform;
            Vector2 point = ScreenOf(rt, rt.rect.center);
            if (degreesBelowEdge > 0f)
            {
                Vector2 a = ScreenOf(rt, rt.rect.min), c = ScreenOf(rt, rt.rect.max);
                point.y = Mathf.Min(a.y, c.y);
            }
            headRotation = Quaternion.LookRotation(PanelDirection(point).normalized, Vector3.up);
            if (degreesBelowEdge > 0f) headRotation = headRotation * Quaternion.AngleAxis(degreesBelowEdge, Vector3.right);
            return true;
        }
    }
}
