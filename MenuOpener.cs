using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Opening the pause menu without a controller. Turn the head down to the left (rarely done in play) and a "menu" label
    /// appears there, fixed in the world; turn the head onto it (no eye aiming needed) and it
    /// lights; close the eyes for a moment and the pause menu opens. While it's lit, blinks don't reach the game, so
    /// that blink can't move the story on. It's drawn on the UI overlay layer, so scenery can't hide it.
    /// </summary>
    internal static class MenuOpener
    {
        // Low on the left of the scene's forward direction (each scene is recentered to it), out of the way of what
        // scenes ask you to look at; it shows when the head turns towards it.
        private const float LabelYaw = -70f;    // degrees left of forward
        private const float ShowWithin = 22f;   // head pointing within this of it shows the label
        private const float LabelPitch = -55f;  // degrees below the horizon
        private static Vector3 Anchor => Quaternion.Euler(-LabelPitch, LabelYaw, 0f) * Vector3.forward;
        private const float LitWithin = 12f;    // head pointing within this of it lights it
        private const float HoldSeconds = 0.5f; // eyes closed this long on it opens the menu
        private const float Distance = 1.5f;

        private static TextMesh _text;
        private static Vector3 _anchor;          // label direction in tracking space
        private static Vector3 _origin;
        private static bool _originSet;
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
            bool closed = BlinkState.RawClosed;

            if (!playing || (Vector3.Angle(fwd, Anchor) > ShowWithin + 5f && !(_armed && closed)))
            {
                _visible = _armed = false;
                _closedSince = -1f;
                if (!closed) _fired = false;
                Show();
                return;
            }
            if (!_visible && Vector3.Angle(fwd, Anchor) < ShowWithin) { _anchor = Anchor; _visible = true; }
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
            if (!_visible) { _originSet = false; if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false); return; }
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
            // Fixed in tracking space (the overlay camera's parent), not counter-rotated against the head: placing it
            // through the head pose made it tremble, as the headset renders with a slightly newer pose than ours.
            Transform cam = UiOverlay.Camera.transform;
            if (_text.transform.parent != cam.parent) _text.transform.SetParent(cam.parent, false);
            if (!_originSet) { _origin = cam.localPosition; _originSet = true; } // where your head was when it appeared
            _text.transform.localPosition = _origin + _anchor * Distance;
            _text.transform.localRotation = Quaternion.LookRotation(_anchor, Vector3.up);
            _text.text = MenuText.Menu;
            _text.color = new Color(1f, 1f, 1f, _armed ? 0.95f : 0.4f);
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
        }
    }
}
