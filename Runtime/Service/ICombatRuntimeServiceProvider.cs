namespace NiumaCombat.Service
{
    public interface ICombatRuntimeServiceProvider
    {
        ICombatQuery CombatQuery { get; }
        ICombatCommand CombatCommand { get; }
        ICombatHitboxService CombatHitboxService { get; }
    }
}
