using HarmonyLib;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool))]
    public static class GenSpawn_Spawn_Patch
    {
        public static void Postfix(Thing newThing, IntVec3 loc, Map map, Thing __result)
        {
            if (__result == null || __result.def.category != ThingCategory.Item || __result is Corpse corpse && corpse.InnerPawn is null)
            {
                return;
            }

            Building_Conveyor conveyor = loc.GetFirstThing<Building_Conveyor>(map);
            if (conveyor != null && !conveyor.ejecting)
            {
                conveyor.TryAdd(__result);
            }
        }
    }
}
