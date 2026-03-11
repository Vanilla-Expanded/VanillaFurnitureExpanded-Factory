using RimWorld;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class PlaceWorker_UndergroundConveyorLink : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            PlaceWorker_Conveyor.DrawArrow(center, rot);
            Map map = Find.CurrentMap;
            float maxDistance = def.GetModExtension<ConveyorExtension>().maxDistance;
            var target = Building_UndergroundConveyorBase.FindLinkTarget(center, rot, rot, maxDistance, map, def);
            if (target != null)
                GenDraw.DrawLineBetween(center.ToVector3Shifted(), target.TrueCenter(), SimpleColor.White);
        }
    }
}
