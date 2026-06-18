using NiumaAttribute.Service;
using NiumaCore.Event;

namespace NiumaCombat.Service
{
    public interface ICombatConfigurationService
    {
        void SetAttributeService(IAttributeQuery query, IAttributeCommand command);
        void SetFactionResolver(ICombatFactionResolver resolver);
        void SetEventBus(IEventBus eventBus);
    }
}