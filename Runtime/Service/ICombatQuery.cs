using NiumaCombat.Data;
using NiumaCombat.Enum;

namespace NiumaCombat.Service
{
    public interface ICombatQuery
    {
        long Revision { get; }

        bool IsActorAlive(string actorId);
        bool CanTarget(string sourceActorId, string targetActorId);
        bool HasHit(string attackInstanceId, string targetActorId);
        CombatResult GetLastOutgoingResult(string sourceActorId);
        CombatResult GetLastIncomingResult(string targetActorId);
        CombatResult[] GetRecentResults(string actorId, CombatResultActorRole role, int maxCount = 16);
    }
}