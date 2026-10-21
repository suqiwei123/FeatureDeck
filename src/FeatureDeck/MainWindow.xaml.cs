using Albacore.ViVe.NativeEnums;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FeatureDeck.Models;
using FeatureDeck.Services;
using FeatureDeck.ViewModels;
using FeatureDeck.Views;
using WinRT.Interop;

namespace FeatureDeck
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();

        private DispatcherQueueTimer _searchTimer;

        public MainWindow()
        {
            InitializeComponent();
            Title = AppResources.Get("AppTitle");

            // 首帧前预设主题，避免按系统主题渲染后跳变；Mica/标题栏在 Loaded 后接入
            ThemeService.Prepare(this, Root);

            Root.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= OnLoaded;
            ThemeService.Initialize(this, Root);
            ViewModel.XamlRoot = Content.XamlRoot;
            ResizeWindow();

            // RadioButtons 子项不支持 x:Uid，且 Items 必须在控件挂载后才能填充
            // （构造函数里操作 RadioButtons.Items 会触发 XAML 内部异常导致启动崩溃 0xc000027b）
            StoreSelector.Items.Add(AppResources.Get("StoreRuntime"));
            StoreSelector.Items.Add(AppResources.Get("StoreBoot"));
            StoreSelector.Items.Add(AppResources.Get("StoreBoth"));

            if (!ViewModel.IsBuildSupported)
            {
                await ShowDialogAsync(
                    AppResources.Get("UnsupportedTitle"),
                    AppResources.Format("UnsupportedMessageFormat",
                        AppResources.Format("CurrentBuildFormat", ViewModel.BuildNumber)));
                return;
            }

            // 系统语言不受支持时，自动弹出语言选择界面
            if (AppResources.NeedsLanguageSelection)
            {
                await ShowLanguagePickerAsync(automatic: true);
            }

            await ViewModel.InitializeAsync();
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
            => await ShowSettingsAsync();

        /// <summary>
        /// 打开修改历史对话框。对话框内可能撤销过记录，关闭后刷新主列表，
        /// 没有撤销则不打扰（刷新要重新枚举全部特性配置）。
        /// </summary>
        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HistoryDialog { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();

            if (dialog.ViewModel.AnyUndone)
                await ViewModel.RefreshAsync();
        }

        /// <summary>
        /// 设置对话框：默认颜色（即时生效）+ 界面语言（重启生效）。
        /// 语言选择在对话框关闭后再应用，避免与重启提示框叠放导致第二个 ShowAsync 被吞。
        /// </summary>
        private async Task ShowSettingsAsync()
        {
            var panel = new StackPanel { Spacing = 8 };

            panel.Children.Add(new TextBlock
            {
                Text = AppResources.Get("SettingsThemeHeader"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var themeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            themeBox.Items.Add(AppResources.Get("ThemeFollowSystem"));
            themeBox.Items.Add(AppResources.Get("ThemeLight"));
            themeBox.Items.Add(AppResources.Get("ThemeDark"));
            themeBox.SelectedIndex = ThemeService.CurrentTheme switch
            {
                ThemeService.ThemeLight => 1,
                ThemeService.ThemeDark => 2,
                _ => 0,
            };
            panel.Children.Add(themeBox);

            panel.Children.Add(new TextBlock
            {
                Text = AppResources.Get("SettingsLanguageHeader"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 0)
            });

            var langBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            langBox.Items.Add(AppResources.Get("LangFollowSystem"));
            langBox.Items.Add(AppResources.Get("LangChinese"));
            langBox.Items.Add(AppResources.Get("LangEnglish"));
            var savedLang = AppResources.UserOverrideLanguage;
            langBox.SelectedIndex = savedLang switch
            {
                AppResources.DefaultLanguage => 1,
                AppResources.EnglishLanguage => 2,
                _ => 0,
            };
            panel.Children.Add(langBox);

            panel.Children.Add(new TextBlock
            {
                Text = AppResources.Get("SettingsNote"),
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });

            // 主题即时生效
            themeBox.SelectionChanged += (_, _) =>
            {
                var setting = themeBox.SelectedIndex switch
                {
                    1 => ThemeService.ThemeLight,
                    2 => ThemeService.ThemeDark,
                    _ => ThemeService.ThemeSystem,
                };
                ThemeService.SetTheme(setting);
            };

            // 语言仅记录选择，关闭对话框后再应用
            string pendingLang = null;
            langBox.SelectionChanged += (_, _) =>
            {
                pendingLang = langBox.SelectedIndex switch
                {
                    1 => AppResources.DefaultLanguage,
                    2 => AppResources.EnglishLanguage,
                    _ => AppResources.FollowSystem,
                };
            };

            var dialog = new ContentDialog
            {
                Title = AppResources.Get("SettingsDialogTitle"),
                Content = panel,
                CloseButtonText = AppResources.Get("OK"),
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();

            if (pendingLang == null || pendingLang == savedLang) return;
            AppResources.SetLanguage(pendingLang);
            await ShowRestartHintAsync();
        }

        /// <summary>语言已保存后提示重启；用户选择立即重启时执行。</summary>
        private async Task ShowRestartHintAsync()
        {
            var restart = new ContentDialog
            {
                Title = AppResources.Get("LangTitle"),
                Content = AppResources.Get("LangRestartHint"),
                PrimaryButtonText = AppResources.Get("LangRestartNow"),
                CloseButtonText = AppResources.Get("LangLater"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await restart.ShowAsync();
            if (result == ContentDialogResult.Primary)
                RestartApp();
        }

        /// <summary>弹出语言选择界面。automatic=true 表示系统语言不支持时自动触发。</summary>
        private async Task ShowLanguagePickerAsync(bool automatic)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = AppResources.Get("LangBody"),
                TextWrapping = TextWrapping.Wrap
            });

            var followBtn = new Button
            {
                Content = AppResources.Get("LangFollowSystem"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var zhBtn = new Button
            {
                Content = AppResources.Get("LangChinese"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var enBtn = new Button
            {
                Content = AppResources.Get("LangEnglish"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(followBtn);
            panel.Children.Add(zhBtn);
            panel.Children.Add(enBtn);

            var dialog = new ContentDialog
            {
                Title = AppResources.Get("LangTitle"),
                Content = panel,
                CloseButtonText = AppResources.Get("LangLater"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            string chosen = null;
            followBtn.Click += (_, _) => { chosen = AppResources.FollowSystem; dialog.Hide(); };
            zhBtn.Click += (_, _) => { chosen = AppResources.DefaultLanguage; dialog.Hide(); };
            enBtn.Click += (_, _) => { chosen = AppResources.EnglishLanguage; dialog.Hide(); };

            await dialog.ShowAsync();
            if (chosen == null) return; // 用户点「稍后」，保持当前语言

            AppResources.SetLanguage(chosen);

            // 语言切换需重启生效
            await ShowRestartHintAsync();
        }

        private static void RestartApp()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            }
            catch
            {
                // 重启失败则用户可手动重启
            }
            Application.Current.Exit();
        }

        // 快捷启用/禁用：输入特性 ID 后一键写入，目标存储跟随顶栏选择器
        private async void QuickOpButton_Click(object sender, RoutedEventArgs e)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = AppResources.Format("QuickOpDescFormat", ViewModel.StoreDescription),
                TextWrapping = TextWrapping.Wrap
            });

            var idBox = new TextBox
            {
                PlaceholderText = AppResources.Get("QuickIdBox.PlaceholderText"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            panel.Children.Add(idBox);

            var preview = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(preview);

            idBox.TextChanged += (_, _) => UpdateQuickPreview(idBox.Text, preview);

            var dialog = new ContentDialog
            {
                Title = AppResources.Get("QuickOpTitle"),
                Content = panel,
                PrimaryButtonText = AppResources.Get("QuickEnable"),
                SecondaryButtonText = AppResources.Get("QuickDisable"),
                CloseButtonText = AppResources.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await ViewModel.QuickEnableByFeatureIdAsync(idBox.Text);
            else if (result == ContentDialogResult.Secondary)
                await ViewModel.QuickDisableByFeatureIdAsync(idBox.Text);
        }

        private void UpdateQuickPreview(string text, TextBlock preview)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                !uint.TryParse(text.Trim(), out var featureId))
            {
                preview.Text = string.Empty;
                return;
            }

            if (!ViewModel.HasFeatureId(featureId))
            {
                preview.Text = AppResources.Get("QuickNotFound");
                return;
            }

            var name = FeatureNaming.GetName(featureId);
            preview.Text = string.IsNullOrEmpty(name)
                ? AppResources.Format("UnnamedFeatureFormat", featureId)
                : AppResources.Format("QuickNameFormat", name);
        }

        private void ResizeWindow()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow.Resize(new Windows.Graphics.SizeInt32(1600, 900));
                // 设置窗口图标（标题栏 + 任务栏）。exe 图标由 csproj 的 ApplicationIcon 设置；
                // 这里设的是运行时窗口图标，让标题栏/任务栏显示应用图标而非通用窗口图标。
                try
                {
                    var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "FeatureDeck.ico");
                    if (System.IO.File.Exists(iconPath))
                        appWindow.SetIcon(iconPath);
                }
                catch
                {
                    // 图标设置失败不影响主功能
                }
            }
            catch
            {
                // 调整尺寸失败不影响主功能
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.RefreshAsync();

        private void StoreSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.Store = (StoreTarget)StoreSelector.SelectedIndex;
            ViewModel.ApplyFilter();
        }

        private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ViewModel.FilterIndex = FilterBox.SelectedIndex;
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            if (_searchTimer == null)
            {
                _searchTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _searchTimer.IsRepeating = false;
                _searchTimer.Tick += (_, _) => ViewModel.SearchText = SearchBox.Text;
            }

            _searchTimer.Stop();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(250);
            _searchTimer.Start();
        }

        private void FeatureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => ViewModel.UpdateSelection(FeatureList.SelectedItems);

        private async void EnableButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.EnableSelectedAsync();

        private async void DisableButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.DisableSelectedAsync();

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.ResetSelectedAsync();

        private async void FullResetButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.FullResetAsync();

        private async void FixLkgButton_Click(object sender, RoutedEventArgs e)
            => await ViewModel.FixLkgAsync();

        private async void RowEnable_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveItem(sender) is FeatureItem item)
                await ViewModel.EnableSingleAsync(item);
        }

        private async void RowDisable_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveItem(sender) is FeatureItem item)
                await ViewModel.DisableSingleAsync(item);
        }

        private async void RowReset_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveItem(sender) is FeatureItem item)
                await ViewModel.ResetSingleAsync(item);
        }

        private static FeatureItem ResolveItem(object sender)
        {
            if (sender is not FrameworkElement element)
                return null;
            return element.DataContext as FeatureItem ?? element.Tag as FeatureItem;
        }

        private async Task ShowDialogAsync(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = AppResources.Get("OK"),
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
