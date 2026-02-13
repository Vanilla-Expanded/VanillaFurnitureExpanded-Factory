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
            
            List<CachedIngredient> nutritionItems = process.advancedProcessor.cachedIngredients;

            float resultingTotalNutrition = 0f;

            foreach (CachedIngredient ingredient in nutritionItems)
            {
                resultingTotalNutrition +=
                    ingredient.thingDef.GetStatValueAbstract(StatDefOf.Nutrition)
                    * ingredient.count;
            }

            if (resultingTotalNutrition != 0)
            {
                return Math.Max((int)(resultingTotalNutrition * 10), 1);

            }


            return result.count;
        }
    }
}
