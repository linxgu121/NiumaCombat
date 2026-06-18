using NiumaCombat.Enum;

namespace NiumaCombat.Service
{
    public interface ICombatFactionResolver
    {
        CombatTeamRelation GetRelation(string sourceActorId, string targetActorId);
    }
}