using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>A short head-locked text message (e.g. a setting changed by a hotkey), drawn through UUVR's UI overlay camera.</summary>
    internal static class Toast
    {
        private const float Seconds = 2f;

        private static TextMesh _text;
        private static float _hideAt;

        public static void Show(string message)
        {
            Plugin.Log.LogInfo($"[toast] {message}");
            if (UiOverlay.Camera == null) return;
            if (_text == null)
            {
                var go = new GameObject("BYE Toast");
                go.layer = UiOverlay.OwnLayer;
                UiOverlay.EnforceLayerMasks();
                go.transform.SetParent(UiOverlay.Camera.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.3f, 1.5f);
                _text = go.AddComponent<TextMesh>();
                Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _text.font = font;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.material = font.material;
                renderer.sortingOrder = 30003; // over UUVR's UI panel (order 0) and the veils
                _text.fontSize = 64;
                _text.characterSize = 0.01f;
                _text.anchor = TextAnchor.MiddleCenter;
                _text.alignment = TextAlignment.Center;
                _text.color = new Color(1f, 1f, 1f, 0.9f);
            }
            _text.text = message;
            _text.gameObject.SetActive(true);
            _hideAt = Time.unscaledTime + Seconds;
        }

        public static void LateUpdate()
        {
            if (_text != null && _text.gameObject.activeSelf && Time.unscaledTime >= _hideAt) _text.gameObject.SetActive(false);
        }
    }
}
