/*
    ViVe - Windows feature configuration library
    Copyright (C) 2019-2025  @thebookisclosed

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
 */

using Albacore.ViVe.NativeEnums;
using Albacore.ViVe.NativeMethods;
using Albacore.ViVe.NativeStructs;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Albacore.ViVe
{
    public static class FeatureManager
    {
        public static readonly RTL_FEATURE_CONFIGURATION_PRIORITY[] ImmutablePriorities = new[] {
            RTL_FEATURE_CONFIGURATION_PRIORITY.ImageDefault,
            RTL_FEATURE_CONFIGURATION_PRIORITY.EKB,
            RTL_FEATURE_CONFIGURATION_PRIORITY.ImageDefaultEditionOverride,
            RTL_FEATURE_CONFIGURATION_PRIORITY.Security,
            RTL_FEATURE_CONFIGURATION_PRIORITY.ImageOverride
        };

        public static bool IsPriorityImmutable(RTL_FEATURE_CONFIGURATION_PRIORITY priority)
            => ImmutablePriorities.Contains(priority);

        private const int QueryRetryLimit = 4;
        private const int StatusBufferOverflow = unchecked((int)0x80000005);
        private const int InitialCapacity = 512;

        public unsafe static RTL_FEATURE_CONFIGURATION[] QueryAllFeatureConfigurations(
            RTL_FEATURE_CONFIGURATION_TYPE configurationType, ulong* changeStamp)
        {
            int capacity = InitialCapacity;
            ulong localStamp = 0;
            ulong* stampPtr = changeStamp != null ? changeStamp : &localStamp;

            for (int attempt = 0; attempt < QueryRetryLimit; attempt++)
            {
                var buffer = new RTL_FEATURE_CONFIGURATION[capacity];
                int count = capacity;
                int hRes;

                fixed (RTL_FEATURE_CONFIGURATION* configsPtr = buffer)
                    hRes = Ntdll.RtlQueryAllFeatureConfigurations(
                        configurationType, stampPtr, configsPtr, ref count);

                if (hRes == 0)
                    return count == capacity ? buffer : buffer.Take(count).ToArray();

                // 容量不足时内核会把所需条目数写回 count，据此扩容重试；
                // 期间可能有新条目加入，多留一点余量
                if (count > capacity)
                {
                    capacity = count + 32;
                    continue;
                }

                return null;
            }
            return null;
        }

        public unsafe static RTL_FEATURE_CONFIGURATION[] QueryAllFeatureConfigurations(
            RTL_FEATURE_CONFIGURATION_TYPE configurationType = RTL_FEATURE_CONFIGURATION_TYPE.Runtime)
            => QueryAllFeatureConfigurations(configurationType, null);

        public static RTL_FEATURE_CONFIGURATION? QueryFeatureConfiguration(
            uint featureId, RTL_FEATURE_CONFIGURATION_TYPE configurationType)
        {
            ulong changeStamp = 0;
            var result = Ntdll.RtlQueryFeatureConfiguration(featureId, configurationType, ref changeStamp, out var config);
            return result == 0 ? config : (RTL_FEATURE_CONFIGURATION?)null;
        }

        public static ulong QueryFeatureConfigurationChangeStamp()
            => Ntdll.RtlQueryFeatureConfigurationChangeStamp();

        public static int SetFeatureConfigurations(
            RTL_FEATURE_CONFIGURATION_UPDATE[] updates, RTL_FEATURE_CONFIGURATION_TYPE configurationType)
        {
            if (updates == null || updates.Length == 0)
                return 0;

            foreach (var update in updates)
            {
                if (ImmutablePriorities.Contains(update.Priority))
                    throw new ArgumentException(
                        $"优先级 {update.Priority} ({(uint)update.Priority}) 由系统镜像管理，不可写入。");
            }

            ulong previousChangeStamp = Ntdll.RtlQueryFeatureConfigurationChangeStamp();
            return Ntdll.RtlSetFeatureConfigurations(ref previousChangeStamp, configurationType, updates, updates.Length);
        }

        private const int RtlBsdItemFeatureConfigurationState = 17;

        public static int SetBootFeatureConfigurationState(BSD_FEATURE_CONFIGURATION_STATE state)
        {
            int newState = (int)state;
            return Ntdll.RtlSetSystemBootStatus(RtlBsdItemFeatureConfigurationState, ref newState, sizeof(int), IntPtr.Zero);
        }

        public static int GetBootFeatureConfigurationState(out BSD_FEATURE_CONFIGURATION_STATE state)
        {
            var apiResult = Ntdll.RtlGetSystemBootStatus(RtlBsdItemFeatureConfigurationState, out int intState, sizeof(int), IntPtr.Zero);
            state = (BSD_FEATURE_CONFIGURATION_STATE)intState;
            return apiResult;
        }

        // LKG 存储偶尔会被 fcon.dll 的一个 use-after-free 缺陷写坏，这里把损坏的头部修回来
        public static bool FixLKGStore()
        {
            try
            {
                using (var rKey = Registry.LocalMachine.OpenSubKey(
                    @"CurrentControlSet\Control\FeatureManagement\LastKnownGood", true))
                {
                    if (rKey == null)
                        return false;
                    var lkgBlob = (byte[])rKey.GetValue("LKGConfiguration");
                    if (lkgBlob == null)
                        return false;
                    if (BitConverter.ToInt32(lkgBlob, 0) == 0)
                        return false;

                    int headerSize = sizeof(int);
                    int oneConfigSize = Marshal.SizeOf(typeof(RTL_FEATURE_CONFIGURATION));
                    var fixedBlob = new byte[lkgBlob.Length - oneConfigSize];
                    Array.Copy(lkgBlob, headerSize + oneConfigSize, fixedBlob, headerSize, fixedBlob.Length - headerSize);
                    rKey.SetValue("LKGConfiguration", fixedBlob);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static int InitializeBootStatusDataFile()
            => Ntdll.RtlCreateBootStatusDataFile(null);
    }
}
