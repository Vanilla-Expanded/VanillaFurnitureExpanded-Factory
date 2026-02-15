using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;
using PipeSystem;
using System.Security.Cryptography;

namespace VanillaFurnitureExpandedFactory
{

  [HarmonyPatch(typeof(DefGenerator))]
    [HarmonyPatch("GenerateImpliedDefs_PreResolve")]
    public static class VanillaFurnitureExpandedFactory_DefGenerator_GenerateImpliedDefs_PreResolve_Patch
    {
        [HarmonyPrefix]
        public static void GenerateAutoloomProcesses(bool hotReload = false)
        {
            foreach (PipeSystem.ProcessDef item in ImpliedAutoloomProcesses(hotReload))
            {
                DefGenerator.AddImpliedDef(item, hotReload);
            }
        }


        public static IEnumerable<PipeSystem.ProcessDef> ImpliedAutoloomProcesses(bool hotReload = false)
        {
            List<ThingDef> tailoringBenchRecipes = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => x.costStuffCount!=0 
            && x.recipeMaker?.recipeUsers?.Contains(InternalDefOf.HandTailoringBench)==true).ToList();
           
            foreach (ThingDef def in tailoringBenchRecipes)
            {

                int index = 1;
                foreach (AutoloomProcessTemplateDef templateDef in DefDatabase<AutoloomProcessTemplateDef>.AllDefs)
                {
                    yield return ProcessFromTailoringRecipe(templateDef, def, index, hotReload);
                    
                    index++;
                }
            }
        }

        public static ProcessDef ProcessFromTailoringRecipe(AutoloomProcessTemplateDef tp, ThingDef def, int index, bool hotReload = false)
        {
            
            string defName = tp.defName + def.defName;
            ProcessDef processDef = (hotReload ? (DefDatabase<ProcessDef>.GetNamed(defName, errorOnFail: false) ?? new ProcessDef()) : new ProcessDef());
            processDef.defName = defName;
            processDef.label = tp.label.Formatted(def.label);
            processDef.description = tp.description.Formatted(def.label);
            processDef.priorityInBillList = index;
            processDef.spawnOnInteractionCell = tp.spawnOnInteractionCell;
            processDef.autoGrabFromHoppers= tp.autoGrabFromHoppers;
            processDef.autoInputSlots= tp.autoInputSlots;
            processDef.disallowMixing = tp.disallowMixing;

            float calculatedTicks = def.GetStatValueAbstract(StatDefOf.WorkToMake);
            if (calculatedTicks <= 0)
            {
                calculatedTicks = 3200;
            }
            processDef.ticks = (int)(calculatedTicks * 4);


            ThingCategoryDef category;
            if (def.stuffCategories?.Contains(StuffCategoryDefOf.Fabric)==true)
            {
                category = ThingCategoryDefOf.Textiles;
            }
           
            else
            {
                category = ThingCategoryDefOf.Leathers;
            }

            processDef.ingredients = new List<ProcessDef.Ingredient>
            {

                new ProcessDef.Ingredient
                {
                    thingCategory=category,
                    countNeeded=def.costStuffCount
                }

            };

            processDef.results = new List<ProcessDef.Result>
            {
                new ProcessDef.Result
                {
                    thing = def,
                    count=1
                }
            };
            processDef.isFactoryProcess = tp.isFactoryProcess;
            processDef.autoExtract= tp.autoExtract;
            processDef.onlyGrabAndOutputToFactoryHoppers = tp.onlyGrabAndOutputToFactoryHoppers;
            processDef.useFirstIngredientAsOutputStuff = tp.useFirstIngredientAsOutputStuff;
            InternalDefOf.VFEFactory_Autoloom.GetCompProperties<CompProperties_AdvancedResourceProcessor>().processes.Add(processDef);
            return processDef;
        }

    }






    

}
