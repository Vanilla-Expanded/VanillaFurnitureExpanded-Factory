

using System.Collections.Generic;
using RimWorld;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class RoomRoleWorker_Factory : RoomRoleWorker
    {
        public override float GetScore(Room room)
        {
            int num = 0;
            List<Thing> containedAndAdjacentThings = room.ContainedAndAdjacentThings;
            for (int i = 0; i < containedAndAdjacentThings.Count; i++)
            {
                if (containedAndAdjacentThings[i].def.building?.workTableRoomRole == InternalDefOf.VFEFactory_FactoryRoom)
                {
                    num++;
                }
            }
            return 27f * (float)num;
        }

        public override float GetScoreDeltaIfBuildingPlaced(Room room, ThingDef buildingDef)
        {
            if (buildingDef.building?.workTableRoomRole == InternalDefOf.VFEFactory_FactoryRoom)
            {
                return 27f;
            }
            return 0f;
        }
    }
}