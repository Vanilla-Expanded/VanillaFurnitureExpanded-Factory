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
    public class ResultWorker_Butchery : ResultWorker
    {

        public override int GetCount(Process process)
        {
            ThingDef corpse = process?.GetLastStoredIngredient();
            if (corpse != null)
            {
                return (int)Math.Max(Math.Round(corpse.GetStatValueAbstract(StatDefOf.MeatAmount)),1);
            }
            return result.count;
        }
    }
}
