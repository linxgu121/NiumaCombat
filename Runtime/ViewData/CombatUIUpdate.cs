using System;

namespace NiumaCombat.ViewData
{
    [Serializable]
    public sealed class CombatUIUpdate
    {
        public CombatUIUpdateType UpdateType;
        public long Revision;
        public CombatPanelViewData Current;
        public CombatResultViewData Result;

        public CombatUIUpdate()
        {
        }

        public CombatUIUpdate(CombatUIUpdateType updateType, long revision, CombatPanelViewData current, CombatResultViewData result)
        {
            UpdateType = updateType;
            Revision = revision;
            Current = current?.Clone();
            Result = result?.Clone();
        }

        public CombatUIUpdate Clone()
        {
            return new CombatUIUpdate(UpdateType, Revision, Current, Result);
        }
    }
}
