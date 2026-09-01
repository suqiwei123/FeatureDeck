using System;
using System.Collections.Generic;
using System.IO;

namespace ViVeTool.GUI.Services
{
    public static class FeatureNaming
    {
        public const string DictFileName = "FeatureDictionary.pfs";

        private static readonly Dictionary<uint, string> IdToName = new();
        private static readonly Dictionary<string, List<uint>> NameToIds =
            new(StringComparer.OrdinalIgnoreCase);

        public static int LoadedCount => IdToName.Count;
        public static bool IsLoaded { get; private set; }

        public static string DictFilePath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", DictFileName);

        public static int Load()
        {
            IdToName.Clear();
            NameToIds.Clear();
            IsLoaded = false;

            var path = DictFilePath;
            if (!File.Exists(path))
                return 0;

            try
            {
                // 显式 UTF-8，避免换设备后字典文件被其它工具改存为不同编码导致乱码
                using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
                while (reader.ReadLine() is { } rawLine)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    var separator = rawLine.IndexOf(',');
                    if (separator <= 0)
                        continue;

                    var name = rawLine.Substring(0, separator);
                    if (!uint.TryParse(rawLine.Substring(separator + 1).Trim(), out var id))
                        continue;

                    IdToName[id] = name;

                    if (!NameToIds.TryGetValue(name, out var list))
                    {
                        list = new List<uint>();
                        NameToIds[name] = list;
                    }
                    list.Add(id);
                }
            }
            catch
            {
                // 字典文件损坏或不可读时降级为无名称模式，不影响主功能
                IdToName.Clear();
                NameToIds.Clear();
                return 0;
            }

            IsLoaded = IdToName.Count > 0;
            return IdToName.Count;
        }

        public static string GetName(uint featureId)
            => IdToName.TryGetValue(featureId, out var name) ? name : null;

        public static List<uint> FindIdsByName(string name)
            => NameToIds.TryGetValue(name ?? string.Empty, out var list) ? list : new List<uint>();
    }
}
