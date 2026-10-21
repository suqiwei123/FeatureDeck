using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        // settings.ini 通用键值存储（key=value 一行一条）。
        // 必须整体读写：单独重写某一键会丢掉其余设置（旧版 SaveLanguage 只写 language）。
        private static Dictionary<string, string> _settings;

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

        /// <summary>
        /// 读取设置项；键不存在或值为空时返回 fallback。
        /// （语言为空即"跟随系统"，主题正常不会出现空值。）
        /// </summary>
        public static string GetSetting(string key, string fallback)
        {
            if (EnsureSettings().TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
            return fallback;
        }

        /// <summary>写入设置项并持久化全部键值。</summary>
        public static void SetSetting(string key, string value)
        {
            EnsureSettings()[key] = value ?? string.Empty;
            WriteSettings();
        }

        private static Dictionary<string, string> EnsureSettings()
        {
            if (_settings != null) return _settings;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SettingsFile))
                {
                    foreach (var line in File.ReadAllLines(SettingsFile))
                    {
                        var idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        map[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                    }
                }
            }
            catch
            {
                // 读取失败按空配置处理
            }
            return _settings = map;
        }

        private static void WriteSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllLines(SettingsFile,
                    EnsureSettings().Select(kv => kv.Key + "=" + kv.Value));
            }
            catch
            {
                // 保存失败不阻塞主功能
            }
        }

        private static void ApplyOverride()
        {
            try
            {
                // 必须用 MRT Core 版（Microsoft.Windows.Globalization）：
                // unpackaged 应用中 Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride
                // 会抛"进程没有包标识"异常且不生效；
                // MRT Core 版从 WinAppSDK 1.6 起支持 unpackaged，且同时影响
                // ResourceLoader.GetString 与 XAML 的 x:Uid 解析。
                // 注意：unpackaged 下该值不持久化，须在每次启动、任何资源加载前设置。
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage;
            }
            catch
            {
                // 忽略：设置失败则下次启动再试
            }
        }

        private static string ReadSavedLanguage()
            => GetSetting("language", FollowSystem);

        private static void SaveLanguage(string language)
            => SetSetting("language", language);
    }
}
