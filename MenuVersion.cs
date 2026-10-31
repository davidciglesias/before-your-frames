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

            GameObject copy = Object.Instantiate(__instance.gameObject, __instance.transform.parent, false);
            Object.DestroyImmediate(copy.GetComponent<GetVersionNumber>()); // or it would write the game's version
            copy.name = "BYE VR Version";
            var rt = (RectTransform)copy.transform;
            var src = (RectTransform)__instance.transform;
            float line = (label.preferredHeight > 1f ? label.preferredHeight : label.fontSize) * 1.15f;
            // The game's label sits at the bottom edge of the menu, so lift it a line and put ours in its place.
            rt.anchoredPosition = src.anchoredPosition;
            src.anchoredPosition += new Vector2(0f, line);
            copy.GetComponent<Text>().text = "VR V " + Plugin.Version;
        }
    }
}
