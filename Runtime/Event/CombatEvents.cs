using NiumaCombat.Data;

namespace NiumaCombat.Event
{
    public readonly struct CombatHitConfirmedEvent
    {
        public readonly CombatResult Result;

        public CombatHitConfirmedEvent(CombatResult result)
        {
            Result = result?.Clone();
        }
    }

    public readonly struct CombatDamageAppliedEvent
    {
        public readonly CombatResult Result;

        public CombatDamageAppliedEvent(CombatResult result)
        {
            Result = result?.Clone();
        }
    }

    public readonly struct CombatHealedEvent
    {
        public readonly CombatResult Result;

        public CombatHealedEvent(CombatResult result)
        {
            Result = result?.Clone();
        }
    }

    public readonly struct CombatKilledEvent
    {
        public readonly CombatResult Result;

        public CombatKilledEvent(CombatResult result)
        {
            Result = result?.Clone();
        }
    }

    public readonly struct CombatResultRejectedEvent
    {
        public readonly CombatResult Result;

        public CombatResultRejectedEvent(CombatResult result)
        {
            Result = result?.Clone();
        }
    }
}