using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// Shows the mod's version under the game's ("V 1.2.6.8") on the main menu, in the same style, for bug reports.
    /// </summary>
    [HarmonyPatch(typeof(GetVersionNumber), "Start")]
    internal static class MenuVersion
    {
        private static void Postfix(GetVersionNumber __instance)
        {
            Text label = __instance.GetComponent<Text>();
            if (label == null || __instance.transform.parent == null) return;
            if (__instance.transform.parent.Find("BYE VR Version") != null) return;

            // Thin small text is unreadable at a headset's resolution: bolder and bigger (both labels, the copy inherits).
            label.fontStyle = FontStyle.Bold;
            label.fontSize = Mathf.RoundToInt(label.fontSize * 1.5f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            GameObject copy = Object.Instantiate(__instance.gameObject, __instance.transform.parent, false);
            Object.DestroyImmediate(copy.GetComponent<GetVersionNumber>()); // or it would write the game's version
            copy.name = "BYE VR Version";
            var rt = (RectTransform)copy.transform;
            var src = (RectTransform)__instance.transform;
            float line = (label.preferredHeight > 1f ? label.preferredHeight : label.fontSize) * 1.15f;
            // The game's label sits at the bottom edge of the menu, so lift it a line and put ours in its place.
            rt.anchoredPosition = src.anchoredPosition;
            src.anchoredPosition += new Vector2(0f, line);
            copy.AddComponent<EyeSourceLabel>().Init(copy.GetComponent<Text>());
        }
    }

    /// <summary>Keeps the main menu's "VR V x · eyes: source" line current, so you can see which eye tracker is in use.</summary>
    internal class EyeSourceLabel : MonoBehaviour
    {
        private Text _text;
        private float _next;

        public void Init(Text text) { _text = text; Refresh(); }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            Refresh();
        }

        private void Refresh()
        {
            _next = Time.unscaledTime + 0.5f;
            if (_text == null) return;
            string source = Plugin.EyeTracking.Value == EyeTrackingMode.Off ? MenuText.T("off")
                : OscReceiver.ActiveSourceLabel ?? MenuText.T("no data");
            string s = "VR V " + Plugin.Version + " · " + MenuText.T("eyes") + ": " + source;
            if (_text.text != s) _text.text = s;
        }
    }
}
