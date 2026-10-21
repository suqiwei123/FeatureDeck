using System;
using System.Collections.Generic;

namespace FeatureDeck.Models
{
    /// <summary>被记录的用户操作类型。</summary>
    public enum HistoryAction
    {
        Enable,
        Disable,
        Reset,
        FullReset
    }

    /// <summary>
    /// 一条历史记录 = 用户的一次写操作（可能覆盖多个特性、两个存储）。
    /// 只记录内核确实写入过的操作，因此每条记录至少有一个存储可撤销。
    /// </summary>
    public class HistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

        public HistoryAction Action { get; set; }

        /// <summary>用户当时选择的目标存储。</summary>
        public bool WriteRuntime { get; set; }
        public bool WriteBoot { get; set; }

        /// <summary>实际写入成功的存储，撤销只针对这些存储。</summary>
        public bool AppliedRuntime { get; set; }
        public bool AppliedBoot { get; set; }

        public bool Success { get; set; }

        /// <summary>记录当时的结果文本（按记录时的语言保存）。</summary>
        public string Message { get; set; }

        public bool Undone { get; set; }
        public DateTimeOffset? UndoneAt { get; set; }

        public List<HistoryChange> Changes { get; set; } = new();
    }

    /// <summary>单个特性在某个优先级层上的变更。</summary>
    public class HistoryChange
    {
        public uint FeatureId { get; set; }

        /// <summary>记录时的名称快照，字典缺失时为空。</summary>
        public string Name { get; set; }

        /// <summary>受影响的优先级层（Enable/Disable 恒为 User，Reset 为原层级）。</summary>
        public uint Priority { get; set; }

        public bool IsWexp { get; set; }

        /// <summary>操作后的状态；Reset/FullReset 为 Default。</summary>
        public uint TargetState { get; set; }

        /// <summary>操作前该层的配置；null 表示该存储未被写入。</summary>
        public LayerSnapshot RuntimeBefore { get; set; }
        public LayerSnapshot BootBefore { get; set; }
    }

    /// <summary>写入前的单层快照，撤销时按它回写。</summary>
    public class LayerSnapshot
    {
        /// <summary>该优先级层操作前是否已存在；不存在说明覆盖层由本次操作新建。</summary>
        public bool Existed { get; set; }

        public uint State { get; set; }
        public uint Variant { get; set; }
        public uint VariantPayloadKind { get; set; }
        public uint VariantPayload { get; set; }
    }
}
