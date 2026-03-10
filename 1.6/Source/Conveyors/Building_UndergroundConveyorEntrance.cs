using System.Linq;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [HotSwappable]
    public class Building_UndergroundConveyorEntrance : Building_UndergroundConveyorBase
    {
        public override bool ShowItems => false;

        protected override void Tick()
        {
            base.Tick();

            if (Spawned && innerContainer.Count > 0 && IsLinked)
            {
                AbsorbItems();
            }
        }

        private void AbsorbItems()
        {
            var linkedExit = linkedBuilding as Building_UndergroundConveyorExit;
            if (linkedExit != null && linkedExit.Spawned && linkedExit.Map == Map)
            {
                int distance = (int)Position.DistanceTo(linkedExit.Position);
                int travelTime = distance * TicksPerCell;
                int maxCapacity = Mathf.Max(1, distance) * Props.itemsPerCell;

                if (undergroundQueue.Count >= maxCapacity)
                    return;

                foreach (Thing t in innerContainer.ToList())
                {
                    if (t.Spawned) t.DeSpawn();

                    undergroundQueue.Add(new UndergroundItem
                    {
                        thing = t,
                        ticksRemaining = travelTime,
                        returning = false
                    });

                    innerContainer.Remove(t);
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
                    item.ticksRemaining = 10;
                }
            }
            else
            {
                item.returning = true;
                item.ticksRemaining = 0;
            }
        }

        public override bool IsValidLinkTarget(ThingDef def) =>
            def.thingClass == typeof(Building_UndergroundConveyorExit);
    }
}
