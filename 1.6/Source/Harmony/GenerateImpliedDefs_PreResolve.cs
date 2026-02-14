using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;

namespace VanillaFurnitureExpandedFactory
{

  /*  [HarmonyPatch(typeof(DefGenerator))]
    [HarmonyPatch("GenerateImpliedDefs_PreResolve")]
    public static class VanillaFurnitureExpandedFactory_DefGenerator_GenerateImpliedDefs_PreResolve_Patch
    {
        [HarmonyPrefix]
        public static void GenerateAutoloomProcesses(bool hotReload = false)
        {
            foreach (ThingDef item in ImpliedAutoloomProcesses(hotReload))
            {
                DefGenerator.AddImpliedDef(item, hotReload);
            }
        }


        public static IEnumerable<PipeSystem.ProcessDef> ImpliedAutoloomProcesses(bool hotReload = false)
        {
            List<ThingDef> tailoringBenchRecipes = InternalDefOf.HandTailoringBench.AllRecipes.Select(x => x.ProducedThingDef).ToList();
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

        public static PipeSystem.ProcessDef ProcessFromTailoringRecipe(AutoloomProcessTemplateDef tp, ThingDef def, int index, bool hotReload = false)
        {
            string defName = tp.defName + def.defName;
            PipeSystem.ProcessDef thingDef = (hotReload ? (DefDatabase<PipeSystem.ProcessDef>.GetNamed(defName, errorOnFail: false) ?? new PipeSystem.ProcessDef()) : new PipeSystem.ProcessDef());
            thingDef.defName = defName;
            thingDef.label = tp.label.Formatted(def.label);
            thingDef.description = tp.description.Formatted(def.label);
            thingDef.priorityInBillList = index;
            thingDef.spawnOnInteractionCell = tp.spawnOnInteractionCell;
            thingDef.autoGrabFromHoppers= tp.autoGrabFromHoppers;
            thingDef.autoInputSlots= tp.autoInputSlots;
            thingDef.ticks = (int)(def.GetStatValueAbstract(StatDefOf.WorkToMake) * 4);
            thingDef.ingredients = new List<ProcessDef.Ingre>

            thingDef.costList = new List<ThingDefCountClass> {
               new ThingDefCountClass
               {
                   thingDef=def,
                   count=1

               }

};
            ThingDef blocksToGrab = def.butcherProducts?.First()?.thingDef;

            thingDef.statBases = new List<StatModifier> {

                new StatModifier
               {
                   stat=StatDefOf.MaxHitPoints,
                   value= (blocksToGrab?.stuffProps!=null) ? 300 * blocksToGrab.stuffProps.statFactors.Where(x => x.stat == StatDefOf.MaxHitPoints).Select(x => x.value).First() : 300


               },
                 new StatModifier
               {
                   stat=StatDefOf.WorkToBuild,
                   value= (blocksToGrab?.stuffProps!=null) ? 135 * blocksToGrab.stuffProps.statFactors.Where(x => x.stat == StatDefOf.WorkToBuild).Select(x => x.value).First() : 135

               }
                 ,
                 new StatModifier
               {
                   stat=StatDefOf.Flammability,
                   value= 0

               } ,
                 new StatModifier
               {
                   stat=StatDefOf.MeditationFocusStrength,
                   value= 0.22f

               },
                 new StatModifier
               {
                   stat=StatDefOf.Beauty,
                   value= -2

               },
                 new StatModifier
               {
                   stat=StatDefOf.SellPriceFactor,
                   value= 0.7f

               }


            };











            return thingDef;
        }

    }






    */

}
