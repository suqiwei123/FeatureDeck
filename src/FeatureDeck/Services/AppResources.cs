using System;
using System.Globalization;
using System.IO;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FeatureDeck.Services
{
    /// <summary>
    /// 本地化资源访问入口。
    /// 提供：键值读取（AppResources.Get）、系统语言检测（ResolveLanguage）、
    /// 以及统一的语言覆盖设置（跟随系统 or 强制某语言）。
    /// 注意：unpackaged 应用须用 MRT Core 版 ResourceLoader（Microsoft.Windows.*），
    /// 且构建产物必须含 resources.pri（见 csproj 的 CopyPriToResourcesPri target）。
    /// </summary>
    public static class AppResources
    {
        private static ResourceLoader _loader;

        public const string DefaultLanguage = "zh-CN";
        public const string EnglishLanguage = "en-US";
        public const string FollowSystem = "";

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FeatureDeck");
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.ini");

        /// <summary>当前生效的语言代码（两字母或完整 BCP-47），如 zh-CN / en-US。</summary>
        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        /// <summary>用户是否手动覆盖过语言（true=跟随系统则忽略）。</summary>
        public static bool UserOverride { get; private set; }

        /// <summary>用户手动选择的语言代码；空串表示跟随系统。</summary>
        public static string UserOverrideLanguage { get; private set; } = FollowSystem;

        /// <summary>启动时系统语言不受支持，需要弹出语言选择界面。</summary>
        public static bool NeedsLanguageSelection { get; private set; }

        private static ResourceLoader Loader
            => _loader ??= new ResourceLoader();

        /// <summary>读取本地化字符串；键不存在时返回键名（便于发现缺失）。</summary>
        public static string Get(string key)
        {
            try
            {
                var text = Loader.GetString(key);
                return string.IsNullOrEmpty(text) ? key : text;
            }
            catch
            {
                return key;
            }
        }

        /// <summary>格式化读取本地化字符串。</summary>
        public static string Format(string key, params object[] args)
        {
            try
            {
                return string.Format(Get(key), args);
            }
            catch
            {
                return Get(key);
            }
        }

        /// <summary>
        /// 根据系统语言解析应使用的语言：
        /// 优先返回 zh-CN（简体中文）、en-US（英语）；
        /// 其他语言返回 null，表示不受支持（调用方应回退英文或提示选择）。
        /// </summary>
        public static string ResolveLanguage()
        {
            try
            {
                foreach (var lang in Windows.System.UserProfile.GlobalizationPreferences.Languages)
                {
                    if (string.IsNullOrWhiteSpace(lang)) continue;

                    var lower = lang.ToLowerInvariant();
                    if (lower.StartsWith("zh", StringComparison.Ordinal))
                        return DefaultLanguage;
                    if (lower.StartsWith("en", StringComparison.Ordinal))
                        return EnglishLanguage;
                }
            }
            catch
            {
                // 读取系统语言失败时回退系统区域
            }

            // 系统语言列表为空或不受支持：尝试用当前线程区域兜底
            var ui = CultureInfo.CurrentUICulture?.Name ?? string.Empty;
            if (ui.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return DefaultLanguage;
            if (ui.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return EnglishLanguage;

            return null; // 不支持 → 调用方决定
        }

        /// <summary>
        /// 启动时调用一次：决定默认语言。
        /// 优先级：用户已保存的选择 &gt; 跟随系统（支持则用之）&gt; 系统不支持时标记 NeedsLanguageSelection（临时用英文）。
        /// </summary>
        public static void InitializeLanguage()
        {
            var saved = ReadSavedLanguage();
            if (!string.IsNullOrEmpty(saved))
            {
                CurrentLanguage = saved;
                UserOverride = true;
                UserOverrideLanguage = saved;
                ApplyOverride();
                return;
            }

            var resolved = ResolveLanguage();
            if (resolved != null)
            {
                CurrentLanguage = resolved;
                UserOverride = false;
                UserOverrideLanguage = FollowSystem;
                ApplyOverride();
                return;
            }

            // 系统语言不受支持：先用英文保证界面可读，并标记需要弹语言选择界面
            CurrentLanguage = EnglishLanguage;
            UserOverride = false;
            UserOverrideLanguage = FollowSystem;
            NeedsLanguageSelection = true;
            ApplyOverride();
        }

        /// <summary>用户手动设置语言；传 FollowSystem 表示恢复跟随系统。</summary>
        public static void SetLanguage(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                // 恢复跟随系统
                UserOverride = false;
                UserOverrideLanguage = FollowSystem;
                SaveLanguage(FollowSystem);
                var resolved = ResolveLanguage();
                CurrentLanguage = resolved ?? EnglishLanguage;
            }
            else
            {
                UserOverride = true;
                UserOverrideLanguage = language;
                CurrentLanguage = language;
                SaveLanguage(language);
            }

            NeedsLanguageSelection = false;
            ApplyOverride();
        }

        /// <summary>是否有可用的语言选项（用于切换界面判断）。</summary>
        public static bool IsSupported(string language)
            => language == DefaultLanguage || language == EnglishLanguage;

        private static void ApplyOverride()
        {
            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage;
            }
            catch
            {
                // 忽略：设置失败则下次启动再试
            }
        }

        private static string ReadSavedLanguage()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return FollowSystem;
                var lines = File.ReadAllLines(SettingsFile);
                foreach (var line in lines)
                {
                    if (line.StartsWith("language=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring("language=".Length).Trim();
                }
            }
            catch
            {
                // 读取失败按未设置处理
            }
            return FollowSystem;
        }

        private static void SaveLanguage(string language)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllLines(SettingsFile, new[] { "language=" + language });
            }
            catch
            {
                // 保存失败不阻塞主功能
            }
        }
    }
}
