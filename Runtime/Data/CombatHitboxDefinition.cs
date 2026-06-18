using System;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatHitboxDefinition
    {
        public string HitboxId;
        public string DisplayName;
        public CombatDamageRequest DamageTemplate = new CombatDamageRequest();
        public float ActiveSeconds;
        public bool HitSameTargetOnce = true;
        public float SameTargetHitCooldownSeconds;
        public int MaxHitCount;
        public string[] RequiredTargetTags = Array.Empty<string>();
        public string[] RejectedTargetTags = Array.Empty<string>();

        public CombatHitboxDefinition Clone()
        {
            return new CombatHitboxDefinition
            {
                HitboxId = HitboxId,
                DisplayName = DisplayName,
                DamageTemplate = DamageTemplate?.Clone(),
                ActiveSeconds = ActiveSeconds,
                HitSameTargetOnce = HitSameTargetOnce,
                SameTargetHitCooldownSeconds = SameTargetHitCooldownSeconds,
                MaxHitCount = MaxHitCount,
                RequiredTargetTags = RequiredTargetTags != null ? (string[])RequiredTargetTags.Clone() : Array.Empty<string>(),
                RejectedTargetTags = RejectedTargetTags != null ? (string[])RejectedTargetTags.Clone() : Array.Empty<string>()
            };
        }
    }
}