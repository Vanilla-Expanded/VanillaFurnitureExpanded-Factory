using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(ResourceCounter), "UpdateResourceCounts")]
    public static class ResourceCounter_UpdateResourceCounts_Patch
    {
        public static void Postfix(Dictionary<ThingDef, int> ___countedAmounts, Map ___map)
        {
            foreach (var thing in ___map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                if (thing is Building_Conveyor conveyor)
                {
                    foreach (var carriedThing in conveyor.innerContainer)
                    {
                        Thing innerIfMinified = carriedThing.GetInnerIfMinified();
                        if (innerIfMinified.def.CountAsResource && ShouldCount(innerIfMinified))
                        {
                            ___countedAmounts[innerIfMinified.def] += innerIfMinified.stackCount;
                        }
                    }
                }
            }
        }

        private static bool ShouldCount(Thing t)
        {
            if (t.IsNotFresh())
            {
                return false;
            }
            if (t.SpawnedOrAnyParentSpawned && t.PositionHeld.Fogged(t.MapHeld))
            {
                return false;
            }
            return true;
        }
    }
}
