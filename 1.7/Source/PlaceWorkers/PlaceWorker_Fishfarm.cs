using RimWorld;
using Verse;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace VanillaFurnitureExpandedFactory
{
    public class PlaceWorker_Fishfarm : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (IntVec3 c in GenAdj.CellsOccupiedBy(loc, rot, checkingDef.Size))
            {
                if (!map.terrainGrid.TerrainAt(c).IsWater || map.terrainGrid.TerrainAt(c).temporary)
                {
                    return new AcceptanceReport("VFEFactory_NeedsWater".Translate());
                }
                if ((map.terrainGrid.TerrainAt(c).waterBodyType != WaterBodyType.Saltwater && map.terrainGrid.TerrainAt(c).waterBodyType != WaterBodyType.Freshwater) || !map.waterBodyTracker.AnyFishPopulationAt(c))
                {
                    return new AcceptanceReport("VFEFactory_NeedsFish".Translate());
                }
            }
            ThingDef thingDef = checkingDef as ThingDef;
            List<IntVec3> list = new List<IntVec3>();

            ThingUtility.InteractionCellsWhenAt(list, thingDef, loc, rot, map,false);
            foreach (IntVec3 item in list)
            {
                if (map.terrainGrid.TerrainAt(item).IsWater)
                {
                    return new AcceptanceReport("VFEFactory_OutputOnLand".Translate());
                }

            }



            return true;
        }

      




    }


}


