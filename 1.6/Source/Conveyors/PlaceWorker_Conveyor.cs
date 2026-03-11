using RimWorld;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [StaticConstructorOnStartup]
    [HotSwappable]
    public class PlaceWorker_Conveyor : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (!Building_Conveyor.IsConfigurationValid(loc, rot, map))
            {
                return new AcceptanceReport("VFEFactory_InvalidConveyorConfiguration".Translate());
            }
            return true;
        }

        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            float speed = 1f;
            float time = Time.realtimeSinceStartup;

            Vector3 startPos = center.ToVector3Shifted() - rot.FacingCell.ToVector3() * 0.5f;
            Vector3 endPos = center.ToVector3Shifted() + rot.FacingCell.ToVector3() * 0.5f;
            Vector3 dir = rot.FacingCell.ToVector3();
            dir.y = 0f;
            Quaternion arrowRot = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
            float yPos = center.ToVector3ShiftedWithAltitude(AltitudeLayer.Blueprint).y + Altitudes.AltIncVect.y;

            float progress = (time * speed) % 1f;
            Vector3 arrowPos = Vector3.Lerp(startPos, endPos, progress);
            arrowPos.y = yPos;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(arrowPos, arrowRot, new Vector3(0.6f, 1f, 0.6f)), Building_Conveyor.ArrowMat, 0);
        }
    }
}
