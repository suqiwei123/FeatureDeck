using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT;
using WinRT.Interop;

namespace FeatureDeck.Services
{
    /// <summary>
    /// 应用外观（默认颜色）服务：
    /// 1. 持久化 跟随系统/浅色/深色 选择（settings.ini 的 theme= 键）；
    /// 2. 通过根元素 RequestedTheme 实时切换界面主题；
    /// 3. 修复 unpackaged 应用暗色模式下标题栏仍为亮色的问题
    ///    （DWMWA_USE_IMMERSIVE_DARK_MODE，需按生效主题手动打勾）；
    /// 4. 修复暗色下窗口背景纯黑（过黑）的问题：
    ///    Win11 22H2+ 使用 Mica 云母材质，否则回退到 #202020/#F3F3F3 实色。
    /// </summary>
    public static class ThemeService
    {
        public const string ThemeSystem = "system";
        public const string ThemeLight = "light";
        public const string ThemeDark = "dark";

        private const string ThemeSettingKey = "theme";

        // Win10 2004+ 为 20；更早构建上 20 无效、用 19（同一属性的旧编号）
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        private static Window _window;
        private static FrameworkElement _root;
        private static MicaController _mica;
        private static SystemBackdropConfiguration _backdropConfig;
        private static bool _windowHooksAttached;

        /// <summary>用户保存的主题偏好（system/light/dark）。</summary>
        public static string CurrentTheme { get; private set; } = ThemeSystem;

        /// <summary>
        /// 在窗口构造函数（InitializeComponent 之后、内容首帧之前）调用：
        /// 仅预设根元素主题，避免首帧按系统主题渲染后跳变。
        /// </summary>
        public static void Prepare(Window window, FrameworkElement root)
        {
            _window = window;
            _root = root;
            CurrentTheme = Normalize(AppResources.GetSetting(ThemeSettingKey, ThemeSystem));
            ApplyElementTheme();
        }

        /// <summary>在窗口 Loaded 后调用：接入 Mica、标题栏 DWM 属性与窗口事件。</summary>
        public static void Initialize(Window window, FrameworkElement root)
        {
            _window = window;
            _root = root;

            SetupBackdrop();
            AttachWindowHooks();

            root.ActualThemeChanged += (_, _) =>
            {
                SyncBackdropTheme();
                ApplyTitleBarTheme();
                ApplyFallbackBackground();
            };

            SyncBackdropTheme();
            ApplyTitleBarTheme();
            ApplyFallbackBackground();
        }

        /// <summary>切换并持久化主题偏好，立即生效。</summary>
        public static void SetTheme(string theme)
        {
            theme = Normalize(theme);
            if (theme == CurrentTheme) return;
            CurrentTheme = theme;
            AppResources.SetSetting(ThemeSettingKey, theme);
            ApplyElementTheme();
        }

        private static string Normalize(string theme) => theme switch
        {
            ThemeLight or ThemeDark => theme,
            _ => ThemeSystem,
        };

        private static void ApplyElementTheme()
        {
            _root.RequestedTheme = CurrentTheme switch
            {
                ThemeLight => ElementTheme.Light,
                ThemeDark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            // RequestedTheme 未变时不会触发 ActualThemeChanged，主动同步一次
            SyncBackdropTheme();
            ApplyTitleBarTheme();
            ApplyFallbackBackground();
        }

        private static bool IsActualDark() => _root.ActualTheme == ElementTheme.Dark;

        private static void SetupBackdrop()
        {
            if (_mica != null) return;
            try
            {
                if (!MicaController.IsSupported())
                    return; // Win10 / 不支持时保持实色回退背景

                _mica = new MicaController();
                _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true };
                SyncBackdropTheme();

                _mica.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
                _mica.SetSystemBackdropConfiguration(_backdropConfig);

                // 内容根保持透明，Mica 才能透出（WinUI 3 的 Window 没有 Background，
                // 背景一律画在内容根元素上）
                SetRootBackground(null);
            }
            catch
            {
                // Mica 初始化失败不影响主功能，走实色回退
                _mica = null;
                _backdropConfig = null;
            }
        }

        private static void SyncBackdropTheme()
        {
            if (_backdropConfig == null) return;
            _backdropConfig.Theme = _root.ActualTheme switch
            {
                ElementTheme.Dark => SystemBackdropTheme.Dark,
                ElementTheme.Light => SystemBackdropTheme.Light,
                _ => SystemBackdropTheme.Default,
            };
        }

        /// <summary>
        /// 暗色下让系统标题栏（含 caption 按钮）切换为深色样式。
        /// unpackaged 应用不会自动跟随主题，这是"黑色模式标题栏仍为亮色"的根因。
        /// </summary>
        private static void ApplyTitleBarTheme()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(_window);
                if (hwnd == IntPtr.Zero) return;

                int dark = IsActualDark() ? 1 : 0;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark, sizeof(int));
                if (hr != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                        ref dark, sizeof(int));
            }
            catch
            {
                // 标题栏样式失败不影响主功能
            }
        }

        private static void ApplyFallbackBackground()
        {
            if (_mica != null) return; // 有 Mica 时根元素保持透明

            // 替换暗色下纯黑的默认背景；浅色用 Windows 11 标准底色。
            // WinUI 3 的 Window 无 Background 属性，画在内容根上。
            var color = IsActualDark()
                ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20)
                : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
            SetRootBackground(new SolidColorBrush(color));
        }

        /// <summary>Background 定义在 Panel/Control 上而非 FrameworkElement。</summary>
        private static void SetRootBackground(Microsoft.UI.Xaml.Media.Brush brush)
        {
            if (_root is Panel panel) panel.Background = brush;
            else if (_root is Control control) control.Background = brush;
        }

        private static void AttachWindowHooks()
        {
            if (_windowHooksAttached) return;
            _windowHooksAttached = true;

            // Mica 在窗口失焦时按系统规范应停止云母响应
            _window.Activated += (_, e) =>
            {
                if (_backdropConfig != null)
                    _backdropConfig.IsInputActive =
                        e.WindowActivationState != WindowActivationState.Deactivated;
            };

            _window.Closed += (_, _) =>
            {
                try { _mica?.Dispose(); } catch { }
                _mica = null;
                _backdropConfig = null;
            };
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int size);
    }
}
