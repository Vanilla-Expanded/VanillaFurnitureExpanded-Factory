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
    public class ResultWorker_Smelt: ResultWorker
    {
        public override ThingDef GetResult(Process process)
        {
            ThingDef inputSmeltable = process?.GetLastStoredIngredient();
            ThingDef stuff = null;
            if (process?.GetStuffOfLastStoredIngredient() != null)
            {
                stuff = process?.GetStuffOfLastStoredIngredient();
               
            }
            if (stuff != null)
            {
                return stuff;
            }
            if (inputSmeltable != null && !inputSmeltable.costList.NullOrEmpty()) {

                ThingDef thingDef = inputSmeltable.costList.OrderByDescending(x => x.count).Select(x => x.thingDef).
                    Where(x => x != ThingDefOf.ComponentIndustrial && x != ThingDefOf.ComponentSpacer).FirstOrDefault();
                if (thingDef != null)
                {
                    return thingDef;
                }
            }

            return result.thing;
        }

        public override int GetCount(Process process)
        {
            ThingDef inputSmeltable = process?.GetLastStoredIngredient();
            if (inputSmeltable != null)
            {
                if (!inputSmeltable.costList.NullOrEmpty())
                {
                    int count = inputSmeltable.costList.OrderByDescending(x => x.count).Where(x => x.thingDef != ThingDefOf.ComponentIndustrial && x.thingDef != ThingDefOf.ComponentSpacer).
                        Select(x => x.count).FirstOrDefault();
                    if (count != 0)
                    {
                        return Math.Max((int)(count * 0.25f), 1);
                    }
                    
                }
                if (inputSmeltable.costStuffCount!=0)
                {
                    return Math.Max((int)(inputSmeltable.costStuffCount * 0.25f), 1);
                }
            }
            return result.count;
        }
    }
}
