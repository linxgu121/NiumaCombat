using System;

namespace NiumaCombat.ViewData
{
    [Serializable]
    public sealed class CombatPanelViewData
    {
        public string ActorId;
        public long Revision;
        public CombatResultViewData LastResult;
        public CombatResultViewData[] RecentResults = Array.Empty<CombatResultViewData>();
        public CombatFloatingTextViewData[] FloatingTexts = Array.Empty<CombatFloatingTextViewData>();

        public CombatPanelViewData Clone()
        {
            return new CombatPanelViewData
            {
                ActorId = ActorId,
                Revision = Revision,
                LastResult = LastResult?.Clone(),
                RecentResults = CloneArray(RecentResults),
                FloatingTexts = CloneArray(FloatingTexts)
            };
        }

        public static CombatResultViewData[] CloneArray(CombatResultViewData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<CombatResultViewData>();
            }

            var clone = new CombatResultViewData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }

        public static CombatFloatingTextViewData[] CloneArray(CombatFloatingTextViewData[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<CombatFloatingTextViewData>();
            }

            var clone = new CombatFloatingTextViewData[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                clone[i] = source[i]?.Clone();
            }

            return clone;
        }
    }
}
