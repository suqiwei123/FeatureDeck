using Albacore.ViVe.NativeEnums;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using ViVeTool.GUI.Models;
using ViVeTool.GUI.ViewModels;
using WinRT.Interop;

namespace ViVeTool.GUI
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new();

        private DispatcherQueueTimer _searchTimer;

        public MainWindow()
        {
            InitializeComponent();
            Title = "ViVe 图形化工具";
            Root.Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Root.Loaded -= OnLoaded;
            ViewModel.XamlRoot = Content.XamlRoot;
            ResizeWindow();

            if (!ViewModel.IsBuildSupported)
            {
                await ShowDialogAsync("系统版本不受支持",
                    $"ViVe 需要 Windows 10 build 18963 或更新的版本。\n当前内部版本：{ViewModel.BuildNumber}");
                return;
            }

            await ViewModel.InitializeAsync();
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
                CloseButtonText = "确定",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
