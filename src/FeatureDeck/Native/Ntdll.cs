/*
    ViVe - Windows feature configuration library
    Copyright (C) 2019-2025  @thebookisclosed

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.
 */

using Albacore.ViVe.NativeEnums;
using Albacore.ViVe.NativeStructs;
using System.Runtime.InteropServices;

namespace Albacore.ViVe.NativeMethods
{
    public delegate void FeatureConfigurationChangeCallback(System.IntPtr Context);

    public static class Ntdll
    {
        // 注意：最后一个参数是 in/out —— 传入缓冲区容量，返回实际写入的条目数。
        // 容量不足时返回 STATUS_BUFFER_OVERFLOW，并把所需条目数写回该参数。
        // 不要传 null 缓冲区，新版 Windows 上会直接访问违规。
        [DllImport("ntdll.dll")]
        public unsafe static extern int RtlQueryAllFeatureConfigurations(
            RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType,
            ulong* changeStamp,
            RTL_FEATURE_CONFIGURATION* featureConfigurations,
            ref int featureConfigurationCount
            );

        [DllImport("ntdll.dll")]
        public static extern int RtlQueryFeatureConfiguration(
            uint featureId,
            RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType,
            ref ulong changeStamp,
            out RTL_FEATURE_CONFIGURATION featureConfiguration
            );

        [DllImport("ntdll.dll")]
        public static extern ulong RtlQueryFeatureConfigurationChangeStamp();

        [DllImport("ntdll.dll")]
        public static extern int RtlSetFeatureConfigurations(
            ref ulong previousChangeStamp,
            RTL_FEATURE_CONFIGURATION_TYPE featureConfigurationType,
            RTL_FEATURE_CONFIGURATION_UPDATE[] featureConfigurations,
            int featureConfigurationCount
            );

        [DllImport("ntdll.dll")]
        public static extern int RtlSetSystemBootStatus(
            int bsdItemType,
            ref int data,
            int dataLength,
            System.IntPtr returnLength
            );

        [DllImport("ntdll.dll")]
        public static extern int RtlGetSystemBootStatus(
            int bsdItemType,
            out int data,
            int dataLength,
            System.IntPtr returnLength
            );

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        public static extern int RtlCreateBootStatusDataFile(string bootStatusPath);
    }
}
