using System;
using UnityEngine;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class SkillCombatOutputData
    {
        [Tooltip("输出 ID。用于调试日志定位是哪一条 Combat 输出，可为空。")]
        public string OutputId;

        [Tooltip("伤害模板。Skill 命中后会填入 SourceActorId、TargetActorId、SkillId 等运行时字段再交给 Combat 结算。为空表示不造成伤害。")]
        public CombatDamageRequest DamageTemplate;

        [Tooltip("治疗模板。Skill 命中后会填入 SourceActorId、TargetActorId、SkillId 等运行时字段再交给 Combat 结算。为空表示不治疗。")]
        public CombatHealRequest HealTemplate;

        [Tooltip("只对第一个命中目标结算。通常单体技能勾选这个。")]
        public bool ApplyToPrimaryTarget = true;

        [Tooltip("对所有命中目标结算。AOE 或多目标技能勾选这个；勾选后优先于 ApplyToPrimaryTarget。")]
        public bool ApplyToAllResolvedTargets;

        public bool HasRuntimeFields()
        {
            return (DamageTemplate != null && DamageTemplate.HasRuntimeFields())
                || (HealTemplate != null && HealTemplate.HasRuntimeFields());
        }

        public void ClearRuntimeFields()
        {
            DamageTemplate?.ClearRuntimeFields();
            HealTemplate?.ClearRuntimeFields();
        }

        public SkillCombatOutputData Clone()
        {
            return new SkillCombatOutputData
            {
                OutputId = OutputId,
                DamageTemplate = DamageTemplate?.Clone(),
                HealTemplate = HealTemplate?.Clone(),
                ApplyToPrimaryTarget = ApplyToPrimaryTarget,
                ApplyToAllResolvedTargets = ApplyToAllResolvedTargets
            };
        }

        public static SkillCombatOutputData[] CloneArray(SkillCombatOutputData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<SkillCombatOutputData>();
            }

            var result = new SkillCombatOutputData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = source[i]?.Clone();
            }

            return result;
        }
    }
}
