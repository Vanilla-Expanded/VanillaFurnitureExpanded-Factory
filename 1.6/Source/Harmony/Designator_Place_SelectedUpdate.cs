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

    [HarmonyPatch(typeof(Designator_Place))]
    [HarmonyPatch("SelectedUpdate")]
    public static class VanillaFurnitureExpandedFactory_Designator_Place_SelectedUpdate_Patch
    {
        [HarmonyPostfix]
        public static void DrawInputOutput(Designator_Place __instance, Rot4 ___placingRot)
        {
            if (StaticCollections.factories.Contains(__instance.PlacingDef))
            {
                ThingDef factory = __instance.PlacingDef as ThingDef;
                IntVec3 intVec = UI.MouseCell();
                if (factory.hasInteractionCell)
                {
                    DrawSlot(intVec, factory.interactionCellOffset, ___placingRot, PipeSystem.GraphicsCache.OutputCellMaterial);

                }
                ProcessDef firstProcess = factory.GetCompProperties<CompProperties_AdvancedResourceProcessor>()?.processes.First();
                foreach(IntVec3 inputTile in firstProcess.autoInputSlots)
                {
                    DrawSlot(intVec, inputTile, ___placingRot, PipeSystem.GraphicsCache.InputCellMaterial);
                }

            }
        }

        public static void DrawSlot(IntVec3 center, IntVec3 interactionCellOffset, Rot4 placingRot, Material material)
        {
            Vector3 vector = (center + interactionCellOffset.RotatedBy(placingRot)).ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            vector.y += 0.1f;
            Graphics.DrawMesh(MeshPool.plane10, vector, Quaternion.identity, material, 0);
        }


    }





}
