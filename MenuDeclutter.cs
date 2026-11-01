using System.Collections.Generic;
using System.Linq;
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
        private static float _nextScan, _nextDeviceCheck, _nextTextScan;
        private static string[] _deviceNames = new string[0];

        public static void Update()
        {
            bool vr = UnityEngine.XR.XRSettings.enabled;
            if (!vr)
            {
                if (Hidden.Count > 0) { foreach (GameObject g in Hidden) if (g != null) g.SetActive(true); Hidden.Clear(); }
                return;
            }
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.25f;

            // Texts too: the main menu shows the chosen webcam's name (a phone offered as a virtual camera, say) as text.
            if (Time.unscaledTime >= _nextDeviceCheck)
            {
                _nextDeviceCheck = Time.unscaledTime + 10f;
                try { _deviceNames = WebCamTexture.devices.Select(d => d.name).Where(n => !string.IsNullOrEmpty(n)).ToArray(); }
                catch { _deviceNames = new string[0]; }
            }
            if (Time.unscaledTime >= _nextTextScan) // once a second: it looks through every text
            {
                _nextTextScan = Time.unscaledTime + 1f;
                foreach (Graphic g in Object.FindObjectsOfType<Graphic>())
                {
                    if (g == null || !g.isActiveAndEnabled || !(g is Text || g is TMPro.TMP_Text) || g.GetComponentInParent<Selectable>() != null) continue;
                    string text = g is Text t ? t.text : ((TMPro.TMP_Text)g).text;
                    bool named = Keywords.Any(k => g.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    bool device = !string.IsNullOrEmpty(text) && _deviceNames.Any(n => text.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!named && !device) continue;
                    g.gameObject.SetActive(false);
                    Hidden.Add(g.gameObject);
                    Plugin.Log.LogInfo($"Hid text '{g.name}' in VR: it's about the webcam.");
                }
            }

            // Every active control, wherever it sits: the webcam picker and camera-feed toggle live on other canvases
            // and panels than the menus themselves.
            foreach (Selectable s in new List<Selectable>(Selectable.allSelectables)) // a copy: hiding one removes it from the list
            {
                if (s == null || !s.isActiveAndEnabled) continue;
                if (VrSettingsMenu.IsRow(s)) continue;
                string caption = s.name;
                if (caption == null) continue;
                // The webcam picker on the main menu is a plain "Dropdown" listing the camera devices.
                if (s is Dropdown dd && dd.options.Any(o => o != null && _deviceNames.Contains(o.text)))
                {
                    s.gameObject.SetActive(false);
                    Hidden.Add(s.gameObject);
                    Plugin.Log.LogInfo($"Hid '{caption}' in VR: it lists webcams.");
                    continue;
                }
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
