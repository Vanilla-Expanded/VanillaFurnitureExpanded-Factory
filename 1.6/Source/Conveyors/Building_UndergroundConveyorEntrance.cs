using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace VanillaFurnitureExpandedFactory
{
    [HotSwappable]
    public class Building_UndergroundConveyorEntrance : Building_UndergroundConveyorBase
    {
        public override bool ShowItems => false;

        protected override void Tick()
        {
            base.Tick();

            if (Spawned && carriedThings.Count > 0)
            {
                if (itemProgress >= 0.9f) 
                {
                    AbsorbItems();
                }
            }
        }

        private void AbsorbItems()
        {
            var linkedExit = linkedBuilding as Building_UndergroundConveyorExit;
            if (linkedExit != null && linkedExit.Spawned && linkedExit.Map == Map)
            {
                int distance = (int)Position.DistanceTo(linkedExit.Position);
                int travelTime = distance * TicksPerCell;

                foreach (Thing t in carriedThings.ToList())
                {
                    if (t.Spawned) t.DeSpawn();

                    undergroundQueue.Add(new UndergroundItem
                    {
                        thing = t,
                        ticksRemaining = travelTime,
                        returning = false
                    });

                    carriedThings.Remove(t);
                }

                itemProgress = 0f;
                state = ConveyorState.Empty;
            }
        }

        protected override void ProcessArrivedItem(UndergroundItem item, int index)
        {
            if (item.thing == null)
            {
                undergroundQueue.RemoveAt(index);
                return;
            }

            if (item.returning)
            {
                if (GenPlace.TryPlaceThing(item.thing, Position, Map, ThingPlaceMode.Near))
                {
                    undergroundQueue.RemoveAt(index);
                }
                return;
            }

            var linkedExit = linkedBuilding as Building_UndergroundConveyorExit;
            if (linkedExit != null && linkedExit.Spawned && linkedExit.Map == Map)
            {
                if (linkedExit.TryAcceptIncoming(item.thing))
                {
                    undergroundQueue.RemoveAt(index);
                }
                else
                {
                    item.returning = true;
                    int distance = (int)Position.DistanceTo(linkedExit.Position);
                    item.ticksRemaining = distance * TicksPerCell;
                }
            }
            else
            {
                item.returning = true;
                item.ticksRemaining = 0;
            }
        }

        protected override bool IsValidTarget(Thing thing)
        {
            return thing is Building_UndergroundConveyorExit;
        }
    }
}
