using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Diagnostic: per scene, how far the game's own camera is pitched and rolled. A rolled game camera tilts the whole
    /// world's horizon in VR (your head can't undo it), so this finds the scenes where that happens. One log line per
    /// scene, when it ends.
    /// </summary>
    internal static class CameraTilt
    {
        private static int _scene = -1;
        private static float _minPitch, _maxPitch, _maxRoll, _nextSample;

        public static void Update()
        {
            GameManager gm = GameManager.instance;
            Camera cam = Camera.main;
            if (gm == null || cam == null || BlinkCounter.instance == null) return;
            int scene = gm._currentSceneId;
            if (scene != _scene)
            {
                Flush();
                _scene = scene;
                _minPitch = float.MaxValue; _maxPitch = float.MinValue; _maxRoll = 0f;
            }
            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + 0.25f;
            Vector3 e = cam.transform.eulerAngles;
            float pitch = -Mathf.DeltaAngle(0f, e.x); // + up
            float roll = Mathf.DeltaAngle(0f, e.z);
            _minPitch = Mathf.Min(_minPitch, pitch);
            _maxPitch = Mathf.Max(_maxPitch, pitch);
            if (Mathf.Abs(roll) > Mathf.Abs(_maxRoll)) _maxRoll = roll;
        }

        private static void Flush()
        {
            if (_scene < 0 || _minPitch == float.MaxValue) return;
            Plugin.Log.LogInfo($"Camera tilt, scene #{_scene}: pitch {_minPitch:F1}..{_maxPitch:F1}, max roll {_maxRoll:F1}");
        }
    }
}
