using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(SteadyEnvironmentEffects), "TryDoDeteriorate")]
    public static class SteadyEnvironmentEffects_TryDoDeteriorate_Patch
    {
        public static void Postfix(Thing t, bool roofed, bool roomUsesOutdoorTemperature)
        {
            if (roofed is false && t is Building_Conveyor conveyor)
            {
                conveyor.ApplyEnvironmentalEffects(roomUsesOutdoorTemperature);
            }
        }
    }
}
