using System;
using NiumaCombat.Enum;
using UnityEngine;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatDamageRequest
    {
        public string RequestId;
        public string AttackInstanceId;
        public string SourceActorId;
        public string TargetActorId;
        public string SkillId;
        public string HitboxId;
        public string HurtboxId;
        public Vector3 HitPoint;
        public Vector3 HitDirection;
        public Vector3 SourcePosition;
        public CombatDamageType DamageType = CombatDamageType.Physical;
        public float BasePower;
        public string AttackAttributeId;
        public float AttackScale;
        public string DefenseAttributeId;
        public float DefenseScale;
        public string ResistanceAttributeId;
        public float DamageMultiplier = 1f;
        public float CritRateOverride = -1f;
        public float CritDamageOverride = -1f;
        public bool CanCrit = true;
        public bool IgnoreDefense;
        public bool IgnoreResistance;
        public CombatHitReactionData Reaction;

        public CombatDamageRequest Clone()
        {
            return new CombatDamageRequest
            {
                RequestId = RequestId,
                AttackInstanceId = AttackInstanceId,
                SourceActorId = SourceActorId,
                TargetActorId = TargetActorId,
                SkillId = SkillId,
                HitboxId = HitboxId,
                HurtboxId = HurtboxId,
                HitPoint = HitPoint,
                HitDirection = HitDirection,
                SourcePosition = SourcePosition,
                DamageType = DamageType,
                BasePower = BasePower,
                AttackAttributeId = AttackAttributeId,
                AttackScale = AttackScale,
                DefenseAttributeId = DefenseAttributeId,
                DefenseScale = DefenseScale,
                ResistanceAttributeId = ResistanceAttributeId,
                DamageMultiplier = DamageMultiplier,
                CritRateOverride = CritRateOverride,
                CritDamageOverride = CritDamageOverride,
                CanCrit = CanCrit,
                IgnoreDefense = IgnoreDefense,
                IgnoreResistance = IgnoreResistance,
                Reaction = Reaction?.Clone()
            };
        }
    }
}