using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BYEFrameBlink
{
    /// <summary>
    /// In VR the webcam is never used (blinks come from the headset), so the menus' webcam options are only clutter:
    /// blink calibration, the webcam picker, the eye detection guide. Hide the buttons and toggles whose caption is
    /// about them while in VR (the rest of the column closes up through MenuSpread), and bring them back otherwise.
    /// </summary>
    internal static class MenuDeclutter
    {
        // Matched on object names, which stay English whatever the language (captions are translated).
        private static readonly string[] Keywords = { "webcam", "blink cal", "faq sheet", "calibrat", "streamer" };
        private static readonly List<GameObject> Hidden = new List<GameObject>();
        private static float _nextScan;

        public static void Update()
        {
            bool vr = UnityEngine.XR.XRSettings.enabled;
            if (!vr)
            {
                if (Hidden.Count > 0) { foreach (GameObject g in Hidden) if (g != null) g.SetActive(true); Hidden.Clear(); }
                return;
            }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;

            MainMenuController mmc = MainMenuController.Instance;
            PauseMenuController pmc = PauseMenuController.instance;
            Scan(mmc != null ? mmc.mainMenu : null);
            Scan(pmc != null ? pmc.pauseMenu : null);
            // Some sit on the menu canvas outside the menu object (the main menu's camera-feed toggle, top right).
            Canvas canvas = mmc != null && mmc.mainMenu != null && mmc.mainMenu.activeInHierarchy ? mmc.mainMenu.GetComponentInParent<Canvas>() : null;
            if (canvas != null) Scan(canvas.rootCanvas.gameObject);
        }

        private static void Scan(GameObject menu)
        {
            if (menu == null || !menu.activeInHierarchy) return;
            foreach (Selectable s in menu.GetComponentsInChildren<Selectable>(false))
            {
                if (VrSettingsMenu.IsRow(s)) continue;
                string caption = s.name;
                if (caption == null) continue;
                foreach (string k in Keywords)
                {
                    if (caption.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    s.gameObject.SetActive(false);
                    Hidden.Add(s.gameObject);
                    Plugin.Log.LogInfo($"Hid '{caption}' ({s.name}) in VR: webcam options don't apply.");
                    break;
                }
            }
        }

    }
}
