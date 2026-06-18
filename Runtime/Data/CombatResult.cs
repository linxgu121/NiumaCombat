using System;
using NiumaCombat.Enum;
using UnityEngine;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatResult
    {
        public string RequestId;
        public string AttackInstanceId;
        public string SourceActorId;
        public string TargetActorId;
        public string SkillId;
        public string HitboxId;
        public string HurtboxId;
        public Vector3 HitPoint;
        public CombatResultType ResultType;
        public CombatFailureReason FailureReason;
        public float RawValue;
        public float FinalValue;
        public bool IsCritical;
        public bool IsKilled;
        public float TargetHpBefore;
        public float TargetHpAfter;
        public CombatHitReactionData Reaction;
        public long ResolvedAtUnixMs;

        public bool IsImmune => ResultType == CombatResultType.Immune;
        public bool IsMissed => ResultType == CombatResultType.Miss;

        public CombatResult Clone()
        {
            return new CombatResult
            {
                RequestId = RequestId,
                AttackInstanceId = AttackInstanceId,
                SourceActorId = SourceActorId,
                TargetActorId = TargetActorId,
                SkillId = SkillId,
                HitboxId = HitboxId,
                HurtboxId = HurtboxId,
                HitPoint = HitPoint,
                ResultType = ResultType,
                FailureReason = FailureReason,
                RawValue = RawValue,
                FinalValue = FinalValue,
                IsCritical = IsCritical,
                IsKilled = IsKilled,
                TargetHpBefore = TargetHpBefore,
                TargetHpAfter = TargetHpAfter,
                Reaction = Reaction?.Clone(),
                ResolvedAtUnixMs = ResolvedAtUnixMs
            };
        }
    }
}