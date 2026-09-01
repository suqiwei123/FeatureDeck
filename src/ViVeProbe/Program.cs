using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Albacore.ViVe;
using Albacore.ViVe.NativeEnums;
using Albacore.ViVe.NativeStructs;

namespace ViVeProbe
{
    internal static class Program
    {
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"Windows 内部版本: {Environment.OSVersion.Version.Build}");

            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            Console.WriteLine($"已提权(管理员): {principal.IsInRole(WindowsBuiltInRole.Administrator)}");
            Console.WriteLine($"结构体大小: {Marshal.SizeOf<RTL_FEATURE_CONFIGURATION>()} 字节");
            Console.WriteLine($"ChangeStamp: {FeatureManager.QueryFeatureConfigurationChangeStamp()}");

            var lkgResult = FeatureManager.GetBootFeatureConfigurationState(out var lkgState);
            Console.WriteLine($"LKG 状态: {(lkgResult == 0 ? lkgState.ToString() : "0x" + ((uint)lkgResult).ToString("X8"))}");
            Console.WriteLine();

            var failure = false;
            foreach (var type in new[]
                     {
                         RTL_FEATURE_CONFIGURATION_TYPE.Runtime,
                         RTL_FEATURE_CONFIGURATION_TYPE.Boot
                     })
            {
                Console.WriteLine($"--- {type} 存储 ---");
                var configs = FeatureManager.QueryAllFeatureConfigurations(type);

                if (configs == null)
                {
                    Console.WriteLine("  读取失败。");
                    failure = true;
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"  条目数: {configs.Length}");
                Console.WriteLine("  前 5 条:");
                foreach (var cfg in configs.Take(5))
                {
                    Console.WriteLine($"    ID={cfg.FeatureId,-10} 优先级={cfg.Priority,-28} 状态={cfg.EnabledState,-8} 实验={cfg.IsWexpConfiguration,-5} 变体={cfg.Variant}");
                }

                var byPriority = configs.GroupBy(x => x.Priority)
                    .OrderBy(g => (uint)g.Key)
                    .Select(g => $"{g.Key}={g.Count()}");
                Console.WriteLine("  优先级分布: " + string.Join(", ", byPriority));
                Console.WriteLine($"  受保护(不可写): {configs.Count(x => FeatureManager.IsPriorityImmutable(x.Priority))}");
                Console.WriteLine();
            }

            // 顺带验证字典在本机能否被解析
            var dictPath = Path.Combine(AppContext.BaseDirectory, "Assets", "FeatureDictionary.pfs");
            Console.WriteLine($"字典路径: {dictPath}");
            Console.WriteLine($"字典存在: {File.Exists(dictPath)}");

            // 覆盖率诊断：统计本机唯一 ID 中字典能命中的比例
            try
            {
                var allIds = new System.Collections.Generic.HashSet<uint>();
                foreach (var type in new[] { RTL_FEATURE_CONFIGURATION_TYPE.Runtime, RTL_FEATURE_CONFIGURATION_TYPE.Boot })
                {
                    var configs = FeatureManager.QueryAllFeatureConfigurations(type);
                    if (configs != null)
                        foreach (var c in configs) allIds.Add(c.FeatureId);
                }

                // 字典 ID 集合：从探针目录向上查找 GUI 项目的 Assets 字典
                var guiAssets = FindGuiDictionary();
                var dictIds = new System.Collections.Generic.HashSet<uint>();
                if (guiAssets != null && File.Exists(guiAssets))
                {
                    foreach (var line in File.ReadLines(guiAssets))
                    {
                        var sep = line.LastIndexOf(',');
                        if (sep > 0 && uint.TryParse(line.Substring(sep + 1).Trim(), out var id))
                            dictIds.Add(id);
                    }
                }

                var covered = 0;
                var uncovered = new System.Collections.Generic.List<uint>();
                foreach (var id in allIds)
                {
                    if (dictIds.Contains(id)) covered++;
                    else uncovered.Add(id);
                }

                Console.WriteLine();
                Console.WriteLine("=== 覆盖率诊断 ===");
                Console.WriteLine($"本机唯一 Feature ID 总数: {allIds.Count}");
                Console.WriteLine($"字典命中: {covered}  ({100.0 * covered / Math.Max(1, allIds.Count):F1}%)");
                Console.WriteLine($"字典未命中(无名): {uncovered.Count}");
                Console.WriteLine("未命中的 ID 前 30 个: " + string.Join(", ", uncovered.Take(30)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"覆盖率诊断失败: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine(failure ? "探针结束（存在失败项）。" : "探针结束，全部通过。");
            return failure ? 1 : 0;
        }

        // 从探针输出目录向上逐级查找 GUI 项目的字典文件
        private static string FindGuiDictionary()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "ViVeTool.GUI", "Assets", "FeatureDictionary.pfs");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }
    }
}
