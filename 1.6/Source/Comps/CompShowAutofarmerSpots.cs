using System.Text;
using PipeSystem;
using RimWorld;
using Verse;
using Verse.Sound;

namespace VanillaFurnitureExpandedFactory
{
   
    public class CompShowAutofarmerSpots : ThingComp
    {
        public CompProperties_ShowAutofarmerSpots Props => props as CompProperties_ShowAutofarmerSpots;

        public override void PostDrawExtraSelectionOverlays()
        {

            ProcessUtility.DrawSlot(parent, new IntVec3(-3,0,-2), GraphicsCache.AutoFarmerGreenMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(-2, 0, -2), GraphicsCache.AutoFarmerYellowMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(-1, 0, -2), GraphicsCache.AutoFarmerBrownMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(0, 0, -2), GraphicsCache.AutoFarmerPurpleMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(1, 0, -2), GraphicsCache.AutoFarmerBlueMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(2, 0, -2), GraphicsCache.AutoFarmerPurpleMaterial);
            ProcessUtility.DrawSlot(parent, new IntVec3(3, 0, -2), GraphicsCache.AutoFarmerYellowMaterial);


        }
    }
}