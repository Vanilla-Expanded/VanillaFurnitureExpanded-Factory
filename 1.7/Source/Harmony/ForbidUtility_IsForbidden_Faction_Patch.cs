using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new Type[] { typeof(Thing), typeof(Faction) })]
    public static class ForbidUtility_IsForbidden_Faction_Patch
    {
        public static void Postfix(Thing t, Faction faction, ref bool __result)
        {
            if (__result || t == null || !t.Spawned || t.def.category != ThingCategory.Item || faction != Faction.OfPlayer) return;

            if (t.Map.haulDestinationManager.SlotGroupParentAt(t.Position) is Building_FactoryHopper hopper && !hopper.allowTaking)
            {
                __result = true;
            }
        }
    }
}
