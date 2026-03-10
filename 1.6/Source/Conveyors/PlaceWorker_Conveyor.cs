using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
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
    }
}
