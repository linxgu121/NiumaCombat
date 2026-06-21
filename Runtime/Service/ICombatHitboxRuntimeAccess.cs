using NiumaCombat.Hitbox;

namespace NiumaCombat.Service
{
    internal interface ICombatHitboxRuntimeAccess
    {
        void Tick(float deltaTime);
        bool IsHitboxActive(string attackInstanceId);
        bool TryGetState(string attackInstanceId, out CombatHitboxRuntimeState state);
        bool TryIncrementHitCount(string attackInstanceId);
        bool TryDecrementHitCount(string attackInstanceId);
    }
}
