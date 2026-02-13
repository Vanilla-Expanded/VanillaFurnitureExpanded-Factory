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
    public class ResultWorker_TotalNutrition_SurvivalMeal : ResultWorker
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
                /*List<ThingDef> defs = nutritionItems.Select(x => x.thingDef).ToList();
                List<int> counts = nutritionItems.Select(x => x.count).ToList();
                Log.Message(defs.ToStringSafeEnumerable());
                Log.Message(counts.ToStringSafeEnumerable());
                Log.Message(resultingTotalNutrition);
                Log.Message(Math.Max((int)(Math.Round(resultingTotalNutrition * 4 / 2.4f)), 1));*/
                return Math.Max((int)(Math.Round(resultingTotalNutrition * 4 / 2.4f)), 1);

            }
            return result.count;
        }
    }
}
