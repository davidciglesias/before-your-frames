using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's reticle is an Image on the HUD canvas, which UUVR shows on a small panel (~48x27 degrees) fixed in
    /// tracking space, so the reticle couldn't follow the aim ray and got clamped at the panel's edges.
    /// Instead we draw a head-locked copy along the aim ray (gaze, or view center without gaze) through UUVR's UI overlay
    /// camera, copying the game reticle's sprite/color/size/rotation every frame (so its DOT/HAND/PIANO/... states still
    /// show), and hide the original.
    /// </summary>
    internal static class ReticleFollower
    {
        private const float Distance = 2f;      // same distance as UUVR's UI panel, so the reticle keeps its size
        private const float PanelWidth = 1.8f;  // UUVR VrUiManager quad width (meters)

        private static Image _source;
        private static SpriteRenderer _copy;
        private static float _nextSearch;

        /// <summary>Head-space direction the reticle is drawn at (for tests).</summary>
        public static Vector3 LocalDirection { get; private set; } = Vector3.forward;
        public static bool Drawing { get; private set; }

        public static void LateUpdate()
        {
            bool enabled = Plugin.GazeMoveReticle.Value && UnityEngine.XR.XRSettings.enabled;
            if (_source == null)
            {
                if (!enabled || Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 1f; // FindObjectOfType is slow; don't do it every frame in menus
                ReticleManager manager = Object.FindObjectOfType<ReticleManager>();
                if (manager == null) return;
                _source = manager.GetComponent<Image>();
                if (_source == null) return;
            }

            if (!enabled || UiOverlay.Camera == null)
            {
                Release();
                return;
            }
            if (_copy == null) _copy = UiOverlay.CreateSprite("BYE Reticle", 30001);

            Canvas canvas = _source.canvas != null ? _source.canvas.rootCanvas : null;
            // Aiming with the head (no eye data), the dot is the only way to see what you aim at: always show it.
            bool visible = (Plugin.ShowReticle.Value || !Gaze.Active) && _source.isActiveAndEnabled && _source.sprite != null && canvas != null;
            _copy.enabled = visible;
            Drawing = visible;
            if (visible)
            {
                // Size: canvas units -> meters on UUVR's panel at the same distance.
                var canvasRt = (RectTransform)canvas.transform;
                float metersPerUnit = PanelWidth / Mathf.Max(1f, canvasRt.rect.width);
                Vector3 scaleInCanvas = Vector3.Scale(_source.rectTransform.lossyScale, Inverse(canvasRt.lossyScale));
                float distance = TargetDistance();
                // Same angular size at any distance.
                Vector2 sizeMeters = Vector2.Scale(_source.rectTransform.rect.size, scaleInCanvas) * metersPerUnit * (distance / Distance);

                _copy.sprite = _source.sprite;
                Color c = _source.color;
                c.a *= _source.canvasRenderer.GetInheritedAlpha();
                _copy.color = c;
                Vector2 spriteSize = _source.sprite.bounds.size;
                _copy.transform.localScale = new Vector3(
                    sizeMeters.x / Mathf.Max(1e-5f, spriteSize.x),
                    sizeMeters.y / Mathf.Max(1e-5f, spriteSize.y), 1f);

                // The overlay camera already carries the head rotation; add the gaze.
                Quaternion gaze = Gaze.LocalRotation;
                LocalDirection = gaze * Vector3.forward;
                _copy.transform.localPosition = LocalDirection * distance;
                _copy.transform.localRotation = gaze * Quaternion.Euler(0f, 0f, _source.rectTransform.localEulerAngles.z);
            }

            // Hide the original (its CanvasRenderer alpha isn't animated by the game).
            _source.canvasRenderer.SetAlpha(0f);
        }

        private const float MinDistance = 0.15f, MaxDistance = 20f;
        private const int InteractableMask = 79667; // InteractableManager._layerMask
        private static float _distance = Distance;

        /// <summary>
        /// Distance (meters, as you see it) to whatever the aim ray hits, smoothed. A dot drawn at a fixed 2 m doubles
        /// when you focus on something close. Interactables first (incl. triggers, as the game aims), then solid geometry.
        /// </summary>
        private static float TargetDistance()
        {
            if (!Plugin.ReticleAtTargetDepth.Value) return _distance = Distance;
            Camera cam = Camera.main;
            if (cam == null) return _distance;
            Ray ray = Gaze.ViewportPointToRay(cam, new Vector3(0.5f, 0.5f, 0f));
            float target = Distance;
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, InteractableMask, QueryTriggerInteraction.Collide) ||
                Physics.Raycast(ray, out hit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // A smaller eye separation makes the world look proportionally farther away.
                float scale = StereoComfort.Patched ? Mathf.Clamp(Plugin.EyeSeparationScale.Value, 0.05f, 2f) : 1f;
                target = Mathf.Clamp(hit.distance / scale, MinDistance, MaxDistance);
            }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.08f);
            _distance = Mathf.Lerp(_distance, target, k);
            return _distance;
        }

        private static void Release()
        {
            Drawing = false;
            if (_copy != null) _copy.enabled = false;
            if (_source != null) _source.canvasRenderer.SetAlpha(1f);
        }

        private static Vector3 Inverse(Vector3 v) =>
            new Vector3(v.x != 0 ? 1f / v.x : 0f, v.y != 0 ? 1f / v.y : 0f, v.z != 0 ? 1f / v.z : 0f);
    }
}
