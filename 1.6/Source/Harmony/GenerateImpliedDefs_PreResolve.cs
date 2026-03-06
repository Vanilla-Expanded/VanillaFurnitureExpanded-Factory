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
using Verse.Noise;

namespace VanillaFurnitureExpandedFactory
{

  [HarmonyPatch(typeof(DefGenerator))]
    [HarmonyPatch("GenerateImpliedDefs_PreResolve")]
    public static class VanillaFurnitureExpandedFactory_DefGenerator_GenerateImpliedDefs_PreResolve_Patch
    {
        [HarmonyPrefix]
        public static void GenerateAutoProcesses(bool hotReload = false)
        {
            foreach (PipeSystem.ProcessDef item in ImpliedAutoloomProcesses(hotReload))
            {
                DefGenerator.AddImpliedDef(item, hotReload);
            }

            foreach (PipeSystem.ProcessDef item in ImpliedMachiningProcesses(hotReload))
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
            processDef.sustainerWhenWorking = tp.sustainerWhenWorking;
            processDef.sustainerDef = tp.sustainerDef;
            processDef.effecterWhenWorking = tp.effecterWhenWorking;
            processDef.effecterDef = tp.effecterDef;
            processDef.maxOutputCount = tp.maxOutputCount;
            InternalDefOf.VFEFactory_Autoloom.GetCompProperties<CompProperties_AdvancedResourceProcessor>().processes.Add(processDef);
            return processDef;
        }

        public static IEnumerable<PipeSystem.ProcessDef> ImpliedMachiningProcesses(bool hotReload = false)
        {
            List<ThingDef> machiningBenchRecipes = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => (x.costStuffCount != 0 || x.costList!=null)
            && x.recipeMaker?.recipeUsers?.Contains(InternalDefOf.TableMachining) == true && !x.defName.Contains("Shell")).ToList();

            foreach (ThingDef def in machiningBenchRecipes)
            {

                int index = 1;
                foreach (MachiningProcessTemplateDefs templateDef in DefDatabase<MachiningProcessTemplateDefs>.AllDefs)
                {
                    yield return ProcessFromMachiningRecipe(templateDef, def, index, hotReload);

                    index++;
                }
            }
        }
        public static ProcessDef ProcessFromMachiningRecipe(MachiningProcessTemplateDefs tp, ThingDef def, int index, bool hotReload = false)
        {

            string defName = tp.defName + def.defName;
            ProcessDef processDef = (hotReload ? (DefDatabase<ProcessDef>.GetNamed(defName, errorOnFail: false) ?? new ProcessDef()) : new ProcessDef());
            processDef.defName = defName;
            processDef.label = tp.label.Formatted(def.label);
            processDef.description = tp.description.Formatted(def.label);
            processDef.priorityInBillList = index;
            processDef.spawnOnInteractionCell = tp.spawnOnInteractionCell;
            processDef.autoGrabFromHoppers = tp.autoGrabFromHoppers;
            processDef.autoInputSlots = tp.autoInputSlots;
            processDef.disallowMixing = tp.disallowMixing;

            float calculatedTicks = def.GetStatValueAbstract(StatDefOf.WorkToMake);
            if (calculatedTicks <= 0)
            {
                calculatedTicks = 3200;
            }
            processDef.ticks = (int)(calculatedTicks * 4);


            if (def.stuffCategories?.Contains(StuffCategoryDefOf.Metallic) == true)
            {
                processDef.ingredients.Add(new ProcessDef.Ingredient
                {
                    thing = ThingDefOf.Steel,
                    countNeeded = def.costStuffCount
                });
            }

            if (def.costList != null)
            {
                List<ProcessDef.Ingredient> ingredientList = new List<ProcessDef.Ingredient>();
                foreach (var cost in def.costList) {
                    ingredientList.Add(new ProcessDef.Ingredient
                    {
                        thing = cost.thingDef,
                        countNeeded = cost.count
                    });
                }
                processDef.ingredients.AddRange(ingredientList);
            }

            ThingCategoryDef category=null;
            if (def.stuffCategories?.Contains(StuffCategoryDefOf.Fabric) == true)
            {
                category = ThingCategoryDefOf.Textiles;
            }
           
            if (category != null)
            {
                processDef.ingredients.Add(new ProcessDef.Ingredient
                {
                    thingCategory = category,
                    countNeeded = def.costStuffCount
                });
            }         

            processDef.results = new List<ProcessDef.Result>
            {
                new ProcessDef.Result
                {
                    thing = def,
                    count=1
                }
            };
            processDef.isFactoryProcess = tp.isFactoryProcess;
            processDef.autoExtract = tp.autoExtract;
            processDef.onlyGrabAndOutputToFactoryHoppers = tp.onlyGrabAndOutputToFactoryHoppers;
            processDef.useFirstIngredientAsOutputStuff = def.costStuffCount!=0;
            processDef.sustainerWhenWorking = tp.sustainerWhenWorking;
            processDef.sustainerDef = tp.sustainerDef;
            processDef.effecterWhenWorking = tp.effecterWhenWorking;
            processDef.effecterDef = tp.effecterDef;
            processDef.maxOutputCount = tp.maxOutputCount;

            if(ModLister.HasActiveModWithName("Vanilla Chemfuel Expanded") && processDef.ingredients.ContainsAny(x => x.thing==ThingDefOf.Chemfuel))
            {
                processDef.considerBuildingCompResource = true;

            }
            if (def.researchPrerequisites.Count > 0) {
                processDef.researchPrerequisites.AddRange(def.researchPrerequisites);
            }

            InternalDefOf.VFEFactory_AutomatedMachiningBay.GetCompProperties<CompProperties_AdvancedResourceProcessor>().processes.Add(processDef);
            return processDef;
        }
    }

    






}
