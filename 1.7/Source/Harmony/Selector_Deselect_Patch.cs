using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(Selector), "Deselect")]
    public static class Selector_Deselect_Patch
    {
        public static readonly HashSet<Thing> transferringItems = new HashSet<Thing>();

        static bool Prefix(object obj)
        {
            if (obj is Thing thing && transferringItems.Contains(thing))
            {
                return false;
            }
            return true;
        }
    }
}
