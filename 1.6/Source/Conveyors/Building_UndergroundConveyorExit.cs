using System.Collections.Generic;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class Building_UndergroundConveyorExit : Building_UndergroundConveyorBase
    {
        protected override void ProcessArrivedItem(UndergroundItem item, int index)
        {
            if (item.thing == null)
            {
                undergroundQueue.RemoveAt(index);
                return;
            }

            if (item.returning)
            {
                var linkedEntrance = linkedBuilding as Building_UndergroundConveyorEntrance;
                if (linkedEntrance != null && linkedEntrance.Spawned && linkedEntrance.Map == Map)
                {
                    if (GenPlace.TryPlaceThing(item.thing, linkedEntrance.Position, Map, ThingPlaceMode.Near))
                    {
                        undergroundQueue.RemoveAt(index);
                    }
                }
                else
                {
                    if (GenPlace.TryPlaceThing(item.thing, Position, Map, ThingPlaceMode.Near))
                    {
                        undergroundQueue.RemoveAt(index);
                    }
                }
            }
            else
            {
                if (TryAdd(item.thing))
                {
                    undergroundQueue.RemoveAt(index);
                }
            }
        }

        protected override bool IsValidTarget(Thing thing)
        {
            return thing is Building_UndergroundConveyorEntrance;
        }

        public bool TryAcceptIncoming(Thing t)
        {
            if (TryAdd(t))
            {
                return true;
            }
            return false;
        }

        public override IEnumerable<Rot4> PossibleInputDirections()
        {
            yield break;
        }
    }
}
