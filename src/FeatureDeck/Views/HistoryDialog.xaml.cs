using Microsoft.UI.Xaml.Controls;
using FeatureDeck.Services;
using FeatureDeck.ViewModels;

namespace FeatureDeck.Views
{
    /// <summary>
    /// 修改历史对话框：左侧记录列表，右侧变更详情与撤销入口。
    /// 撤销的二次确认与结果反馈都在本对话框内完成，不叠加二级 ContentDialog
    /// （WinUI 3 同一时刻只允许一个 ContentDialog 处于打开状态）。
    /// </summary>
    public sealed partial class HistoryDialog : ContentDialog
    {
        public HistoryViewModel ViewModel { get; } = new();

        public HistoryDialog()
        {
            InitializeComponent();

            Title = AppResources.Get("HistoryDialogTitle");
            CloseButtonText = AppResources.Get("OK");

            // 默认 548px 放不下「列表 + 详情」两栏，放开本对话框的宽度限制
            Resources["ContentDialogMaxWidth"] = 980.0;
            Resources["ContentDialogMinWidth"] = 760.0;

            RecordList.SelectionChanged += (_, _) => FeedbackBar.IsOpen = false;
        }

        private async void UndoButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var result = await ViewModel.RequestUndoAsync();
            if (result == null)
                return;

            FeedbackBar.Severity = result.Success
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            FeedbackBar.Message = result.Message;
            FeedbackBar.IsOpen = true;
        }
    }
}
