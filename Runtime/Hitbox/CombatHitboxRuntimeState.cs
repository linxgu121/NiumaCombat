using System;
using NiumaCombat.Data;

namespace NiumaCombat.Hitbox
{
    [Serializable]
    public sealed class CombatHitboxRuntimeState
    {
        public string AttackInstanceId;
        public string OwnerActorId;
        public CombatHitboxDefinition Definition;
        public float RemainingSeconds;
        public float OpenedAtTime;
        public int EffectiveHitCount;

        public CombatHitboxRuntimeState Clone()
        {
            return new CombatHitboxRuntimeState
            {
                AttackInstanceId = AttackInstanceId,
                OwnerActorId = OwnerActorId,
                Definition = Definition?.Clone(),
                RemainingSeconds = RemainingSeconds,
                OpenedAtTime = OpenedAtTime,
                EffectiveHitCount = EffectiveHitCount
            };
        }
    }
}
