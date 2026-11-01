using UnityEngine;

namespace BYEFrameBlink
{
    /// <summary>
    /// Subtitles for our VR "settings" page. The game's settings page picks them with a dropdown, which is hard to use
    /// by gaze; its subtitles choice is also the text language (None, or one of eight languages). Set the same values
    /// and apply/save them the way its SettingsSubMenu.Apply("subtitles") does.
    /// </summary>
    internal static class Subtitles
    {
        public static readonly string[] Options =
            { "None", "English", "German", "French", "Spanish", "Korean", "Simplified Chinese", "Traditional Chinese", "Japanese" };

        public static string Current
        {
            get
            {
                string v = PlayerSettingsSaveData.Instance != null ? PlayerSettingsSaveData.Instance.settings.subtitlesPref : null;
                return string.IsNullOrEmpty(v) ? "None" : v;
            }
        }

        /// <summary>Menu caption for one option, marked when it's the current choice.</summary>
        public static string Caption(string option)
        {
            string name = option == "None" ? MenuText.NoSubtitles : MenuText.LanguageName(option);
            return option == Current ? "> " + name + " <" : name;
        }

        public static void Set(string option)
        {
            PlayerSettingsSaveData save = PlayerSettingsSaveData.Instance;
            if (save == null) return;
            save.settings.subtitlesPref = option;
            if (DialogueManager.instance != null) DialogueManager.instance.SetSubtitles(option);
            foreach (LocalizationWorldText t in Object.FindObjectsOfType<LocalizationWorldText>()) t.ChangeLanguage();
            save.SaveSettingsToDisk();
            Plugin.Log.LogInfo($"Subtitles set to {option}.");
        }
    }
}
