using System;
using System.Globalization;
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

        /// <summary>当前生效的语言代码（两字母或完整 BCP-47），如 zh-CN / en-US。</summary>
        public static string CurrentLanguage { get; private set; } = DefaultLanguage;

        /// <summary>用户是否手动覆盖过语言（true=跟随系统则忽略）。</summary>
        public static bool UserOverride { get; private set; }

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

            return null; // 不支持 → 调用方决定（默认英文并提示）
        }

        /// <summary>启动时调用一次：决定默认语言（跟随系统，不支持时回退英文）。</summary>
        public static void InitializeLanguage()
        {
            var resolved = ResolveLanguage();
            // 系统语言不受支持时回退英文（界面可完整显示，不弹窗打扰）
            CurrentLanguage = resolved ?? EnglishLanguage;
            UserOverride = false;

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = CurrentLanguage;
            }
            catch
            {
                // 忽略：设置失败则下次启动再试
            }
        }
    }
}
