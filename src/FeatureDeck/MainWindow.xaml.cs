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

            Root.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= OnLoaded;
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

        private async void LanguageButton_Click(object sender, RoutedEventArgs e)
            => await ShowLanguagePickerAsync(automatic: false);

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

        private void ResizeWindow()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                appWindow.Resize(new Windows.Graphics.SizeInt32(1600, 900));
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
