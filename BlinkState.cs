using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Turns raw eyelid openness into a debounced "eyes closed" state, sampled once per frame.
    /// The state doubles as a virtual held left mouse button for the game's click-to-blink mode.
    /// </summary>
    internal static class BlinkState
    {
        private static int _lastFrame = -1;
        private static bool _rawClosed;        // after thresholds/hysteresis
        private static float _rawChangedAt;    // realtime when _rawClosed last flipped
        private static bool _filtered;         // after min-duration debounce
        private static bool _closed;           // _filtered or debug key
        private static bool _prevClosed;
        private static bool _warnedTimeout;

        public static float Openness { get; private set; } = 1f;
        public static bool HasData { get; private set; }
        public static int BlinkCount { get; private set; }

        public static bool Closed { get { Sample(); return _closed; } }
        /// <summary>Below the close threshold right now, before debouncing (eyelid is dropping).</summary>
        public static bool RawClosed { get { Sample(); return _rawClosed; } }
        public static bool ClosedThisFrame { get { Sample(); return _closed && !_prevClosed; } }
        public static bool OpenedThisFrame { get { Sample(); return !_closed && _prevClosed; } }

        private static void Sample()
        {
            int frame = Time.frameCount;
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            _prevClosed = _closed;

            float now = Time.realtimeSinceStartup;
            HasData = OscReceiver.SecondsSinceLidData < Plugin.DataTimeoutSeconds.Value;

            if (HasData)
            {
                _warnedTimeout = false;
                float l = OscReceiver.LeftLid, r = OscReceiver.RightLid;
                switch (Plugin.CombineMode.Value)
                {
                    case EyeCombineMode.BothClosed: Openness = Mathf.Max(l, r); break;
                    case EyeCombineMode.EitherClosed: Openness = Mathf.Min(l, r); break;
                    default: Openness = (l + r) * 0.5f; break;
                }
            }
            else
            {
                if (!_warnedTimeout && OscReceiver.PacketCount > 0)
                {
                    Plugin.Log.LogWarning("Eyelid data timed out; treating eyes as open.");
                    _warnedTimeout = true;
                }
                Openness = 1f;
            }

            bool raw = _rawClosed
                ? Openness < Plugin.OpenThreshold.Value
                : Openness < Plugin.CloseThreshold.Value;
            if (raw != _rawClosed)
            {
                _rawClosed = raw;
                _rawChangedAt = now;
            }

            float held = now - _rawChangedAt;
            if (_rawClosed && !_filtered && held >= Plugin.MinClosedSeconds.Value) _filtered = true;
            else if (!_rawClosed && _filtered && held >= Plugin.MinOpenSeconds.Value) _filtered = false;

            // Debug key bypasses the filters entirely.
            _closed = _filtered || Input.GetKey(Plugin.ForceBlinkKey.Value) || AutoPlay.ForceClosed;

            if (_closed && !_prevClosed) BlinkCount++;
        }
    }
}
