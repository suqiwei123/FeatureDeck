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
using FeatureDeck.Models;
using FeatureDeck.Services;

namespace FeatureDeck.ViewModels
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
        private string _summary = string.Empty;
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
            set
            {
                _selectedCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCountText));
            }
        }

        public string SelectedCountText =>
            $"{AppResources.Get("SelectedPrefix")} {_selectedCount} {AppResources.Get("SelectedSuffix")}";

        public string BuildVersionText =>
            $"{AppResources.Get("BuildVersionPrefix")} {BuildNumber}";

        public bool BootUnavailable
        {
            get => _bootUnavailable;
            set { _bootUnavailable = value; OnPropertyChanged(); }
        }

        public int TotalCount => _allItems.Count;

        public bool IsBuildSupported => FeatureService.IsBuildSupported();

        public int BuildNumber => FeatureService.CurrentBuild;

        public bool HasSelection => SelectedItems.Count > 0;

        /// <summary>语言切换按钮的显示文本（跟随系统时显示当前语言名）。</summary>
        public string LanguageLabel =>
            string.IsNullOrEmpty(AppResources.UserOverrideLanguage)
                ? AppResources.Get("LangFollowSystem")
                : (AppResources.CurrentLanguage == AppResources.DefaultLanguage
                    ? AppResources.Get("LangChinese")
                    : AppResources.Get("LangEnglish"));

        public string LanguageToolTip => AppResources.Get("LangButton.ToolTipService.ToolTip");

        public async Task InitializeAsync()
        {
            Summary = AppResources.Get("Ready");

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
                Summary += AppResources.Get("NoDictionarySuffix");
        }

        public async Task RefreshAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            Summary = AppResources.Get("Loading");

            try
            {
                var result = await Task.Run(() => FeatureService.LoadAll());

                _allItems.Clear();
                _allItems.AddRange(result.Items);
                BootUnavailable = result.BootStoreUnavailable;

                ApplyFilter();

                var named = _allItems.Count(x => x.HasName);
                var percent = 100.0 * named / Math.Max(1, _allItems.Count);
                Summary = result.Error ??
                    AppResources.Format("SummaryFormat",
                        _allItems.Count, named, $"{percent:F1}%", FeatureNaming.LoadedCount, BuildNumber);

                if (result.Error != null)
                    await ShowMessageAsync(AppResources.Get("ReadFailedTitle"), result.Error);
                else if (result.BootStoreUnavailable)
                    Summary += AppResources.Get("BootUnavailableSuffix");
            }
            catch (Exception ex)
            {
                // 换设备/环境时底层 ntdll 行为可能不同，任何未预期异常都降级为友好提示而非崩溃
                _allItems.Clear();
                ApplyFilter();
                Summary = AppResources.Format("ReadErrorPrefixFormat", ex.Message);
                await ShowMessageAsync(AppResources.Get("ReadFailedTitle"),
                    AppResources.Format("ReadFailedMessageFormat", ex.Message));
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
            Summary = AppResources.Format("SummaryFilterFormat", _allItems.Count, Filtered.Count);
            OnPropertyChanged(nameof(IsEmpty));
        }

        public void UpdateSelection(IList<object> selectedItems)
        {
            SelectedItems = selectedItems?.OfType<FeatureItem>().ToList() ?? new List<FeatureItem>();
            SelectedCount = SelectedItems.Count;
            OnPropertyChanged(nameof(HasSelection));
        }

        public async Task EnableSelectedAsync()
            => await ApplyStateAsync(RTL_FEATURE_ENABLED_STATE.Enabled, AppResources.Get("ActionEnable"), confirm: true);

        public async Task DisableSelectedAsync()
            => await ApplyStateAsync(RTL_FEATURE_ENABLED_STATE.Disabled, AppResources.Get("ActionDisable"), confirm: true);

        // 行内按钮已经表达了明确意图，不再弹确认框，失败时才提示
        public Task EnableSingleAsync(FeatureItem item)
            => ApplySingleAsync(item, RTL_FEATURE_ENABLED_STATE.Enabled, AppResources.Get("ActionEnable"));

        public Task DisableSingleAsync(FeatureItem item)
            => ApplySingleAsync(item, RTL_FEATURE_ENABLED_STATE.Disabled, AppResources.Get("ActionDisable"));

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
                await ShowMessageAsync(AppResources.Get("CannotOperateTitle"), AppResources.Get("CannotModifyMessage"));
                return;
            }

            var skipped = SelectedItems.Count - targets.Count;
            var storeText = DescribeStore();

            if (confirm)
            {
                var body = state == RTL_FEATURE_ENABLED_STATE.Enabled
                    ? AppResources.Format("ConfirmBodyEnableFormat", targets.Count, storeText)
                    : AppResources.Format("ConfirmBodyDisableFormat", targets.Count, storeText);

                var extra = string.Empty;
                if (skipped > 0)
                    extra += "\n" + AppResources.Format("SkippedSuffixFormat", skipped);
                if (_store != StoreTarget.Runtime)
                    extra += "\n\n" + AppResources.Get("BootRebootNotice");

                var confirmed = await ConfirmAsync(
                    AppResources.Format("ConfirmTitleFormat", actionName),
                    body + extra);

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
                await ShowMessageAsync(AppResources.Get("CannotOperateTitle"),
                    AppResources.Format("CannotModifyMessage"));
                return;
            }

            if (confirm)
            {
                var confirmed = await ConfirmAsync(
                    AppResources.Format("ConfirmTitleFormat", AppResources.Get("ActionReset")),
                    AppResources.Format("ConfirmBodyResetFormat", targets.Count,
                        AppResources.Format("TargetStoreFormat", DescribeStore())));

                if (!confirmed) return;
            }

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => FeatureService.Reset(
                    targets,
                    _store is StoreTarget.Runtime or StoreTarget.Both,
                    _store is StoreTarget.Boot or StoreTarget.Both));

                await ShowResultAsync(result, AppResources.Get("ActionReset"), dialog: confirm);
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
                AppResources.Get("DangerTitle"),
                AppResources.Format("FullResetBodyFormat",
                    AppResources.Format("TargetStoreFormat", DescribeStore()))
                    + "\n\n" + AppResources.Get("FullResetIrreversible"),
                primaryText: AppResources.Get("ActionFullReset"));

            if (!confirmed) return;

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => FeatureService.FullReset(
                    _store is StoreTarget.Runtime or StoreTarget.Both,
                    _store is StoreTarget.Boot or StoreTarget.Both));

                await ShowResultAsync(result, AppResources.Get("ActionFullReset"));
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
                AppResources.Get("FixLkgTitle"),
                performed ? AppResources.Get("FixLkgPerformed") : AppResources.Get("FixLkgNoDamage"));
        }

        private string DescribeStore() => _store switch
        {
            StoreTarget.Runtime => AppResources.Get("StoreRuntimeText"),
            StoreTarget.Boot => AppResources.Get("StoreBootText"),
            _ => AppResources.Get("StoreBothText")
        };

        private async Task ShowResultAsync(OperationResult result, string actionName, bool dialog = true)
        {
            if (result == null) return;

            Summary = result.Message;

            if (result.Success && result.NeedsReboot)
                Summary += AppResources.Get("RebootPendingSuffix");

            // 单行操作默认静默，只有出错才打断用户
            if (dialog || !result.Success)
            {
                var title = result.Success
                    ? AppResources.Format("OperationDoneTitleFormat", actionName)
                    : AppResources.Format("OperationFailTitleFormat", actionName);
                var content = result.Message;
                if (result.Success && result.NeedsReboot)
                    content += "\n\n" + AppResources.Get("BootPendingNotice");
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
                CloseButtonText = AppResources.Get("OK"),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task<bool> ConfirmAsync(string title, string content, string primaryText = null)
        {
            if (XamlRoot == null) return false;
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText ?? AppResources.Get("OK"),
                CloseButtonText = AppResources.Get("Cancel"),
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
