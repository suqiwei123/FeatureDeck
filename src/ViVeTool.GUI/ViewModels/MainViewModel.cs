using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Albacore.ViVe;
using Albacore.ViVe.NativeEnums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ViVeTool.GUI.Models;
using ViVeTool.GUI.Services;

namespace ViVeTool.GUI.ViewModels
{
    public enum StoreTarget
    {
        Runtime = 0,
        Boot = 1,
        Both = 2
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly List<FeatureItem> _allItems = new();
        private List<FeatureItem> _filtered = new();
        private string _searchText = string.Empty;
        private int _filterIndex;
        private StoreTarget _store = StoreTarget.Runtime;
        private bool _isBusy;
        private string _summary = "准备就绪";
        private int _selectedCount;
        private bool _bootUnavailable;

        public event PropertyChangedEventHandler PropertyChanged;

        public XamlRoot XamlRoot { get; set; }

        public List<FeatureItem> Filtered
        {
            get => _filtered;
            private set { _filtered = value; OnPropertyChanged(); }
        }

        public List<FeatureItem> SelectedItems { get; set; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public int FilterIndex
        {
            get => _filterIndex;
            set
            {
                if (_filterIndex == value) return;
                _filterIndex = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public StoreTarget Store
        {
            get => _store;
            set { _store = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIdle));
            }
        }

        public bool IsIdle => !_isBusy;

        public bool IsEmpty => _filtered.Count == 0;

        public string Summary
        {
            get => _summary;
            set { _summary = value; OnPropertyChanged(); }
        }

        public int SelectedCount
        {
            get => _selectedCount;
            set { _selectedCount = value; OnPropertyChanged(); }
        }

        public bool BootUnavailable
        {
            get => _bootUnavailable;
            set { _bootUnavailable = value; OnPropertyChanged(); }
        }

        public int TotalCount => _allItems.Count;

        public bool IsBuildSupported => FeatureService.IsBuildSupported();

        public int BuildNumber => FeatureService.CurrentBuild;

        public bool HasSelection => SelectedItems.Count > 0;

        public async Task InitializeAsync()
        {
            int dictCount = 0;
            try
            {
                dictCount = await Task.Run(() => FeatureNaming.Load());
            }
            catch
            {
                dictCount = 0;
            }

            await RefreshAsync();

            if (dictCount == 0)
                Summary += "　（未找到名称字典，仅显示 ID）";
        }

        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            Summary = "正在读取特性配置…";

            try
            {
                var result = await Task.Run(() => FeatureService.LoadAll());

                _allItems.Clear();
                _allItems.AddRange(result.Items);
                BootUnavailable = result.BootStoreUnavailable;

                ApplyFilter();

                var named = _allItems.Count(x => x.HasName);
                Summary = result.Error ??
                    $"共 {_allItems.Count} 条，已命名 {named} 条（{100.0 * named / Math.Max(1, _allItems.Count):F1}%）　·　字典 {FeatureNaming.LoadedCount} 条　·　内部版本 {BuildNumber}";

                if (result.Error != null)
                    await ShowMessageAsync("读取失败", result.Error);
                else if (result.BootStoreUnavailable)
                    Summary += "　·　启动存储暂不可用";
            }
            catch (Exception ex)
            {
                // 换设备/环境时底层 ntdll 行为可能不同，任何未预期异常都降级为友好提示而非崩溃
                _allItems.Clear();
                ApplyFilter();
                Summary = $"读取特性配置时发生错误：{ex.Message}";
                await ShowMessageAsync("读取失败", $"读取本机特性配置时出现异常：\n{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ApplyFilter()
        {
            IEnumerable<FeatureItem> query = _allItems;

            switch (_store)
            {
                case StoreTarget.Runtime:
                    query = query.Where(x => x.ExistsInRuntime);
                    break;
                case StoreTarget.Boot:
                    query = query.Where(x => x.ExistsInBoot);
                    break;
            }

            switch (_filterIndex)
            {
                case 1:
                    query = query.Where(x => x.IsUserModified);
                    break;
                case 2:
                    query = query.Where(x => x.CanEdit);
                    break;
                case 3:
                    query = query.Where(x => x.IsWexpConfiguration);
                    break;
                case 4:
                    query = query.Where(x => x.HasSubscriptions);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var key = _searchText.Trim();
                query = query.Where(x =>
                    x.SearchableText.Contains(key, StringComparison.OrdinalIgnoreCase));
            }

            Filtered = query.ToList();
            Summary = $"共 {_allItems.Count} 条，当前显示 {Filtered.Count} 条";
            OnPropertyChanged(nameof(IsEmpty));
        }

        public void UpdateSelection(IList<object> selectedItems)
        {
            SelectedItems = selectedItems?.OfType<FeatureItem>().ToList() ?? new List<FeatureItem>();
            SelectedCount = SelectedItems.Count;
            OnPropertyChanged(nameof(HasSelection));
        }

        public async Task EnableSelectedAsync()
            => await ApplyStateAsync(RTL_FEATURE_ENABLED_STATE.Enabled, "启用", confirm: true);

        public async Task DisableSelectedAsync()
            => await ApplyStateAsync(RTL_FEATURE_ENABLED_STATE.Disabled, "禁用", confirm: true);

        // 行内按钮已经表达了明确意图，不再弹确认框，失败时才提示
        public Task EnableSingleAsync(FeatureItem item)
            => ApplySingleAsync(item, RTL_FEATURE_ENABLED_STATE.Enabled, "启用");

        public Task DisableSingleAsync(FeatureItem item)
            => ApplySingleAsync(item, RTL_FEATURE_ENABLED_STATE.Disabled, "禁用");

        public Task ResetSingleAsync(FeatureItem item)
        {
            if (item == null) return Task.CompletedTask;
            SelectSingle(item);
            return ResetSelectedAsync(confirm: false);
        }

        private void SelectSingle(FeatureItem item)
        {
            SelectedItems = new List<FeatureItem> { item };
            SelectedCount = 1;
            OnPropertyChanged(nameof(HasSelection));
        }

        private Task ApplySingleAsync(FeatureItem item, RTL_FEATURE_ENABLED_STATE state, string actionName)
        {
            if (item == null) return Task.CompletedTask;
            SelectSingle(item);
            return ApplyStateAsync(state, actionName, confirm: false);
        }

        private async Task ApplyStateAsync(RTL_FEATURE_ENABLED_STATE state, string actionName, bool confirm)
        {
            var targets = SelectedItems.Where(x => x.CanEdit).ToList();
            if (targets.Count == 0)
            {
                await ShowMessageAsync("无法操作", "选中的条目由系统镜像管理，不允许修改。");
                return;
            }

            var skipped = SelectedItems.Count - targets.Count;
            var storeText = DescribeStore();

            if (confirm)
            {
                var confirmed = await ConfirmAsync(
                    $"确认{actionName}",
                    $"将对 {targets.Count} 条特性执行「{actionName}」，目标存储：{storeText}。"
                    + (skipped > 0 ? $"\n另有 {skipped} 条为系统受保护条目，将被跳过。" : string.Empty)
                    + (_store != StoreTarget.Runtime ? "\n\n写入启动存储后需要重启系统才会生效。" : string.Empty));

                if (!confirmed) return;
            }

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => FeatureService.SetState(
                    targets, state,
                    _store is StoreTarget.Runtime or StoreTarget.Both,
                    _store is StoreTarget.Boot or StoreTarget.Both));

                await ShowResultAsync(result, actionName, dialog: confirm);
                await RefreshAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ResetSelectedAsync(bool confirm = true)
        {
            var targets = SelectedItems.Where(x => x.CanEdit).ToList();
            if (targets.Count == 0)
            {
                await ShowMessageAsync("无法操作", "选中的条目由系统镜像管理，不允许还原。");
                return;
            }

            if (confirm)
            {
                var confirmed = await ConfirmAsync(
                    "确认还原",
                    $"将清除 {targets.Count} 条特性的用户覆盖，恢复系统默认状态。\n目标存储：{DescribeStore()}");

                if (!confirmed) return;
            }

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => FeatureService.Reset(
                    targets,
                    _store is StoreTarget.Runtime or StoreTarget.Both,
                    _store is StoreTarget.Boot or StoreTarget.Both));

                await ShowResultAsync(result, "还原", dialog: confirm);
                await RefreshAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task FullResetAsync()
        {
            var confirmed = await ConfirmAsync(
                "危险操作",
                "这将清除所有非系统保护的用户覆盖，把本机全部特性恢复为系统默认状态。\n"
                + $"目标存储：{DescribeStore()}\n\n此操作不可撤销，确定继续吗？",
                primaryText: "全部还原");

            if (!confirmed) return;

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => FeatureService.FullReset(
                    _store is StoreTarget.Runtime or StoreTarget.Both,
                    _store is StoreTarget.Boot or StoreTarget.Both));

                await ShowResultAsync(result, "全部还原");
                await RefreshAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task FixLkgAsync()
        {
            var performed = await Task.Run(() => FeatureManager.FixLKGStore());
            await ShowMessageAsync(
                "修复上次正确配置存储",
                performed ? "已修复损坏的存储头。" : "未发现损坏，无需修复。");
        }

        private string DescribeStore() => _store switch
        {
            StoreTarget.Runtime => "运行时存储（立即生效）",
            StoreTarget.Boot => "启动存储（重启后生效）",
            _ => "运行时 + 启动存储"
        };

        private async Task ShowResultAsync(OperationResult result, string actionName, bool dialog = true)
        {
            if (result == null) return;

            Summary = result.Message;

            if (result.Success && result.NeedsReboot)
                Summary += "　·　重启后生效";

            // 单行操作默认静默，只有出错才打断用户
            if (dialog || !result.Success)
            {
                var title = result.Success ? $"{actionName}完成" : $"{actionName}失败";
                var content = result.Message;
                if (result.Success && result.NeedsReboot)
                    content += "\n\n已写入启动存储，重启系统后生效。";
                await ShowMessageAsync(title, content);
            }
        }

        private async Task ShowMessageAsync(string title, string content)
        {
            if (XamlRoot == null) return;
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task<bool> ConfirmAsync(string title, string content, string primaryText = "确定")
        {
            if (XamlRoot == null) return false;
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
