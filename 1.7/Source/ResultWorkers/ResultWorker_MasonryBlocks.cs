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
    public class ResultWorker_MasonryBlocks : ResultWorker
    {

        public override ThingDef GetResult(Process process)
        {
            ThingDef inputSmeltable = process?.GetLastStoredIngredient();
                      
            if (inputSmeltable != null && !inputSmeltable.butcherProducts.NullOrEmpty())
            {
                return inputSmeltable.butcherProducts.First().thingDef;               
            }

            return result.thing;
        }
    }
}
