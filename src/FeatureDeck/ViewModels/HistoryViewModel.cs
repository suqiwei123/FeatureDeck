using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Albacore.ViVe.NativeEnums;
using FeatureDeck.Models;
using FeatureDeck.Services;
using Microsoft.UI.Dispatching;

namespace FeatureDeck.ViewModels
{
    /// <summary>历史详情里一条特性变更的展示包装。</summary>
    public class HistoryChangeItem
    {
        private readonly HistoryRecord _record;
        private readonly HistoryChange _change;

        public HistoryChangeItem(HistoryRecord record, HistoryChange change)
        {
            _record = record;
            _change = change;
        }

        public string TitleText => AppResources.Format("HistoryEntryFormat",
            _change.FeatureId,
            string.IsNullOrEmpty(_change.Name)
                ? AppResources.Format("UnnamedFeatureFormat", _change.FeatureId)
                : _change.Name);

        public string SubtitleText => AppResources.Format("HistoryPriorityFormat",
            $"{(RTL_FEATURE_CONFIGURATION_PRIORITY)_change.Priority} ({_change.Priority})");

        public string RuntimeText => FormatStore("StoreRuntime", _record.AppliedRuntime, _change.RuntimeBefore);
        public string BootText => FormatStore("StoreBoot", _record.AppliedBoot, _change.BootBefore);

        public bool HasRuntime => RuntimeText != null;
        public bool HasBoot => BootText != null;

        private string FormatStore(string storeKey, bool applied, LayerSnapshot before)
        {
            // 只展示确实写入过的存储；当时没写成功的存储没有"之后"可言
            if (!applied || before == null)
                return null;

            var from = before.Existed
                ? StateText(before.State)
                : AppResources.Get("HistoryUnconfigured");

            return AppResources.Format("HistoryStoreChangeFormat",
                AppResources.Get(storeKey), from, StateText(_change.TargetState));
        }

        private static string StateText(uint state) => (RTL_FEATURE_ENABLED_STATE)state switch
        {
            RTL_FEATURE_ENABLED_STATE.Enabled => AppResources.Get("StateEnabled"),
            RTL_FEATURE_ENABLED_STATE.Disabled => AppResources.Get("StateDisabled"),
            _ => AppResources.Get("StateDefault")
        };
    }

    /// <summary>历史列表中一条记录的展示包装。</summary>
    public class HistoryRecordItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public HistoryRecordItem(HistoryRecord record)
        {
            Record = record;
            Changes = new ObservableCollection<HistoryChangeItem>(
                record.Changes.Select(change => new HistoryChangeItem(record, change)));
        }

        public HistoryRecord Record { get; }

        public ObservableCollection<HistoryChangeItem> Changes { get; }

        public string ActionText => AppResources.Get(ActionKey(Record.Action));

        public string TimeText => Record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

        public string StoreText => DescribeStore();

        public string SummaryText => AppResources.Format(
            "HistoryRecordSummaryFormat", Changes.Count, StoreText);

        public string HeaderText => AppResources.Format(
            "HistoryDetailTitleFormat", ActionText, StoreText);

        public string MetaText => AppResources.Format(
            "HistoryDetailMetaFormat", TimeText, Changes.Count);

        public string ResultText => Record.Undone
            ? AppResources.Format("HistoryUndoneFormat",
                Record.UndoneAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty)
            : AppResources.Format("HistoryDetailResultFormat", Record.Message);

        public bool IsFailure => !Record.Success;

        public bool IsUndone => Record.Undone;

        public bool CanUndo => !Record.Undone;

        public string UndoneText => AppResources.Get("HistoryUndoneBadge");

        /// <summary>撤销后刷新徽标与按钮状态。x:Bind 不响应空属性名，须逐个通知。</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(IsUndone));
            OnPropertyChanged(nameof(IsFailure));
            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(UndoneText));
        }

        private string DescribeStore()
        {
            if (Record.AppliedRuntime && Record.AppliedBoot)
                return AppResources.Get("StoreBoth");
            if (Record.AppliedBoot)
                return AppResources.Get("StoreBoot");
            return AppResources.Get("StoreRuntime");
        }

        private static string ActionKey(HistoryAction action) => action switch
        {
            HistoryAction.Enable => "ActionEnable",
            HistoryAction.Disable => "ActionDisable",
            HistoryAction.Reset => "ActionReset",
            _ => "ActionFullReset"
        };

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>历史对话框的视图模型：记录列表、选中项与两段式撤销确认。</summary>
    public class HistoryViewModel : INotifyPropertyChanged
    {
        private const int ConfirmWindowSeconds = 5;

        private readonly ObservableCollection<HistoryRecordItem> _records = new();
        private HistoryRecordItem _selected;
        private bool _isBusy;
        private bool _armed;
        private DispatcherQueueTimer _armTimer;

        public event PropertyChangedEventHandler PropertyChanged;

        public HistoryViewModel()
        {
            foreach (var record in HistoryService.Records)
                _records.Add(new HistoryRecordItem(record));
        }

        public ObservableCollection<HistoryRecordItem> Records => _records;

        public bool IsEmpty => _records.Count == 0;

        /// <summary>本次打开对话框期间是否成功撤销过，决定关闭后是否刷新主列表。</summary>
        public bool AnyUndone { get; private set; }

        public HistoryRecordItem Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value))
                    return;
                _selected = value;
                Disarm();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(UndoButtonText));
                OnPropertyChanged(nameof(SelectedChanges));
                OnPropertyChanged(nameof(DetailHeaderText));
                OnPropertyChanged(nameof(DetailMetaText));
                OnPropertyChanged(nameof(DetailResultText));
            }
        }

        public bool HasSelection => _selected != null;

        // 详情区绑定全部走单步属性：x:Bind 的多级路径在中间对象为 null 时容易踩坑
        public ObservableCollection<HistoryChangeItem> SelectedChanges => _selected?.Changes;

        public string DetailHeaderText => _selected?.HeaderText ?? string.Empty;

        public string DetailMetaText => _selected?.MetaText ?? string.Empty;

        public string DetailResultText => _selected?.ResultText ?? string.Empty;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanUndo));
            }
        }

        public bool CanUndo => !_isBusy && _selected != null && _selected.CanUndo;

        public string UndoButtonText =>
            AppResources.Get(_armed ? "HistoryUndoConfirmButton" : "HistoryUndoButton");

        /// <summary>
        /// 点击撤销：第一次进入确认态（5 秒超时），再次点击才执行。
        /// 返回 null 表示本次点击没有真正执行撤销。
        /// </summary>
        public async Task<OperationResult> RequestUndoAsync()
        {
            var target = Selected;
            if (target == null || IsBusy)
            {
                Disarm();
                return null;
            }

            if (!target.CanUndo)
            {
                Disarm();
                return OperationResult.Fail(AppResources.Get("HistoryAlreadyUndone"));
            }

            if (!_armed)
            {
                Arm();
                return null;
            }

            Disarm();
            IsBusy = true;

            try
            {
                var record = target.Record;
                var result = await Task.Run(() => HistoryService.Undo(record));

                target.Refresh();
                if (record.Undone)
                    AnyUndone = true;

                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(UndoButtonText));
                OnPropertyChanged(nameof(DetailResultText));
                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Arm()
        {
            _armed = true;
            OnPropertyChanged(nameof(UndoButtonText));

            if (_armTimer == null)
            {
                var queue = DispatcherQueue.GetForCurrentThread();
                if (queue == null)
                    return;

                _armTimer = queue.CreateTimer();
                _armTimer.IsRepeating = false;
                _armTimer.Tick += (_, _) => Disarm();
            }

            _armTimer.Stop();
            _armTimer.Interval = TimeSpan.FromSeconds(ConfirmWindowSeconds);
            _armTimer.Start();
        }

        private void Disarm()
        {
            _armTimer?.Stop();
            if (!_armed)
                return;

            _armed = false;
            OnPropertyChanged(nameof(UndoButtonText));
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
