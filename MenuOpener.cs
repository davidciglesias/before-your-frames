using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Opening the pause menu without a controller. Tilt the head well down (rarely done in play) and a "menu" label
    /// appears below, fixed in the world in front of you; turn the head onto it (no eye aiming needed) and it
    /// lights; close the eyes for a moment and the pause menu opens. While it's lit, blinks don't reach the game, so
    /// that blink can't move the story on. It's drawn on the UI overlay layer, so scenery can't hide it.
    /// </summary>
    internal static class MenuOpener
    {
        private const float ShowBelow = -35f;   // head pitch (degrees) below which the label shows
        private const float LabelPitch = -50f;  // where it sits: this far below the horizon, ahead of you
        private const float LitWithin = 12f;    // head pointing within this of it lights it
        private const float HoldSeconds = 0.5f; // eyes closed this long on it opens the menu
        private const float Distance = 1.5f;

        private static TextMesh _text;
        private static Vector3 _anchor;          // label direction in tracking space
        private static bool _visible, _armed, _fired;
        private static float _closedSince = -1f;

        public static bool Armed => _armed;

        public static void Update()
        {
            bool playing = UnityEngine.XR.XRSettings.enabled && Plugin.GazeMenuOpener.Value && BlinkCounter.instance != null
                           && PauseMenuController.instance != null
                           && PauseMenuController.instance.activeMenu == PauseMenuController.ActiveMenu.None;
            Quaternion head = HeadTracking.ReadHeadRotation();
            Vector3 fwd = head * Vector3.forward;
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool closed = BlinkState.RawClosed;

            if (!playing || (pitch > ShowBelow + 5f && !(_armed && closed)))
            {
                _visible = _armed = false;
                _closedSince = -1f;
                if (!closed) _fired = false;
                Show();
                return;
            }
            if (!_visible && pitch < ShowBelow)
            {
                // Place it ahead of where you face, down on the "floor", and keep it there (world-fixed).
                Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude < 1e-4f) flat = head * Vector3.up * -1f; // looking straight down
                flat.y = 0f;
                _anchor = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(-LabelPitch, 0f, 0f) * Vector3.forward;
                _visible = true;
            }
            if (!_visible) { Show(); return; }

            _armed = Vector3.Angle(fwd, _anchor) < LitWithin || (_armed && closed);
            if (_armed && closed)
            {
                if (_closedSince < 0f) _closedSince = Time.unscaledTime;
                if (!_fired && Time.unscaledTime - _closedSince >= HoldSeconds)
                {
                    _fired = true;
                    Plugin.Log.LogInfo("Pause menu opened from the look-down menu label.");
                    PauseMenuController.instance.Pause();
                }
            }
            else { _closedSince = -1f; if (!closed) _fired = false; }
            Show(head);
        }

        private static void Show(Quaternion head = default(Quaternion))
        {
            if (!_visible) { if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false); return; }
            if (UiOverlay.Camera == null) return;
            if (_text == null)
            {
                var go = new GameObject("BYE Menu Opener");
                go.layer = UiOverlay.OwnLayer;
                UiOverlay.EnforceLayerMasks();
                go.transform.SetParent(UiOverlay.Camera.transform, false);
                _text = go.AddComponent<TextMesh>();
                Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _text.font = font;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.material = font.material;
                renderer.sortingOrder = 30002;
                _text.fontSize = 64;
                _text.characterSize = 0.012f;
                _text.anchor = TextAnchor.MiddleCenter;
                _text.alignment = TextAlignment.Center;
            }
            // The overlay camera turns with the head: counter-rotate so the label stays put in the world, facing you.
            Quaternion inv = Quaternion.Inverse(head);
            _text.transform.localPosition = inv * (_anchor * Distance);
            _text.transform.localRotation = inv * Quaternion.LookRotation(_anchor, Vector3.up);
            _text.text = MenuText.Menu;
            _text.color = new Color(1f, 1f, 1f, _armed ? 0.95f : 0.4f);
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
        }
    }
}
