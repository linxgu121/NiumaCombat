using NiumaCombat.Data;

namespace NiumaCombat.Service
{
    public interface ICombatHitboxService
    {
        string OpenHitbox(CombatHitboxDefinition definition, string ownerActorId);
        bool CloseHitbox(string attackInstanceId);
        bool IsHitboxActive(string attackInstanceId);
    }
}
