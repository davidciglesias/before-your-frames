using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's menus stack their buttons closely, made for a mouse. Picking one by gaze is easier with more room
    /// between them, so while a menu is open in VR, move the buttons of each vertical column apart (downward from
    /// the top option, since titles sit above) by Menu Spacing, and put them back when the menu closes. Our own VR settings rows are laid out
    /// from the spread spacing already, so they're left alone.
    /// </summary>
    internal static class MenuSpread
    {
        private struct Entry { public float Original, Applied; }

        private static readonly Dictionary<RectTransform, Entry> Moved = new Dictionary<RectTransform, Entry>();
        private static float _nextScan;

        public static void Update(bool menuActive)
        {
            float k = Mathf.Clamp(Plugin.MenuSpacing.Value, 1f, 2.5f);
            // Our VR settings page has 10 rows: with the game's spacing they just fit, spread they would run off the menu.
            if (!menuActive || k <= 1.001f || VrSettingsMenu.IsOpen) { RestoreAll(); return; }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;

            var buttons = Selectable.allSelectables
                .Where(s => s != null && s.isActiveAndEnabled && s is Button && !VrSettingsMenu.IsRow(s) && s.transform.parent != null)
                .Select(s => (RectTransform)s.transform);
            foreach (var column in buttons.GroupBy(r => r.parent))
            {
                List<RectTransform> col = column.ToList();
                // Short menus only (main, pause): long lists like chapter select scroll or are packed on purpose.
                if (col.Count < 3 || col.Count > 8 || col[0].GetComponentInParent<ScrollRect>() != null) continue;
                // Only vertical lists: the buttons share (roughly) one x.
                float minX = col.Min(r => r.anchoredPosition.x), maxX = col.Max(r => r.anchoredPosition.x);
                if (maxX - minX > 30f) continue;

                // Positions as the game laid them out (ours undone), so re-spreading doesn't compound.
                var original = new Dictionary<RectTransform, float>();
                foreach (RectTransform r in col)
                {
                    float y = r.anchoredPosition.y;
                    original[r] = Moved.TryGetValue(r, out Entry e) && Mathf.Abs(e.Applied - y) < 0.01f ? e.Original : y;
                }
                float centre = original.Values.Max(); // keep the top option in place (titles sit above): spread downward
                foreach (RectTransform r in col)
                {
                    float want = centre + (original[r] - centre) * k;
                    if (Mathf.Abs(r.anchoredPosition.y - want) > 0.01f) r.anchoredPosition = new Vector2(r.anchoredPosition.x, want);
                    Moved[r] = new Entry { Original = original[r], Applied = want };
                }
            }
        }

        public static void RestoreAll()
        {
            if (Moved.Count == 0) return;
            foreach (var kv in Moved)
            {
                RectTransform r = kv.Key;
                if (r != null && Mathf.Abs(r.anchoredPosition.y - kv.Value.Applied) < 0.01f)
                    r.anchoredPosition = new Vector2(r.anchoredPosition.x, kv.Value.Original);
            }
            Moved.Clear();
        }
    }
}
