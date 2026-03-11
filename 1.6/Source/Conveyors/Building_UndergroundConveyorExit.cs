using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class Building_UndergroundConveyorExit : Building_UndergroundConveyorBase
    {
        private static readonly Rot4[] EmptyDirections = new Rot4[0];
        
        protected override Rot4 GetLinkSearchDirection() => Rotation.Opposite;
        
        protected override bool IsValidTarget(Thing thing)
        {
            if (thing is not Building_UndergroundConveyorBase b) return false;
            if (!IsValidLinkTarget(b.def)) return false;
            if (b.Rotation != Rotation) return false;
            IntVec3 toTarget = b.Position - Position;
            IntVec3 behindDir = Rotation.Opposite.FacingCell;
            return toTarget.x * behindDir.x + toTarget.z * behindDir.z > 0;
        }
        private static readonly Rot4[] ForwardOutput = new Rot4[1];
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

        public override bool IsValidLinkTarget(ThingDef def) =>
            def.thingClass == typeof(Building_UndergroundConveyorEntrance);

        public bool TryAcceptIncoming(Thing t)
        {
            if (TryAdd(t))
            {
                return true;
            }
            return false;
        }

        public override Rot4[] PossibleInputDirections()
        {
            return EmptyDirections;
        }

        public override Rot4[] PossibleOutputDirections()
        {
            ForwardOutput[0] = Rotation;
            return ForwardOutput;
        }
    }
}
