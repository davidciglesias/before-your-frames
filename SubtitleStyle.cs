using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's subtitles (DialogueManager's two caption texts) are sized for a monitor: in a headset they come out
    /// small and soft. In VR make them bigger and bold, with a dark outline so they read over bright scenes too.
    /// </summary>
    internal static class SubtitleStyle
    {
        private const float Scale = 1.6f;
        private static readonly HashSet<Text> Done = new HashSet<Text>();
        private static float _nextCheck;

        public static void Update()
        {
            if (!UnityEngine.XR.XRSettings.enabled || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            DialogueManager dm = DialogueManager.instance;
            if (dm == null) return;
            Style(dm._debugTextUi);
            Style(dm.nonChunkCaptionUi);
        }

        private static void Style(Text t)
        {
            if (t == null || Done.Contains(t)) return;
            Done.Add(t);
            t.fontSize = Mathf.RoundToInt(t.fontSize * Scale);
            if (t.resizeTextForBestFit) t.resizeTextMaxSize = Mathf.RoundToInt(t.resizeTextMaxSize * Scale);
            t.fontStyle = FontStyle.Bold;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (t.GetComponent<Outline>() == null)
            {
                Outline o = t.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(2f, -2f);
            }
            Plugin.Log.LogInfo($"Subtitles '{t.name}' enlarged for VR (font {t.fontSize}).");
        }
    }
}
