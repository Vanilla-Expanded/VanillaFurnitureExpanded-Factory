using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HotSwappable]
    [HarmonyPatch(typeof(SelectionDrawer), "DrawSelectionBracketFor")]
    public static class SelectionDrawer_DrawSelectionBracketFor_Patch
    {
        public static bool Prefix(object obj, Material overrideMat = null)
        {
            if (obj is Thing thing && thing.ParentHolder is Building_Conveyor conveyor)
            {
                var visualPos = conveyor.CalculateItemPosition();
                visualPos.y = AltitudeLayer.ItemImportant.AltitudeFor();

                var bracketLocs = AccessTools.StaticFieldRefAccess<Vector3[]>(typeof(SelectionDrawer), "bracketLocs");
                var selectTimes = AccessTools.StaticFieldRefAccess<Dictionary<object, float>>(typeof(SelectionDrawer), "selectTimes");
                SelectionDrawerUtility.CalculateSelectionBracketPositionsWorld(bracketLocs, thing, visualPos, thing.RotatedSize.ToVector2(), selectTimes, Vector2.one, 1f, thing.def.deselectedSelectionBracketFactor);

                float num = (thing.MultipleItemsPerCellDrawn() ? 0.8f : 1f);
                float num2 = 1f;
                CameraDriver cameraDriver = Find.CameraDriver;
                float num3 = Mathf.Clamp01(Mathf.InverseLerp(cameraDriver.config.sizeRange.max * 0.84999996f, cameraDriver.config.sizeRange.max, cameraDriver.ZoomRootSize));
                if (thing is Pawn)
                {
                    if (thing.def.Size == IntVec2.One)
                    {
                        num *= Mathf.Min(1f + num3 / 2f, 2f);
                    }
                    else
                    {
                        num2 = Mathf.Min(1f + num3 / 2f, 2f);
                    }
                }
                int num4 = 0;
                var selectionBracketMat = AccessTools.StaticFieldRefAccess<Material>(typeof(SelectionDrawer), "SelectionBracketMat");
                for (int i = 0; i < 4; i++)
                {
                    Quaternion q = Quaternion.AngleAxis(num4, Vector3.up);
                    Vector3 pos = (bracketLocs[i] - visualPos) * num + visualPos;
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, q, new Vector3(num, 1f, num) * num2), overrideMat ?? selectionBracketMat, 0);
                    num4 -= 90;
                }
                return false;
            }
            return true;
        }
    }
}
