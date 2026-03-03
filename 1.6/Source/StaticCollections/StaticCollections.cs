
using Verse;
using System;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using PipeSystem;


namespace VanillaFurnitureExpandedFactory
{
    [StaticConstructorOnStartup]
    public static class StaticCollections
    {
        public static List<ThingDef> factories = new List<ThingDef>();
        public static List<ThingDef> hoppers = new List<ThingDef>();


        static StaticCollections()
        {
            hoppers = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => x.GetModExtension<FactoryHopperExtension>()?.isfactoryHopper == true).ToList();
            factories = DefDatabase<ThingDef>.AllDefsListForReading.Where(x => x.GetCompProperties<CompProperties_AdvancedResourceProcessor>()?.notWorkingKey == "VFEFactory_FactoryNotWorking").ToList();

        }

    }
}
