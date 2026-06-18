namespace NiumaCombat.Service
{
    public interface ICombatService : ICombatQuery, ICombatCommand
    {
        void Tick(float deltaTime);
    }
}