using PipeSystem;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Noise;
using static PipeSystem.ProcessDef;

namespace VanillaFurnitureExpandedFactory
{
    public class ResultWorker_RandomFishAmount : ResultWorker
    {
        float pollutionChancePerTile = 0.066f;

        public override ThingDef GetResult(Process process)
        {
            float pollutedFishChance = 0;
            Thing building = process.advancedProcessor.parent;
            foreach (IntVec3 c in GenAdj.CellsOccupiedBy(building))
            {
                if (building.Map?.pollutionGrid.IsPolluted(c)==true)
                {
                    pollutedFishChance+= pollutionChancePerTile;
                }
                
            }
            if (Rand.Chance(pollutedFishChance))
            {
                return ThingDefOf.Fish_Toxfish;
            }
           

            return result.thing;
        }


        public override int GetCount(Process process)
        {
            
            return new IntRange(1,3).RandomInRange;
        }
    }
}
