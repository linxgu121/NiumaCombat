using System;
using UnityEngine;

namespace NiumaCombat.Data
{
    [Serializable]
    public sealed class CombatHitReactionData
    {
        public string ReactionId;
        public float StaggerSeconds;
        public float KnockbackDistance;
        public float KnockbackForce;
        public Vector3 HitDirection;
        public bool ForceKnockdown;
        public string HitVfxCueId;
        public string HitAudioCueId;

        public CombatHitReactionData Clone()
        {
            return new CombatHitReactionData
            {
                ReactionId = ReactionId,
                StaggerSeconds = StaggerSeconds,
                KnockbackDistance = KnockbackDistance,
                KnockbackForce = KnockbackForce,
                HitDirection = HitDirection,
                ForceKnockdown = ForceKnockdown,
                HitVfxCueId = HitVfxCueId,
                HitAudioCueId = HitAudioCueId
            };
        }
    }
}