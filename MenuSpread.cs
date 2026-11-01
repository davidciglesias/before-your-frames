using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// The game's menus stack their buttons closely, made for a mouse. Picking one by gaze is easier with more room
    /// between them, so while a menu is open in VR, restack the buttons of each vertical column downward from the
    /// top option (titles sit above) at the game's step times Menu Spacing, closing the gaps of options hidden in VR
    /// (MenuDeclutter). The main menu goes into two columns, side by side. Everything is put back when the menu
    /// closes. Our own settings pages lay themselves out, so they're left alone.
    /// </summary>
    internal static class MenuSpread
    {
        private struct Entry { public Vector2 Original, Applied; }

        private const float ColumnMargin = 60f;
        private static readonly Dictionary<RectTransform, Entry> Moved = new Dictionary<RectTransform, Entry>();
        private static float _nextScan;

        public static void Update(bool menuActive)
        {
            float k = Mathf.Clamp(Plugin.MenuSpacing.Value, 1f, 2.5f);
            if (!menuActive || k <= 1.001f || VrSettingsMenu.IsOpen) { RestoreAll(); return; }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;

            MainMenuController mmc = MainMenuController.Instance;
            Transform mainMenu = mmc != null && mmc.mainMenu != null ? mmc.mainMenu.transform : null;
            Transform chapters = mmc != null && mmc.chaptersMenu != null ? mmc.chaptersMenu.transform : null;
            var buttons = Selectable.allSelectables
                .Where(s => s != null && s.isActiveAndEnabled && s is Button && !VrSettingsMenu.IsRow(s) && s.transform.parent != null)
                .Select(s => (RectTransform)s.transform);
            foreach (var column in buttons.GroupBy(r => r.parent))
            {
                List<RectTransform> col = column.ToList();
                // Short menus only (main, pause): long lists like chapter select scroll or are packed on purpose.
                bool chapterList = chapters != null && column.Key.IsChildOf(chapters);
                if (col.Count < 3 || col.Count > (chapterList ? 16 : 8) || (!chapterList && col[0].GetComponentInParent<ScrollRect>() != null)) continue;

                // Positions as the game laid them out (ours undone), so re-spreading doesn't compound.
                var original = new Dictionary<RectTransform, Vector2>();
                foreach (RectTransform r in col)
                {
                    Vector2 p = r.anchoredPosition;
                    original[r] = Moved.TryGetValue(r, out Entry e) && (e.Applied - p).sqrMagnitude < 1e-4f ? e.Original : p;
                }
                // Only vertical lists: the buttons share (roughly) one x.
                float minX = original.Values.Min(p => p.x), maxX = original.Values.Max(p => p.x);
                if (maxX - minX > 30f) continue;

                List<RectTransform> ordered = col.OrderByDescending(r => original[r].y).ToList();
                var gaps = new List<float>();
                for (int i = 1; i < ordered.Count; i++) gaps.Add(original[ordered[i - 1]].y - original[ordered[i]].y);
                gaps.Sort();
                float step = gaps.FirstOrDefault(g => g > 1f); // smallest real gap = the game's step; larger ones are hidden options
                if (step <= 1f) continue;
                Vector2 top = original[ordered[0]];

                bool twoColumns = column.Key == mainMenu || chapterList;
                // A back button in the list (chapter select) goes on its own row below the columns.
                RectTransform back = twoColumns ? ordered.FirstOrDefault(r => r.name.IndexOf("back", System.StringComparison.OrdinalIgnoreCase) >= 0) : null;
                if (back != null) ordered.Remove(back);
                int perColumn = twoColumns ? (ordered.Count + 1) / 2 : ordered.Count;
                float dx = twoColumns ? ColumnOffset(ordered, perColumn) : 0f;
                // Two columns: spread the rows over the height the single column used (times Menu Spacing where it
                // fits: the main menu has room; chapter select already filled the menu), so options are far apart.
                float rowStep = step * k;
                if (twoColumns)
                {
                    float height = (ordered.Count + (back != null ? 1 : 0) - 1) * step * (chapterList ? 1f : k);
                    float rows = (perColumn - 1) + (back != null ? 1.5f : 0f);
                    if (rows > 0f) rowStep = Mathf.Max(rowStep, height / rows);
                }
                for (int i = 0; i < ordered.Count; i++)
                {
                    RectTransform r = ordered[i];
                    Vector2 want = new Vector2(top.x + (i < perColumn ? 0f : dx), top.y - (i % perColumn) * rowStep);
                    if ((r.anchoredPosition - want).sqrMagnitude > 1e-4f) r.anchoredPosition = want;
                    Moved[r] = new Entry { Original = original[r], Applied = want };
                }
                if (back != null)
                {
                    Vector2 want = new Vector2(top.x + dx * 0.5f, top.y - (perColumn - 1 + 1.5f) * rowStep);
                    if ((back.anchoredPosition - want).sqrMagnitude > 1e-4f) back.anchoredPosition = want;
                    Moved[back] = new Entry { Original = original[back], Applied = want };
                }
            }
        }

        /// <summary>The second column starts a margin after the first column's widest text ends.</summary>
        private static float ColumnOffset(List<RectTransform> ordered, int perColumn)
        {
            float firstRight = float.MinValue, secondLeft = float.MaxValue;
            for (int i = 0; i < ordered.Count; i++)
            {
                Vector2 span = VrSettingsMenu.TextSpanOf(ordered[i].GetComponent<Selectable>());
                if (i < perColumn) firstRight = Mathf.Max(firstRight, span.y);
                else secondLeft = Mathf.Min(secondLeft, span.x);
            }
            return firstRight - secondLeft + ColumnMargin;
        }

        public static void RestoreAll()
        {
            if (Moved.Count == 0) return;
            foreach (var kv in Moved)
            {
                RectTransform r = kv.Key;
                if (r != null && (r.anchoredPosition - kv.Value.Applied).sqrMagnitude < 1e-4f) r.anchoredPosition = kv.Value.Original;
            }
            Moved.Clear();
        }
    }
}
