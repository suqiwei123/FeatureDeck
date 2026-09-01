using System;

namespace FeatureDeck.Services
{
    /// <summary>
    /// 根据特性英文技术名，推断其功能类别，返回资源键（由 AppResources 翻译成当前语言），
    /// 帮助用户一眼看出每个 ID 对应的大致功能方向。
    /// 名称是微软内部代号，无官方翻译，这里只做经验性归类，仅供辅助。
    /// 注意：token 均为足够长的精确词，避免 "ime"/"store"/"edge"/"input"
    /// 这类短词子串误伤（如 "Time" 含 "ime"、"Restore" 含 "store"）。
    /// </summary>
    public static class FeatureCategory
    {
        // 按顺序匹配，命中即返回；越靠前的规则优先级越高。Label 为资源键名。
        private static readonly (string[] Tokens, string ResourceKey)[] Rules =
        {
            (new[] { "filesystem", "refs", "ntfs", "storage", "disk", "smartsense" }, "CatFilesystem"),
            (new[] { "fileexplorer", "explorer", "shell" }, "CatExplorer"),
            (new[] { "taskbar", "systemtray" }, "CatTaskbar"),
            (new[] { "startmenu", "startexperience" }, "CatStartMenu"),
            (new[] { "desktopspotlight", "spotlight", "wallpaper", "lockscreen" }, "CatDesktop"),
            (new[] { "settingshomepage", "settingsrejuv", "settings" }, "CatSettings"),
            (new[] { "wsl", "linux", "virtualmachine", "hyperv", "hyper-v" }, "CatVirtual"),
            (new[] { "bitlocker", "defender", "firewall", "tpm", "windowssecurity" }, "CatSecurity"),
            (new[] { "audio", "sound", "volume" }, "CatAudio"),
            (new[] { "network", "wifi", "bluetooth", "ethernet", "wns", "vpn" }, "CatNetwork"),
            (new[] { "search", "indexer" }, "CatSearch"),
            (new[] { "widgets", "gadgets" }, "CatWidgets"),
            (new[] { "copilot", "recall", "windowsai" }, "CatAI"),
            (new[] { "snap", "windowing", "multitask" }, "CatWindowing"),
            (new[] { "print", "printer" }, "CatPrint"),
            (new[] { "camera", "capture", "screenrecording", "screenshots" }, "CatCamera"),
            (new[] { "battery", "power", "energy" }, "CatPower"),
            (new[] { "accessibilit", "narrator", "magnifier", "braille", "voiceaccess" }, "CatAccessibility"),
            (new[] { "xbox", "gaming", "gamebar", "directx", "dxgi" }, "CatGaming"),
            (new[] { "microsoftedge", "msedge", "browser" }, "CatBrowser"),
            (new[] { "notepad", "paint", "photo", "calculator", "mediaplayer" }, "CatApps"),
            (new[] { "microsoftstore", "appstore", "appinstaller", "winget" }, "CatStore"),
            (new[] { "windowsupdate", "servicing" }, "CatUpdate"),
            (new[] { "keyboard", "textinput", "inputmethod", "font", "typing", "handwriting" }, "CatInput"),
            (new[] { "yourphone", "android", "mobile" }, "CatPhone"),
        };

        /// <summary>
        /// 根据名称返回类别资源键（如 "CatFilesystem"）；无法归类时返回 null。
        /// 调用方用 AppResources.Get(key) 取得当前语言的类别文本。
        /// </summary>
        public static string Classify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var (tokens, resourceKey) in Rules)
            {
                foreach (var token in tokens)
                {
                    if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return resourceKey;
                }
            }

            return null;
        }
    }
}
