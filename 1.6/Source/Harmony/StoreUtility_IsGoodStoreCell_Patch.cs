using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(StoreUtility), nameof(StoreUtility.IsGoodStoreCell))]
    public static class StoreUtility_IsGoodStoreCell_Patch
    {
        public static void Postfix(IntVec3 c, Map map, Thing t, Pawn carrier, Faction faction, ref bool __result)
        {
            if (__result && carrier != null)
            {
                if (map.haulDestinationManager.SlotGroupParentAt(c) is Building_FactoryHopper hopper && !hopper.allowInserting)
                {
                    __result = false;
                }
            }
        }
    }
}
