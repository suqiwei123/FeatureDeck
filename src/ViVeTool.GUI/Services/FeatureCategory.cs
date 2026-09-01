using System;

namespace ViVeTool.GUI.Services
{
    /// <summary>
    /// 根据特性英文技术名，推断其功能类别，返回简短中文标签，
    /// 帮助用户一眼看出每个 ID 对应的大致功能方向。
    /// 名称是微软内部代号，无官方中文翻译，这里只做经验性归类，仅供辅助。
    /// 注意：token 均为足够长的精确词，避免 "ime"/"store"/"edge"/"input"
    /// 这类短词子串误伤（如 "Time" 含 "ime"、"Restore" 含 "store"）。
    /// </summary>
    public static class FeatureCategory
    {
        // 按顺序匹配，命中即返回；越靠前的规则优先级越高
        private static readonly (string[] Keys, string Label)[] Rules =
        {
            (new[] { "filesystem", "refs", "ntfs", "storage", "disk", "smartsense" }, "文件系统/存储"),
            (new[] { "fileexplorer", "explorer", "shell" }, "文件资源管理器"),
            (new[] { "taskbar", "systemtray" }, "任务栏"),
            (new[] { "startmenu", "startexperience" }, "开始菜单"),
            (new[] { "desktopspotlight", "spotlight", "wallpaper", "lockscreen" }, "桌面/锁屏"),
            (new[] { "settingshomepage", "settingsrejuv", "settings" }, "设置"),
            (new[] { "wsl", "linux", "virtualmachine", "hyperv", "hyper-v" }, "虚拟化/WSL"),
            (new[] { "bitlocker", "defender", "firewall", "tpm", "windowssecurity" }, "安全"),
            (new[] { "audio", "sound", "volume" }, "音频"),
            (new[] { "network", "wifi", "bluetooth", "ethernet", "wns", "vpn" }, "网络"),
            (new[] { "search", "indexer" }, "搜索"),
            (new[] { "widgets", "gadgets" }, "小组件"),
            (new[] { "copilot", "recall", "windowsai" }, "AI/Copilot"),
            (new[] { "snap", "windowing", "multitask" }, "窗口管理"),
            (new[] { "print", "printer" }, "打印"),
            (new[] { "camera", "capture", "screenrecording", "screenshots" }, "摄像头/截图"),
            (new[] { "battery", "power", "energy" }, "电源/电池"),
            (new[] { "accessibilit", "narrator", "magnifier", "braille", "voiceaccess" }, "辅助功能"),
            (new[] { "xbox", "gaming", "gamebar", "directx", "dxgi" }, "游戏"),
            (new[] { "microsoftedge", "msedge", "browser" }, "浏览器"),
            (new[] { "notepad", "paint", "photo", "calculator", "mediaplayer" }, "内置应用"),
            (new[] { "microsoftstore", "appstore", "appinstaller", "winget" }, "应用商店"),
            (new[] { "windowsupdate", "servicing" }, "系统更新/维护"),
            (new[] { "keyboard", "textinput", "inputmethod", "font", "typing", "handwriting" }, "输入/字体"),
            (new[] { "yourphone", "android", "mobile" }, "手机连接"),
        };

        /// <summary>根据名称返回中文类别标签；无法归类时返回 null。</summary>
        public static string Classify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            foreach (var (keys, label) in Rules)
            {
                foreach (var key in keys)
                {
                    if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
                        return label;
                }
            }

            return null;
        }
    }
}
