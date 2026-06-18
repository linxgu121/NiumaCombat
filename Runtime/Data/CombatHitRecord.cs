using System;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatHitRecord
    {
        public string AttackInstanceId;
        public string HitboxId;
        public string TargetActorId;
        public string HurtboxId;
        public float HitTime;

        public CombatHitRecord Clone()
        {
            return new CombatHitRecord
            {
                AttackInstanceId = AttackInstanceId,
                HitboxId = HitboxId,
                TargetActorId = TargetActorId,
                HurtboxId = HurtboxId,
                HitTime = HitTime
            };
        }
    }
}