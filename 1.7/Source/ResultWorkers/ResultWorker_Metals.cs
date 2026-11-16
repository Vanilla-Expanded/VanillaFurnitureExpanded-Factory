using PipeSystem;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static PipeSystem.ProcessDef;

namespace VanillaFurnitureExpandedFactory
{
    public class ResultWorker_Metals : ResultWorker
    {
        public ThingDef cachedMetal;

        public override ThingDef GetResult(Process process)
        {
            List<ThingDef> allMetals = new List<ThingDef>();
            List<DrillableMetalsDef> allMetalLists = DefDatabase<DrillableMetalsDef>.AllDefsListForReading;
            foreach (DrillableMetalsDef individualList in allMetalLists)
            {
                allMetals.AddRange(individualList.drillableMetals);
            }

            ThingDef resultingMetal = allMetals.RandomElementByWeight(x => x.deepCommonality);

            if (resultingMetal != null)
            {
                cachedMetal = resultingMetal;
                return resultingMetal;
            }
            cachedMetal = ThingDefOf.Steel;
            return result.thing;
        }

        public override int GetCount(Process process)
        {
            if (cachedMetal != null)
            {
                return cachedMetal.deepCountPerPortion;

            }
            return result.count;
        }
    }
}
