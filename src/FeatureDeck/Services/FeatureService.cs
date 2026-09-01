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

        public static OperationResult SetState(
            IEnumerable<FeatureItem> items,
            RTL_FEATURE_ENABLED_STATE state,
            bool writeRuntime,
            bool writeBoot)
        {
            var updates = items.Select(item => new RTL_FEATURE_CONFIGURATION_UPDATE
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

            return Apply(updates, writeRuntime, writeBoot,
                state == RTL_FEATURE_ENABLED_STATE.Enabled
                    ? AppResources.Get("ActionEnable")
                    : AppResources.Get("ActionDisable"));
        }

        public static OperationResult Reset(IEnumerable<FeatureItem> items, bool writeRuntime, bool writeBoot)
        {
            var updates = items.Select(item => new RTL_FEATURE_CONFIGURATION_UPDATE
            {
                FeatureId = item.FeatureId,
                Priority = item.Priority,
                Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
            }).ToArray();

            return Apply(updates, writeRuntime, writeBoot, AppResources.Get("ActionReset"));
        }

        public static OperationResult FullReset(bool writeRuntime, bool writeBoot)
        {
            var updates = new List<RTL_FEATURE_CONFIGURATION_UPDATE>();
            var seen = new HashSet<(uint, uint)>();

            if (writeRuntime)
                CollectResettables(RTL_FEATURE_CONFIGURATION_TYPE.Runtime, updates, seen);
            if (writeBoot)
                CollectResettables(RTL_FEATURE_CONFIGURATION_TYPE.Boot, updates, seen);

            if (updates.Count == 0)
                return OperationResult.Ok(AppResources.Get("NoResettable"));

            return Apply(updates.ToArray(), writeRuntime, writeBoot, AppResources.Get("ActionFullReset"));
        }

        private static void CollectResettables(
            RTL_FEATURE_CONFIGURATION_TYPE type,
            List<RTL_FEATURE_CONFIGURATION_UPDATE> target,
            HashSet<(uint, uint)> seen)
        {
            var configs = FeatureManager.QueryAllFeatureConfigurations(type);
            if (configs == null)
                return;

            foreach (var cfg in configs)
            {
                if (FeatureManager.IsPriorityImmutable(cfg.Priority))
                    continue;
                if (!seen.Add((cfg.FeatureId, (uint)cfg.Priority)))
                    continue;

                target.Add(new RTL_FEATURE_CONFIGURATION_UPDATE
                {
                    FeatureId = cfg.FeatureId,
                    Priority = cfg.Priority,
                    Operation = RTL_FEATURE_CONFIGURATION_OPERATION.ResetState
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
                if (writeRuntime)
                {
                    var result = FeatureManager.SetFeatureConfigurations(
                        updates, RTL_FEATURE_CONFIGURATION_TYPE.Runtime);
                    if (result != 0)
                        return OperationResult.Fail(
                            AppResources.Format("FailRuntimeFormat", actionName, NtStatus.Describe(result)));
                }

                if (writeBoot)
                {
                    var result = FeatureManager.SetFeatureConfigurations(
                        updates, RTL_FEATURE_CONFIGURATION_TYPE.Boot);
                    if (result != 0)
                        return OperationResult.Fail(
                            AppResources.Format("FailBootFormat", actionName, NtStatus.Describe(result)));

                    EnsureBootPending();
                }

                return OperationResult.Ok(
                    AppResources.Format("SuccessCountFormat", actionName, updates.Length),
                    needsReboot: writeBoot);
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
