using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BYEFrameBlink
{
    /// <summary>
    /// Blink-driven menus (main menu, pause menu, settings...), so no mouse or controller is needed.
    /// The aim ray (head rotation * eye gaze) is intersected with UUVR's UI panel (fixed in tracking space, 2 m ahead,
    /// 1.8 m wide), converted to a screen point and fed to the EventSystem: hovering highlights the button under it,
    /// and a deliberate blink (eyes closed at least MenuBlinkSeconds) clicks it. Short natural blinks are ignored.
    /// Only active outside gameplay (no BlinkCounter yet, or the pause menu is open).
    /// </summary>
    internal static class MenuGaze
    {
        private const float PanelDistance = 2f;
        private const float PanelWidth = 1.8f;

        private static PointerEventData _ped;
        private static readonly List<RaycastResult> Results = new List<RaycastResult>();
        private static GameObject _hover;
        private static float _closedAt = -1f;
        private static SpriteRenderer _cursor;
        private static float _nextGameplayCheck;
        private static bool _inGameplay;

        public static bool Active { get; private set; }
        public static string HoverName => _hover != null ? _hover.name : "";
        public static int Clicks { get; private set; }
        public static Vector2 ScreenPoint { get; private set; }

        public static void LateUpdate()
        {
            Active = Plugin.BlinkMenus.Value && UnityEngine.XR.XRSettings.enabled && EventSystem.current != null && !InGameplay();
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
                float panelHeight = PanelWidth * Screen.height / Screen.width;
                ScreenPoint = new Vector2(
                    (onPanel.x / PanelWidth + 0.5f) * Screen.width,
                    (onPanel.y / panelHeight + 0.5f) * Screen.height);

                if (_ped == null) _ped = new PointerEventData(EventSystem.current);
                _ped.Reset();
                _ped.position = ScreenPoint;
                _ped.button = PointerEventData.InputButton.Left;
                Results.Clear();
                EventSystem.current.RaycastAll(_ped, Results);
                foreach (RaycastResult r in Results)
                {
                    target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(r.gameObject);
                    if (target != null) break;
                }
            }
            SetHover(target);
            UpdateCursor(gaze, target != null);

            // Deliberate blink = click.
            float now = Time.unscaledTime;
            if (BlinkState.ClosedThisFrame) _closedAt = now;
            if (BlinkState.OpenedThisFrame && _closedAt >= 0f)
            {
                float held = now - _closedAt;
                _closedAt = -1f;
                if (held >= Plugin.MenuBlinkSeconds.Value && _hover != null)
                {
                    Clicks++;
                    Plugin.Log.LogInfo($"Menu blink-click on {_hover.name} (held {held:F2} s).");
                    GameObject clicked = _hover;
                    _ped.pointerPress = clicked;
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(clicked, _ped, ExecuteEvents.pointerClickHandler);
                }
            }
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
            _cursor.transform.localPosition = gaze * Vector3.forward * PanelDistance * 0.99f;
            _cursor.transform.localRotation = gaze * Quaternion.Euler(0f, 0f, 45f);
            _cursor.transform.localScale = new Vector3(size, size, 1f);
        }

        /// <summary>Test helper: simulated-head rotation that puts the aim (no gaze) on a UI element's center.</summary>
        public static bool TryAimAt(string label, out Quaternion headRotation)
        {
            headRotation = Quaternion.identity;
            foreach (UnityEngine.UI.Button b in Object.FindObjectsOfType<UnityEngine.UI.Button>())
            {
                var text = b.GetComponentInChildren<UnityEngine.UI.Text>(true);
                var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                string l = text != null ? text.text : tmp != null ? tmp.text : b.name;
                if (!string.Equals(l?.Trim(), label, System.StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(b.name, label, System.StringComparison.OrdinalIgnoreCase)) continue;

                Canvas canvas = b.GetComponentInParent<Canvas>()?.rootCanvas;
                Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var rt = (RectTransform)b.transform;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
                float panelHeight = PanelWidth * Screen.height / Screen.width;
                var onPanel = new Vector3((screen.x / Screen.width - 0.5f) * PanelWidth, (screen.y / Screen.height - 0.5f) * panelHeight, PanelDistance);
                headRotation = Quaternion.LookRotation(onPanel.normalized, Vector3.up);
                return true;
            }
            return false;
        }
    }
}
