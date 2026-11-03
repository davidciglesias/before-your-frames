using System.Collections.Generic;

namespace BYEFrameBlink
{
    /// <summary>
    /// Captions of our menu entries in the game's languages. The game's text language is its subtitles choice
    /// (PlayerSettingsSaveData.settings.subtitlesPref); "None" keeps English.
    /// </summary>
    internal static class MenuText
    {
        private static readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>
        {
            // entries:          subtitles, vr settings, back, no subtitles, menu (the gaze menu opener)
            { "English",             new[] { "subtitles", "vr settings", "back", "no subtitles", "menu" } },
            { "German",              new[] { "untertitel", "vr-einstellungen", "zurück", "keine untertitel", "menü" } },
            { "French",              new[] { "sous-titres", "paramètres vr", "retour", "pas de sous-titres", "menu" } },
            { "Spanish",             new[] { "subtítulos", "ajustes de rv", "atrás", "sin subtítulos", "menú" } },
            { "Korean",              new[] { "자막", "VR 설정", "뒤로", "자막 없음", "메뉴" } },
            { "Simplified Chinese",  new[] { "字幕", "VR 设置", "返回", "无字幕", "菜单" } },
            { "Traditional Chinese", new[] { "字幕", "VR 設定", "返回", "無字幕", "選單" } },
            { "Japanese",            new[] { "字幕", "VR設定", "戻る", "字幕なし", "メニュー" } },
        };

        /// <summary>Each language's own name for itself.</summary>
        private static readonly Dictionary<string, string> Native = new Dictionary<string, string>
        {
            { "English", "english" }, { "German", "deutsch" }, { "French", "français" }, { "Spanish", "español" },
            { "Korean", "한국어" }, { "Simplified Chinese", "简体中文" }, { "Traditional Chinese", "繁體中文" }, { "Japanese", "日本語" },
        };

        private static string[] Current =>
            Words.TryGetValue(Subtitles.Current, out string[] w) ? w : Words["English"];

        /// <summary>The game's English and French menus are all lowercase; its German and Spanish menus capitalize ("Reanudar").</summary>
        public static string Cap(string s)
        {
            string lang = Subtitles.Current;
            if (string.IsNullOrEmpty(s) || !(lang == "German" || lang == "Spanish")) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        public static string SubtitlesEntry => Cap(Current[0]);
        public static string VrSettingsEntry => Cap(Current[1]);
        public static string Back => Cap(Current[2]);
        public static string NoSubtitles => Cap(Current[3]);
        public static string Menu => Cap(Current[4]);

        public static string LanguageName(string option) => Cap(Native.TryGetValue(option, out string n) ? n : option.ToLowerInvariant());

        // English phrase -> German, French, Spanish, Korean, Simplified Chinese, Traditional Chinese, Japanese.
        private static readonly Dictionary<string, string[]> Phrases = new Dictionary<string, string[]>
        {
            { "eye tracking",             new[] { "augenverfolgung", "suivi oculaire", "seguimiento ocular", "시선 추적", "眼动追踪", "眼動追蹤", "アイトラッキング" } },
            { "world size",               new[] { "weltgröße", "taille du monde", "tamaño del mundo", "세계 크기", "世界大小", "世界大小", "世界のサイズ" } },
            { "aiming dot",               new[] { "zielpunkt", "point de visée", "punto de mira", "조준점", "瞄准点", "瞄準點", "照準ドット" } },
            { "recenter each scene",      new[] { "jede szene zentrieren", "recentrer à chaque scène", "recentrar en cada escena", "장면마다 재중앙", "每个场景重新居中", "每個場景重新置中", "シーンごとに再センター" } },
            { "recenter view now",        new[] { "ansicht jetzt zentrieren", "recentrer la vue", "recentrar la vista ahora", "지금 시점 재중앙", "立即重新居中", "立即重新置中", "今すぐ再センター" } },
            { "render quality (in game)", new[] { "renderqualität (im spiel)", "qualité de rendu (en jeu)", "calidad de render (en juego)", "렌더 품질 (게임 중)", "渲染质量（游戏中）", "渲染品質（遊戲中）", "描画品質（ゲーム中）" } },
            { "performance mode",         new[] { "leistungsmodus", "mode performance", "modo rendimiento", "성능 모드", "性能模式", "效能模式", "パフォーマンスモード" } },
            { "no data",                  new[] { "keine daten", "pas de données", "sin datos", "데이터 없음", "无数据", "無資料", "データなし" } },
            { "eyes",                     new[] { "augen", "yeux", "ojos", "눈", "眼睛", "眼睛", "目" } },
            { "on",                       new[] { "an", "activé", "sí", "켬", "开", "開", "オン" } },
            { "off",                      new[] { "aus", "désactivé", "no", "끔", "关", "關", "オフ" } },
            { "auto",                     new[] { "auto", "auto", "auto", "자동", "自动", "自動", "自動" } },
            { "blinks only",              new[] { "nur blinzeln", "clignements seuls", "solo parpadeos", "깜빡임만", "仅眨眼", "僅眨眼", "まばたきのみ" } },
            { "head aim",                 new[] { "kopfzielen", "visée tête", "apuntar con la cabeza", "머리 조준", "头部瞄准", "頭部瞄準", "頭で照準" } },
            { "real",                     new[] { "echt", "réelle", "real", "실제", "真实", "真實", "実寸" } },
            { "a bit bigger",             new[] { "etwas größer", "un peu plus grande", "algo más grande", "조금 크게", "稍大", "稍大", "少し大きく" } },
            { "bigger",                   new[] { "größer", "plus grande", "más grande", "크게", "更大", "更大", "大きく" } },
            { "default",                  new[] { "standard", "par défaut", "predeterminado", "기본", "默认", "預設", "デフォルト" } },
            { "much bigger",              new[] { "viel größer", "beaucoup plus grande", "mucho más grande", "훨씬 크게", "大得多", "大得多", "かなり大きく" } },
        };

        private static readonly string[] Languages =
            { "German", "French", "Spanish", "Korean", "Simplified Chinese", "Traditional Chinese", "Japanese" };

        /// <summary>One phrase in the current language (English, or anything not in the table, stays as it is).</summary>
        public static string T(string english)
        {
            int i = System.Array.IndexOf(Languages, Subtitles.Current);
            return i >= 0 && Phrases.TryGetValue(english, out string[] t) ? t[i] : english;
        }

        /// <summary>A setting value, also of the form "auto (on)": each part translated.</summary>
        public static string V(string value)
        {
            int open = value.IndexOf(" (", System.StringComparison.Ordinal);
            if (open > 0 && value.EndsWith(")")) return T(value.Substring(0, open)) + " (" + T(value.Substring(open + 2, value.Length - open - 3)) + ")";
            return T(value);
        }

        /// <summary>A whole row caption: "name: value", translated and capitalized for the language.</summary>
        public static string Row(string name, string value) => Cap(T(name) + (value == null ? "" : ": " + V(value)));
    }
}
