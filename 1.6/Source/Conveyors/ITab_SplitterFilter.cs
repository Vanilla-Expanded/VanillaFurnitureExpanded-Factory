using RimWorld;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class ITab_SplitterFilter : ITab_Storage
    {
        private static readonly Vector2 WinSize = new Vector2(300f, 480f);

        public ITab_SplitterFilter()
        {
            size = WinSize;
            labelKey = "VFEFactory_TabSplitterFilter";
            tutorTag = "SplitterFilter";
        }

        protected override IStoreSettingsParent SelStoreSettingsParent
        {
            get
            {
                if (base.SelObject is Building_Conveyor conveyor)
                {
                    return conveyor;
                }
                return null;
            }
        }

        protected override bool IsPrioritySettingVisible => false;
    }
}
