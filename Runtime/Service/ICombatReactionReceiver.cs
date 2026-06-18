using NiumaCombat.Data;

namespace NiumaCombat.Service
{
    public interface ICombatReactionReceiver
    {
        void ApplyCombatResult(CombatResult result);
    }
}