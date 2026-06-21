using System;
using UnityEngine;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatHealRequest
    {
        public string RequestId;
        public string SourceActorId;
        public string TargetActorId;
        public string SkillId;
        public Vector3 HitPoint;
        public float BasePower;
        public string HealPowerAttributeId;
        public float HealScale;
        public float HealMultiplier = 1f;

        public bool HasRuntimeFields()
        {
            return !string.IsNullOrWhiteSpace(RequestId)
                || !string.IsNullOrWhiteSpace(SourceActorId)
                || !string.IsNullOrWhiteSpace(TargetActorId)
                || !string.IsNullOrWhiteSpace(SkillId)
                || HitPoint.sqrMagnitude > 0.0001f;
        }

        public void ClearRuntimeFields()
        {
            RequestId = string.Empty;
            SourceActorId = string.Empty;
            TargetActorId = string.Empty;
            SkillId = string.Empty;
            HitPoint = Vector3.zero;
        }

        public CombatHealRequest Clone()
        {
            return new CombatHealRequest
            {
                RequestId = RequestId,
                SourceActorId = SourceActorId,
                TargetActorId = TargetActorId,
                SkillId = SkillId,
                HitPoint = HitPoint,
                BasePower = BasePower,
                HealPowerAttributeId = HealPowerAttributeId,
                HealScale = HealScale,
                HealMultiplier = HealMultiplier
            };
        }
    }
}
