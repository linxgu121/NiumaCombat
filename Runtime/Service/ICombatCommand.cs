using NiumaCombat.Data;

namespace NiumaCombat.Service
{
    public interface ICombatCommand
    {
        CombatResult ApplyDamage(CombatDamageRequest request);
        CombatResult ApplyHeal(CombatHealRequest request);
        bool TryRegisterHit(CombatHitRecord record);
        void ClearAttackHits(string attackInstanceId);
    }
}