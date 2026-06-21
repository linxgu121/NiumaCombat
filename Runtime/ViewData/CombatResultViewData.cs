using System;
using NiumaCombat.Data;
using NiumaCombat.Enum;
using UnityEngine;

namespace NiumaCombat.ViewData
{
    [Serializable]
    public sealed class CombatResultViewData
    {
        public string RequestId;
        public string SourceActorId;
        public string TargetActorId;
        public string SkillId;
        public string HitboxId;
        public string HurtboxId;
        public CombatResultType ResultType;
        public CombatFailureReason FailureReason;
        public float RawValue;
        public float FinalValue;
        public bool IsCritical;
        public bool IsKilled;
        public Vector3 HitPoint;
        public Vector3 HitDirection;
        public long ResolvedAtUnixMs;
        public string Message;
        public string StyleKey;

        public CombatResultViewData Clone()
        {
            return new CombatResultViewData
            {
                RequestId = RequestId,
                SourceActorId = SourceActorId,
                TargetActorId = TargetActorId,
                SkillId = SkillId,
                HitboxId = HitboxId,
                HurtboxId = HurtboxId,
                ResultType = ResultType,
                FailureReason = FailureReason,
                RawValue = RawValue,
                FinalValue = FinalValue,
                IsCritical = IsCritical,
                IsKilled = IsKilled,
                HitPoint = HitPoint,
                HitDirection = HitDirection,
                ResolvedAtUnixMs = ResolvedAtUnixMs,
                Message = Message,
                StyleKey = StyleKey
            };
        }

        public static CombatResultViewData FromResult(CombatResult result)
        {
            if (result == null)
            {
                return null;
            }

            return new CombatResultViewData
            {
                RequestId = result.RequestId,
                SourceActorId = result.SourceActorId,
                TargetActorId = result.TargetActorId,
                SkillId = result.SkillId,
                HitboxId = result.HitboxId,
                HurtboxId = result.HurtboxId,
                ResultType = result.ResultType,
                FailureReason = result.FailureReason,
                RawValue = result.RawValue,
                FinalValue = result.FinalValue,
                IsCritical = result.IsCritical,
                IsKilled = result.IsKilled,
                HitPoint = result.HitPoint,
                HitDirection = result.HitDirection,
                ResolvedAtUnixMs = result.ResolvedAtUnixMs,
                Message = BuildMessage(result),
                StyleKey = BuildStyleKey(result)
            };
        }

        private static string BuildMessage(CombatResult result)
        {
            switch (result.ResultType)
            {
                case CombatResultType.Damage:
                    return result.IsCritical
                        ? $"暴击伤害 {result.FinalValue:0.#}"
                        : $"伤害 {result.FinalValue:0.#}";
                case CombatResultType.Heal:
                    return $"治疗 {result.FinalValue:0.#}";
                case CombatResultType.Blocked:
                    return "格挡 / 0 伤害";
                case CombatResultType.Immune:
                    return "免疫";
                case CombatResultType.Miss:
                    return "未命中";
                case CombatResultType.Filtered:
                    return $"已过滤：{result.FailureReason}";
                case CombatResultType.Failed:
                    return $"失败：{result.FailureReason}";
                default:
                    return result.ResultType.ToString();
            }
        }

        private static string BuildStyleKey(CombatResult result)
        {
            if (result == null)
            {
                return "combat-none";
            }

            if (result.IsKilled)
            {
                return "combat-kill";
            }

            if (result.IsCritical)
            {
                return "combat-critical";
            }

            switch (result.ResultType)
            {
                case CombatResultType.Damage:
                    return "combat-damage";
                case CombatResultType.Heal:
                    return "combat-heal";
                case CombatResultType.Blocked:
                    return "combat-blocked";
                case CombatResultType.Immune:
                    return "combat-immune";
                case CombatResultType.Miss:
                    return "combat-miss";
                case CombatResultType.Filtered:
                    return "combat-filtered";
                case CombatResultType.Failed:
                    return "combat-failed";
                default:
                    return "combat-none";
            }
        }
    }
}
