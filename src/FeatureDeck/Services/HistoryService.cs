using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FeatureDeck.Models;

namespace FeatureDeck.Services
{
    /// <summary>
    /// 修改历史的持久化与撤销入口。
    /// 存储位置与 settings.ini 同目录：%LOCALAPPDATA%\FeatureDeck\history.json，
    /// 只保留最近 <see cref="MaxRecords"/> 条，写文件失败不阻塞主功能。
    /// </summary>
    public static class HistoryService
    {
        /// <summary>历史条目上限，超出后丢弃最旧的记录。</summary>
        public const int MaxRecords = 100;

        private const int CurrentSchema = 1;

        private static readonly object Gate = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FeatureDeck");
        private static readonly string _filePath = Path.Combine(_dir, "history.json");

        private static List<HistoryRecord> _records;

        /// <summary>当前全部记录，新的在前。</summary>
        public static IReadOnlyList<HistoryRecord> Records
        {
            get
            {
                lock (Gate)
                {
                    EnsureLoaded();
                    return _records.ToList();
                }
            }
        }

        /// <summary>追加一条记录并落盘。</summary>
        public static void Add(HistoryRecord record)
        {
            if (record == null)
                return;

            lock (Gate)
            {
                EnsureLoaded();
                _records.Insert(0, record);
                if (_records.Count > MaxRecords)
                    _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);
                Save();
            }
        }

        /// <summary>
        /// 撤销一条记录：按记录里的前状态回写内核。
        /// 只有完全成功才标记为已撤销，部分失败保留可再次撤销。
        /// </summary>
        public static OperationResult Undo(HistoryRecord record)
        {
            if (record == null)
                return OperationResult.Fail(AppResources.Get("HistoryNoSelection"));

            if (record.Undone)
                return OperationResult.Fail(AppResources.Get("HistoryAlreadyUndone"));

            var result = FeatureService.Restore(
                record.Changes, record.AppliedRuntime, record.AppliedBoot);

            if (result != null && result.Success)
            {
                lock (Gate)
                {
                    record.Undone = true;
                    record.UndoneAt = DateTimeOffset.Now;
                    EnsureLoaded();
                    Save();
                }
            }

            return result;
        }

        private static void EnsureLoaded()
        {
            if (_records != null)
                return;

            var list = new List<HistoryRecord>();

            try
            {
                if (File.Exists(_filePath))
                {
                    var doc = JsonSerializer.Deserialize<HistoryFile>(
                        File.ReadAllText(_filePath), JsonOptions);

                    if (doc?.Records != null)
                    {
                        foreach (var record in doc.Records)
                        {
                            if (record?.Changes == null)
                                continue;
                            record.Changes.RemoveAll(c => c == null);
                            list.Add(record);
                        }
                    }
                }
            }
            catch
            {
                // 文件损坏或结构变化：按空历史处理，下次写入会覆盖
            }

            _records = list
                .OrderByDescending(x => x.Timestamp)
                .Take(MaxRecords)
                .ToList();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(_dir);

                var json = JsonSerializer.Serialize(
                    new HistoryFile { Schema = CurrentSchema, Records = _records }, JsonOptions);

                // 先写临时文件再替换，避免进程中断留下半截 JSON
                var temp = _filePath + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, _filePath, true);
            }
            catch
            {
                // 保存失败不影响本次操作结果
            }
        }

        private sealed class HistoryFile
        {
            public int Schema { get; set; } = CurrentSchema;
            public List<HistoryRecord> Records { get; set; } = new();
        }
    }
}
