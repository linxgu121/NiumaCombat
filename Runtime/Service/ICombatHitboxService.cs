using NiumaCombat.Data;
using NiumaCombat.Hitbox;

namespace NiumaCombat.Service
{
    public interface ICombatHitboxService
    {
        string OpenHitbox(CombatHitboxDefinition definition, string ownerActorId);
        bool CloseHitbox(string attackInstanceId);
        bool IsHitboxActive(string attackInstanceId);
        void Tick(float deltaTime);
        bool TryGetState(string attackInstanceId, out CombatHitboxRuntimeState state);
        bool TryIncrementHitCount(string attackInstanceId);
        bool HasReachedMaxHitCount(string attackInstanceId);
    }
}
