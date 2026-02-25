using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(SteadyEnvironmentEffects), "DoCellSteadyEffects")]
    public static class SteadyEnvironmentEffects_DoCellSteadyEffects_Patch
    {
        public static void Postfix(IntVec3 c, Map ___map)
        {
            Building edifice = c.GetEdifice(___map);
            if (edifice is Building_Conveyor conveyor)
            {
                conveyor.ApplyEnvironmentalEffects();
            }
        }
    }
}
