using Albacore.ViVe.NativeEnums;
using FeatureDeck.Services;

namespace FeatureDeck.Models
{
    public class FeatureItem
    {
        public uint FeatureId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public bool HasName => !string.IsNullOrEmpty(Name);
        public string DisplayName => HasName ? Name : AppResources.Format("UnnamedFeatureFormat", FeatureId);
        public string IdText => FeatureId.ToString();
        public bool HasCategory => !string.IsNullOrEmpty(Category);

        public RTL_FEATURE_CONFIGURATION_PRIORITY Priority { get; set; }
        public string PriorityText => $"{Priority} ({(uint)Priority})";
        public bool IsImmutable { get; set; }

        public bool IsWexpConfiguration { get; set; }
        public bool HasSubscriptions { get; set; }
        public uint Variant { get; set; }
        public RTL_FEATURE_VARIANT_PAYLOAD_KIND VariantPayloadKind { get; set; }
        public uint VariantPayload { get; set; }

        public bool ExistsInRuntime { get; set; }
        public RTL_FEATURE_ENABLED_STATE RuntimeState { get; set; }
        public bool ExistsInBoot { get; set; }
        public RTL_FEATURE_ENABLED_STATE BootState { get; set; }

        public string RuntimeStateText => ExistsInRuntime ? Translate(RuntimeState) : "-";
        public string BootStateText => ExistsInBoot ? Translate(BootState) : "-";
        public string TypeText => IsWexpConfiguration
            ? AppResources.Get("TypeExperimental")
            : AppResources.Get("TypeOverride");
        public string VariantText => Variant == 0 ? "-" : Variant.ToString();

        // 用户或测试级别写入过的条目才算是"被改过"，用于快速定位
        public bool IsUserModified =>
            Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.User ||
            Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.UserPolicy ||
            Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.Test ||
            Priority == RTL_FEATURE_CONFIGURATION_PRIORITY.Dynamic;

        public bool CanEdit => !IsImmutable;

        private static string Translate(RTL_FEATURE_ENABLED_STATE state) => state switch
        {
            RTL_FEATURE_ENABLED_STATE.Enabled => AppResources.Get("StateEnabled"),
            RTL_FEATURE_ENABLED_STATE.Disabled => AppResources.Get("StateDisabled"),
            _ => AppResources.Get("StateDefault")
        };

        public string SearchableText => string.IsNullOrEmpty(Name) ? FeatureId.ToString() : $"{Name} {FeatureId}";
    }
}
