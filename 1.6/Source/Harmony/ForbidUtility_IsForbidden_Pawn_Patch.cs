using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new Type[] { typeof(Thing), typeof(Pawn) })]
    public static class ForbidUtility_IsForbidden_Pawn_Patch
    {
        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (__result || t == null || !t.Spawned || t.def.category != ThingCategory.Item || pawn.Faction != Faction.OfPlayer) return;

            if (t.Map.haulDestinationManager.SlotGroupParentAt(t.Position) is Building_FactoryHopper hopper && !hopper.allowTaking)
            {
                if (pawn.CurJob != null && pawn.CurJob.playerForced && pawn.CurJob.ignoreForbidden && pawn.CurJob.AnyTargetIs(t)) return;
                __result = true;
            }
        }
    }
}
