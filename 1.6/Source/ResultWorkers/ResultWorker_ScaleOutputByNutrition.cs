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
    public class ResultWorker_ScaleOutputByNutrition : ResultWorker
    {
        public override int GetCount(Process process)
        {
            ThingDef lastFoodItem = process?.GetLastStoredIngredient();
            if (lastFoodItem != null && lastFoodItem.IsIngestible)
            {
             
                return Math.Max((int)(Math.Round(lastFoodItem.GetStatValueAbstract(StatDefOf.Nutrition) / 0.05f)), 1); 
            }
            return result.count;
        }
    }
}
