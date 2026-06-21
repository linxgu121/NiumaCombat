using System;
using NiumaCombat.Enum;
using UnityEngine;

namespace NiumaCombat.ViewData
{
    [Serializable]
    public sealed class CombatFloatingTextViewData
    {
        public string TargetActorId;
        public string RequestId;
        public CombatResultType ResultType;
        public float Value;
        public bool IsCritical;
        public bool IsKilled;
        public Vector3 WorldPosition;
        public string Text;
        public string StyleKey;

        public CombatFloatingTextViewData Clone()
        {
            return new CombatFloatingTextViewData
            {
                TargetActorId = TargetActorId,
                RequestId = RequestId,
                ResultType = ResultType,
                Value = Value,
                IsCritical = IsCritical,
                IsKilled = IsKilled,
                WorldPosition = WorldPosition,
                Text = Text,
                StyleKey = StyleKey
            };
        }
    }
}
