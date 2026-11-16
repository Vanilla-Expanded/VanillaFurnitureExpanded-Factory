using System.Text;
using PipeSystem;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace VanillaFurnitureExpandedFactory
{
   
    public class CompShowAutofarmerSpots : ThingComp
    {
        public CompProperties_ShowAutofarmerSpots Props => props as CompProperties_ShowAutofarmerSpots;

        private static readonly Color InteractionCellIntensity = new Color(1f, 1f, 1f, 0.3f);

        public override void PostDrawExtraSelectionOverlays()
        {
            Graphic hopper = InternalDefOf.VFEFactory_FactoryHopper.graphic.GetColoredVersion(ShaderTypeDefOf.EdgeDetect.Shader, InteractionCellIntensity, Color.white);

            for(int i = -3; i < 4; i++)
            {
                Vector3 position = (parent.Position + (new IntVec3(i, 0, -2)).RotatedBy(parent.Rotation)).ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                position.y += 0.1f;
                hopper.DrawFromDef(position, parent.Rotation.Opposite, InternalDefOf.VFEFactory_FactoryHopper);
            }
            
            
          

        }
    }
}