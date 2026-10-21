using Albacore.ViVe;
using Albacore.ViVe.NativeEnums;
using Albacore.ViVe.NativeStructs;
using System;
using System.Collections.Generic;
using System.Linq;
using FeatureDeck.Models;

namespace FeatureDeck.Services
{
    public class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool NeedsReboot { get; set; }

        /// <summary>本次调用确实写入过的存储，用于决定撤销要覆盖哪些存储。</summary>
        public bool AppliedRuntime { get; set; }
        public bool AppliedBoot { get; set; }

        public static OperationResult Ok(string message, bool needsReboot = false)
            => new() { Success = true, Message = message, NeedsReboot = needsReboot };

        public static OperationResult Fail(string message)
            => new() { Success = false, Message = message, NeedsReboot = false };
    }

    public class LoadResult
    {
        public List<FeatureItem> Items { get; set; } = new();
        public string Error { get; set; }
        public bool BootStoreUnavailable { get; set; }
    }

    public static class FeatureService
    {
        private const uint StatusObjectNameNotFound = 0xC0000034;

        public static LoadResult LoadAll()
        {
            var result = new LoadResult();

            RTL_FEATURE_CONFIGURATION[] runtimeConfigs = null;
            RTL_FEATURE_CONFIGURATION[] bootConfigs = null;
            int runtimeError = 0;
            int bootError = 0;

            try
            {
                runtimeConfigs = FeatureManager.QueryAllFeatureConfigurations(
                    RTL_FEATURE_CONFIGURATION_TYPE.Runtime);
            }
            catch (Exception ex)
            {
                runtimeError = ex.HResult;
            }

            try
            {
                bootConfigs = FeatureManager.QueryAllFeatureConfigurations(
                    RTL_FEATURE_CONFIGURATION_TYPE.Boot);
            }
            catch (Exception ex)
            {
                bootError = ex.HResult;
            }

            if (runtimeConfigs == null)
            {
                result.Error = runtimeError != 0
                    ? AppResources.Format("ReadRuntimeFailFormat", NtStatus.Describe(runtimeError))
                    : AppResources.Get("ReadUnknownFail");
                return result;
            }

            if (bootConfigs == null)
                result.BootStoreUnavailable = bootError != 0;

            var map = new Dictionary<(uint, uint), FeatureItem>();

            foreach (var cfg in runtimeConfigs)
            {
                var key = (cfg.FeatureId, (uint)cfg.Priority);
                var item = new FeatureItem
                {
                    FeatureId = cfg.FeatureId,
                    Priority = cfg.Priority,
                    IsImmutable = FeatureManager.IsPriorityImmutable(cfg.Priority),
                    IsWexpConfiguration = cfg.IsWexpConfiguration,
                    HasSubscriptions = cfg.HasSubscriptions,
                    Variant = cfg.Variant,
                    VariantPayloadKind = cfg.VariantPayloadKind,
                    VariantPayload = cfg.VariantPayload,
                    ExistsInRuntime = true,
                    RuntimeState = cfg.EnabledState
                };
                map[key] = item;
            }

            if (bootConfigs != null)
            {
                foreach (var cfg in bootConfigs)
                {
                    var key = (cfg.FeatureId, (uint)cfg.Priority);
                    if (map.TryGetValue(key, out var item))
                    {
                        item.ExistsInBoot = true;
                        item.BootState = cfg.EnabledState;
                    }
                    else
                    {
                        map[key] = new FeatureItem
                        {
                            FeatureId = cfg.FeatureId,
                            Priority = cfg.Priority,
                            IsImmutable = FeatureManager.IsPriorityImmutable(cfg.Priority),
                            IsWexpConfiguration = cfg.IsWexpConfiguration,
                            HasSubscriptions = cfg.HasSubscriptions,
                            Variant = cfg.Variant,
                            VariantPayloadKind = cfg.VariantPayloadKind,
                            VariantPayload = cfg.VariantPayload,
                            ExistsInBoot = true,
                            BootState = cfg.EnabledState
                        };
                    }
                }
            }

            foreach (var item in map.Values)
            {
                item.Name = FeatureNaming.GetName(item.FeatureId);
                var categoryKey = FeatureCategory.Classify(item.Name);
                item.Category = categoryKey != null ? AppResources.Get(categoryKey) : null;
            }

            result.Items = map.Values
                .OrderBy(x => x.FeatureId)
                .ThenBy(x => (uint)x.Priority)
                .ToList();

            return result;
        }

        /// <summary>
        /// 按单个 ID 现查配置（快捷操作用）。先查 Runtime 再查 Boot，
        /// 两个存储都没有返回 null。字段映射与 LoadAll 一致，保证后续写入携带
        /// Wexp 标志与 Variant 信息。
        /// </summary>
        public static FeatureItem LoadById(uint featureId)
        {
            Albacore.ViVe.NativeStructs.RTL_FEATURE_CONFIGURATION? runtime = null;
            Albacore.ViVe.NativeStructs.RTL_FEATURE_CONFIGURATION? boot = null;
            try
            {
                runtime = FeatureManager.QueryFeatureConfiguration(
                    featureId, RTL_FEATURE_CONFIGURATION_TYPE.Runtime);
                boot = FeatureManager.QueryFeatureConfiguration(
                    featureId, RTL_FEATURE_CONFIGURATION_TYPE.Boot);
            }
            catch
            {
                return null;
            }

            var src = runtime ?? boot;
            if (src == null)
                return null;

            var cfg = src.Value;
            var item = new FeatureItem
            {
                FeatureId = cfg.FeatureId,
                Priority = cfg.Priority,
                IsImmutable = FeatureManager.IsPriorityImmutable(cfg.Priority),
                IsWexpConfiguration = cfg.IsWexpConfiguration,
                HasSubscriptions = cfg.HasSubscriptions,
                Variant = cfg.Variant,
                VariantPayloadKind = cfg.VariantPayloadKind,
                VariantPayload = cfg.VariantPayload,
                ExistsInRuntime = runtime != null,
                RuntimeState = runtime?.EnabledState ?? default,
                ExistsInBoot = boot != null,
                BootState = boot?.EnabledState ?? default
            };

            item.Name = FeatureNaming.GetName(item.FeatureId);
            var categoryKey = FeatureCategory.Classify(item.Name);
            item.Category = categoryKey != null ? AppResources.Get(categoryKey) : null;

            return item;
        }

        public static OperationResult SetState(
            IEnumerable<FeatureItem> items,
            RTL_FEATURE_ENABLED_STATE state,
            bool writeRuntime,
            bool writeBoot,
            List<HistoryChange> changes = null)
        {
            // 同一特性可能在列表里按不同优先级出现多行，写入内容完全相同，去重后再写
            var targets = items == null
                ? new List<FeatureItem>()
                : items.GroupBy(x => x.FeatureId).Select(g => g.First()).ToList();

            var updates = targets.Select(item => new RTL_FEATURE_CONFIGURATION_UPDATE
            {
                FeatureId = item.FeatureId,
                EnabledState = state,
                EnabledStateOptions = item.IsWexpConfiguration
                    ? RTL_FEATURE_ENABLED_STATE_OPTIONS.WexpConfig
                    : RTL_FEATURE_ENABLED_STATE_OPTIONS.None,
                Priority = RTL_FEATURE_CONFIGURATION_PRIORITY.User,
                Variant = item.Variant,
                VariantPayloadKind = item.VariantPayloadKind,
                VariantPayload = item.VariantPayload,
                Operation = RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState
                          | RTL_FEATURE_CONFIGURATION_OPERATION.VariantState
            }).ToArray();

            CaptureHistory(changes,
                targets.Select(item => (item.FeatureId, item.Name, item.IsWexpConfiguration,
                    (uint)RTL_FEATURE_CONFIGURATION_PRIORITY.User, (uint)state)),
                writeRuntime, writeBoot);

            return Apply(updates, writeRuntime, writeBoot,
                state == RTL_FEATURE_ENABLED_STATE.Enabled
                    ? AppResources.Get("ActionEnable")
                    : AppResources.Get("ActionDisable"));
        }

        public static OperationResult Reset(
            IEnumerable<FeatureItem> items,
            bool writeRuntime,
            bool writeBoot,
            List<HistoryChange> changes = null)
        {
            var updates = new List<RTL_FEATURE_CONFIGURATION_UPDATE>();
            var targets = new List<(uint FeatureId, string Name, bool IsWexp, uint Priority, uint TargetState)>();
            var seen = new HashSet<(uint, uint)>();

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!seen.Add((item.FeatureId, (uint)item.Priority)))
                        continue;

                    updates.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
                    {
                        FeatureId = item.FeatureId,
                        Priority = item.Priority,
                        Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
                    });

                    targets.Add((item.FeatureId, item.Name, item.IsWexpConfiguration,
                        (uint)item.Priority, (uint)RTL_FEATURE_ENABLED_STATE.Default));
                }
            }

            CaptureHistory(changes, targets, writeRuntime, writeBoot);

            return Apply(updates.ToArray(), writeRuntime, writeBoot, AppResources.Get("ActionReset"));
        }

        public static OperationResult FullReset(
            bool writeRuntime,
            bool writeBoot,
            List<HistoryChange> changes = null)
        {
            var updates = new List<RTL_FEATURE_CONFIGURATION_UPDATE>();
            var map = new Dictionary<(uint, uint), HistoryChange>();

            if (writeRuntime)
                CollectResettables(RTL_FEATURE_CONFIGURATION_TYPE.Runtime, updates, map);
            if (writeBoot)
                CollectResettables(RTL_FEATURE_CONFIGURATION_TYPE.Boot, updates, map);

            if (changes != null)
            {
                changes.Clear();
                changes.AddRange(map.Values);
            }

            if (updates.Count == 0)
                return OperationResult.Ok(AppResources.Get("NoResettable"));

            return Apply(updates.ToArray(), writeRuntime, writeBoot, AppResources.Get("ActionFullReset"));
        }

        private static void CollectResettables(
            RTL_FEATURE_CONFIGURATION_TYPE type,
            List<RTL_FEATURE_CONFIGURATION_UPDATE> target,
            Dictionary<(uint, uint), HistoryChange> map)
        {
            var configs = FeatureManager.QueryAllFeatureConfigurations(type);
            if (configs == null)
                return;

            var isRuntime = type == RTL_FEATURE_CONFIGURATION_TYPE.Runtime;

            foreach (var cfg in configs)
            {
                if (FeatureManager.IsPriorityImmutable(cfg.Priority))
                    continue;

                var snapshot = new LayerSnapshot
                {
                    Existed = true,
                    State = (uint)cfg.EnabledState,
                    Variant = cfg.Variant,
                    VariantPayloadKind = (uint)cfg.VariantPayloadKind,
                    VariantPayload = cfg.VariantPayload
                };

                var key = (cfg.FeatureId, (uint)cfg.Priority);
                if (map.TryGetValue(key, out var existing))
                {
                    // 同一层在两个存储都有：补上另一侧的前状态，更新只需一条
                    if (isRuntime) existing.RuntimeBefore = snapshot;
                    else existing.BootBefore = snapshot;
                    continue;
                }

                target.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
                {
                    FeatureId = cfg.FeatureId,
                    Priority = cfg.Priority,
                    Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
                });

                map[key] = new HistoryChange
                {
                    FeatureId = cfg.FeatureId,
                    Name = FeatureNaming.GetName(cfg.FeatureId),
                    Priority = (uint)cfg.Priority,
                    IsWexp = cfg.IsWexpConfiguration,
                    TargetState = (uint)RTL_FEATURE_ENABLED_STATE.Default,
                    RuntimeBefore = isRuntime ? snapshot : null,
                    BootBefore = isRuntime ? null : snapshot
                };
            }
        }

        /// <summary>
        /// 按历史记录把特性写回操作前的状态：原本存在的优先级层回写原状态与变体，
        /// 由本次操作新建的覆盖层（撤销启用/禁用）则清除该层。
        /// 按 64 条分批写入，单批失败不影响其余批次，便于全部还原的撤销尽量恢复。
        /// </summary>
        public static OperationResult Restore(
            IReadOnlyList<HistoryChange> changes,
            bool writeRuntime,
            bool writeBoot)
        {
            var actionName = AppResources.Get("ActionUndo");

            var runtimeUpdates = BuildRestoreUpdates(changes, true);
            var bootUpdates = BuildRestoreUpdates(changes, false);

            if (runtimeUpdates.Count == 0 && bootUpdates.Count == 0)
                return OperationResult.Fail(AppResources.Get("HistoryNoChanges"));

            var runtime = new RestoreStats();
            var boot = new RestoreStats();

            if (writeRuntime)
                ApplyRestoreChunks(runtimeUpdates, RTL_FEATURE_CONFIGURATION_TYPE.Runtime, runtime);
            if (writeBoot)
                ApplyRestoreChunks(bootUpdates, RTL_FEATURE_CONFIGURATION_TYPE.Boot, boot);

            if (boot.Applied > 0)
            {
                try { EnsureBootPending(); }
                catch { /* 启动状态写入失败不影响已完成的撤销 */ }
            }

            var result = new OperationResult
            {
                AppliedRuntime = runtime.Applied > 0,
                AppliedBoot = boot.Applied > 0
            };

            int ok = runtime.Applied + boot.Applied;
            int bad = runtime.Failed + boot.Failed;
            string error = runtime.Error ?? boot.Error;

            if (ok == 0)
            {
                result.Message = AppResources.Format("FailGenericFormat", actionName,
                    error ?? AppResources.Get("UnknownError"));
                return result;
            }

            result.NeedsReboot = result.AppliedBoot;

            if (bad == 0)
            {
                result.Success = true;
                result.Message = AppResources.Format("SuccessCountFormat", actionName, ok);
            }
            else
            {
                result.Message = AppResources.Format("UndoPartialFormat", actionName, ok, bad,
                    error ?? AppResources.Get("UnknownError"));
            }

            return result;
        }

        private static List<RTL_FEATURE_CONFIGURATION_UPDATE> BuildRestoreUpdates(
            IReadOnlyList<HistoryChange> changes,
            bool isRuntime)
        {
            var updates = new List<RTL_FEATURE_CONFIGURATION_UPDATE>();
            if (changes == null)
                return updates;

            foreach (var change in changes)
            {
                if (change == null)
                    continue;

                var before = isRuntime ? change.RuntimeBefore : change.BootBefore;
                if (before == null)
                    continue; // 该存储当时未被写入，无需恢复

                var priority = (RTL_FEATURE_CONFIGURATION_PRIORITY)change.Priority;

                if (before.Existed)
                {
                    updates.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
                    {
                        FeatureId = change.FeatureId,
                        Priority = priority,
                        EnabledState = (RTL_FEATURE_ENABLED_STATE)before.State,
                        EnabledStateOptions = change.IsWexp
                            ? RTL_FEATURE_ENABLED_STATE_OPTIONS.WexpConfig
                            : RTL_FEATURE_ENABLED_STATE_OPTIONS.None,
                        Variant = before.Variant,
                        VariantPayloadKind = (RTL_FEATURE_VARIANT_PAYLOAD_KIND)before.VariantPayloadKind,
                        VariantPayload = before.VariantPayload,
                        Operation = RTL_FEATURE_CONFIGURATION_OPERATION.FeatureState
                                  | RTL_FEATURE_CONFIGURATION_OPERATION.VariantState
                    });
                }
                else if (change.TargetState != (uint)RTL_FEATURE_ENABLED_STATE.Default)
                {
                    // 覆盖层由本次操作新建：撤销即删除这一层
                    updates.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
                    {
                        FeatureId = change.FeatureId,
                        Priority = priority,
                        Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
                    });
                }
            }

            return updates;
        }

        private sealed class RestoreStats
        {
            public int Applied;
            public int Failed;
            public string Error;
        }

        private static void ApplyRestoreChunks(
            List<RTL_FEATURE_CONFIGURATION_UPDATE> updates,
            RTL_FEATURE_CONFIGURATION_TYPE type,
            RestoreStats stats)
        {
            const int ChunkSize = 64;

            for (int offset = 0; offset < updates.Count; offset += ChunkSize)
            {
                var chunk = updates
                    .GetRange(offset, Math.Min(ChunkSize, updates.Count - offset))
                    .ToArray();

                int rc;
                try
                {
                    rc = FeatureManager.SetFeatureConfigurations(chunk, type);
                }
                catch (Exception ex)
                {
                    stats.Failed += chunk.Length;
                    stats.Error ??= ex.Message;
                    continue;
                }

                if (rc == 0)
                    stats.Applied += chunk.Length;
                else
                {
                    stats.Failed += chunk.Length;
                    stats.Error ??= NtStatus.Describe(rc);
                }
            }
        }

        /// <summary>写入前捕获各存储的 (FeatureId, Priority) 层快照。</summary>
        private static Dictionary<(uint, uint), RTL_FEATURE_CONFIGURATION> QueryLayers(
            RTL_FEATURE_CONFIGURATION_TYPE type)
        {
            var map = new Dictionary<(uint, uint), RTL_FEATURE_CONFIGURATION>();
            try
            {
                var configs = FeatureManager.QueryAllFeatureConfigurations(type);
                if (configs != null)
                {
                    foreach (var cfg in configs)
                        map[(cfg.FeatureId, (uint)cfg.Priority)] = cfg;
                }
            }
            catch
            {
                // 查询失败按"该层不存在"处理，撤销时退化为清除覆盖层
            }
            return map;
        }

        private static LayerSnapshot Snapshot(
            Dictionary<(uint, uint), RTL_FEATURE_CONFIGURATION> layers,
            uint featureId,
            RTL_FEATURE_CONFIGURATION_PRIORITY priority)
        {
            if (layers == null)
                return null;

            if (layers.TryGetValue((featureId, (uint)priority), out var cfg))
            {
                return new LayerSnapshot
                {
                    Existed = true,
                    State = (uint)cfg.EnabledState,
                    Variant = cfg.Variant,
                    VariantPayloadKind = (uint)cfg.VariantPayloadKind,
                    VariantPayload = cfg.VariantPayload
                };
            }

            return new LayerSnapshot { Existed = false };
        }

        private static void CaptureHistory(
            List<HistoryChange> changes,
            IEnumerable<(uint FeatureId, string Name, bool IsWexp, uint Priority, uint TargetState)> targets,
            bool writeRuntime,
            bool writeBoot)
        {
            if (changes == null)
                return;

            changes.Clear();

            var runtimeLayers = writeRuntime ? QueryLayers(RTL_FEATURE_CONFIGURATION_TYPE.Runtime) : null;
            var bootLayers = writeBoot ? QueryLayers(RTL_FEATURE_CONFIGURATION_TYPE.Boot) : null;

            foreach (var target in targets)
            {
                var priority = (RTL_FEATURE_CONFIGURATION_PRIORITY)target.Priority;
                changes.Add(new HistoryChange
                {
                    FeatureId = target.FeatureId,
                    Name = target.Name,
                    Priority = target.Priority,
                    IsWexp = target.IsWexp,
                    TargetState = target.TargetState,
                    RuntimeBefore = Snapshot(runtimeLayers, target.FeatureId, priority),
                    BootBefore = Snapshot(bootLayers, target.FeatureId, priority)
                });
            }
        }

        private static OperationResult Apply(
            RTL_FEATURE_CONFIGURATION_UPDATE[] updates,
            bool writeRuntime,
            bool writeBoot,
            string actionName)
        {
            if (updates == null || updates.Length == 0)
                return OperationResult.Fail(AppResources.Get("NoSelection"));

            if (!writeRuntime && !writeBoot)
                return OperationResult.Fail(AppResources.Get("NoStore"));

            try
            {
                return ApplyStores(
                    writeRuntime ? updates : null,
                    writeBoot ? updates : null,
                    actionName);
            }
            catch (ArgumentException ex)
            {
                return OperationResult.Fail(AppResources.Format("FailGenericFormat", actionName, ex.Message));
            }
            catch (Albacore.ViVe.Exceptions.FeaturePropertyOverflowException ex)
            {
                return OperationResult.Fail(AppResources.Format("FailGenericFormat", actionName, ex.Message));
            }
        }

        /// <summary>按存储分别写入：两个存储可以携带不同的更新集合。</summary>
        private static OperationResult ApplyStores(
            RTL_FEATURE_CONFIGURATION_UPDATE[] runtimeUpdates,
            RTL_FEATURE_CONFIGURATION_UPDATE[] bootUpdates,
            string actionName)
        {
            var result = new OperationResult();

            if (runtimeUpdates != null && runtimeUpdates.Length > 0)
            {
                var rc = FeatureManager.SetFeatureConfigurations(
                    runtimeUpdates, RTL_FEATURE_CONFIGURATION_TYPE.Runtime);
                if (rc != 0)
                {
                    result.Message = AppResources.Format(
                        "FailRuntimeFormat", actionName, NtStatus.Describe(rc));
                    return result;
                }
                result.AppliedRuntime = true;
            }

            if (bootUpdates != null && bootUpdates.Length > 0)
            {
                var rc = FeatureManager.SetFeatureConfigurations(
                    bootUpdates, RTL_FEATURE_CONFIGURATION_TYPE.Boot);
                if (rc != 0)
                {
                    // 运行时已改动，标记出来让历史记录仍可撤销那部分
                    result.AppliedRuntime = runtimeUpdates != null && runtimeUpdates.Length > 0;
                    result.Message = AppResources.Format(
                        "FailBootFormat", actionName, NtStatus.Describe(rc));
                    return result;
                }

                result.AppliedBoot = true;
                EnsureBootPending();
            }

            result.Success = true;
            result.NeedsReboot = result.AppliedBoot;
            result.Message = AppResources.Format("SuccessCountFormat", actionName,
                Math.Max(runtimeUpdates?.Length ?? 0, bootUpdates?.Length ?? 0));

            return result;
        }

        // 往启动存储写完必须把状态置为待重启，否则重启后改动不会生效
        private static void EnsureBootPending()
        {
            var result = FeatureManager.GetBootFeatureConfigurationState(out var currentStatus);
            if (result != 0)
            {
                if ((uint)result == StatusObjectNameNotFound)
                {
                    result = FeatureManager.InitializeBootStatusDataFile();
                    if (result != 0)
                        return;
                }
            }

            if (currentStatus != BSD_FEATURE_CONFIGURATION_STATE.BootPending)
                FeatureManager.SetBootFeatureConfigurationState(BSD_FEATURE_CONFIGURATION_STATE.BootPending);
        }

        public static ulong GetChangeStamp()
        {
            try
            {
                return FeatureManager.QueryFeatureConfigurationChangeStamp();
            }
            catch
            {
                return 0;
            }
        }

        public static bool IsBuildSupported()
            => Environment.OSVersion.Version.Build >= 18963;

        public static int CurrentBuild => Environment.OSVersion.Version.Build;
    }
}
