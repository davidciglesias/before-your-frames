using UnityEngine.SceneManagement;

namespace BYEFrameBlink
{
    /// <summary>
    /// Startup goes LogoSplashes -> EULA (auto-advances after 5 s) -> MainMenu. After the intro has been seen once
    /// (Intro Seen, set when the main menu is first reached), jump straight to the main menu when either of the first
    /// two scenes loads. LogoSplashes' objects still get their Awake (the switch happens at the end of that frame), so
    /// anything they set up for the rest of the game survives.
    /// </summary>
    internal static class IntroSkip
    {
        public static int Skipped { get; private set; }

        public static void Init() => SceneManager.sceneLoaded += OnSceneLoaded;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu")
            {
                if (!Plugin.IntroSeen.Value) Plugin.IntroSeen.Value = true;
                return;
            }
            if (!Plugin.SkipIntro.Value || !Plugin.IntroSeen.Value) return;
            if (scene.name != "LogoSplashes" && scene.name != "EULA") return;

            Skipped++;
            Plugin.Log.LogInfo($"Skipping intro scene '{scene.name}' (Skip Intro Logos = true).");
            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        }
    }
}
