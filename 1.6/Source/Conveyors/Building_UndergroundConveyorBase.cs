using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace VanillaFurnitureExpandedFactory
{
    public abstract class Building_UndergroundConveyorBase : Building_Conveyor
    {
        protected Building_UndergroundConveyorBase linkedBuilding;
        protected List<UndergroundItem> undergroundQueue = new List<UndergroundItem>();
        protected int TicksPerCell => Props.ticksPerCell;
        protected float MaxDistance => Props.maxDistance;
        public bool IsLinked => linkedBuilding != null;
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                AttemptAutoLink();
            }
        }

        private void AttemptAutoLink()
        {
            var target = FindLinkTarget(Position, Rotation, MaxDistance, Map, def);
            if (target != null)
            {
                linkedBuilding = target;
                linkedBuilding.Notify_Linked(this);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref linkedBuilding, "linkedBuilding");
            Scribe_Collections.Look(ref undergroundQueue, "undergroundQueue", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                undergroundQueue ??= new List<UndergroundItem>();
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (undergroundQueue.Count > 0)
            {
                TickUndergroundQueue();
            }
        }

        protected void TickUndergroundQueue()
        {
            for (int i = undergroundQueue.Count - 1; i >= 0; i--)
            {
                UndergroundItem item = undergroundQueue[i];
                item.ticksRemaining--;

                if (item.ticksRemaining <= 0)
                {
                    ProcessArrivedItem(item, i);
                }
            }
        }

        protected abstract void ProcessArrivedItem(UndergroundItem item, int index);

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            var linked = linkedBuilding;
            if (linked != null && linked.Spawned && linked.Map == Map)
            {
                linked.ReceiveQueue(undergroundQueue);
                linked.Notify_Unlinked();
            }
            else
            {
                foreach (var item in undergroundQueue)
                {
                    GenPlace.TryPlaceThing(item.thing, Position, Map, ThingPlaceMode.Near);
                }
            }

            undergroundQueue.Clear();
            base.Destroy(mode);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
            {
                yield return g;
            }

            var linked = linkedBuilding;
            if (linked == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "VFEFactory_CommandCreateLink".Translate(),
                    defaultDesc = "VFEFactory_CommandCreateLinkDesc".Translate(),
                    icon = GraphicsCache.GizmoMakeLink,
                    action = () =>
                    {
                        Find.Targeter.BeginTargeting(new TargetingParameters
                        {
                            canTargetBuildings = true,
                            validator = (TargetInfo x) =>
                            {
                                if (!x.HasThing || !x.Cell.InBounds(Map) || x.Cell.DistanceTo(Position) > Props.maxDistance) return false;
                                return IsValidTarget(x.Thing);
                            }
                        }, (LocalTargetInfo target) =>
                        {
                            var building = target.Thing;
                            if (!IsValidTarget(building) || building.Rotation != Rotation)
                            {
                                Messages.Message("VFEFactory_CantLinkDifferentRotation".Translate(), MessageTypeDefOf.RejectInput);
                                return;
                            }

                            var linked = building as Building_UndergroundConveyorBase;
                            if (linked == this)
                            {
                                Messages.Message("VFEFactory_CantLinkSelf".Translate(), MessageTypeDefOf.RejectInput);
                                return;
                            }

                            if (linked.IsLinked)
                            {
                                Messages.Message("VFEFactory_AlreadyLinked".Translate(), MessageTypeDefOf.RejectInput);
                                return;
                            }

                            linkedBuilding = linked;
                            linked.Notify_Linked(this);
                        });
                    },
                    onHover = () =>
                    {
                        IntVec3 facing = Rotation.FacingCell;
                        for (int i = 1; i <= (int)Props.maxDistance; i++)
                        {
                            IntVec3 cell = Position + facing * i;
                            if (cell.InBounds(Map)) GenDraw.DrawTargetHighlight(cell);
                        }
                    }
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "VFEFactory_CommandBreakLink".Translate(),
                    defaultDesc = "VFEFactory_CommandBreakLinkDesc".Translate(),
                    icon = GraphicsCache.GizmoBreakLink,
                    action = () =>
                    {
                        linked.Notify_Unlinked();
                        linkedBuilding = null;
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "VFEFactory_CommandSelectLinked".Translate(),
                    defaultDesc = "VFEFactory_CommandSelectLinkedDesc".Translate(),
                    icon = GraphicsCache.GizmoSelectLinked,
                    action = () =>
                    {
                        CameraJumper.TryJumpAndSelect(linked);
                    }
                };
            }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder();
            sb.Append(base.GetInspectString());

            var linked = linkedBuilding;
            if (linked == null)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("VFEFactory_ConnectionMissing".Translate());
            }

            if (undergroundQueue.Count > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("VFEFactory_ItemsUnderground".Translate(undergroundQueue.Count));
            }

            return sb.ToString();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (linkedBuilding == null)
            {
                Vector3 drawPos = DrawPos;
                drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                Graphics.DrawMesh(MeshPool.plane10, drawPos, Quaternion.identity, GraphicsCache.OverlayNoLink, 0);
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            var linked = linkedBuilding;
            if (linked != null && linked.Spawned)
            {
                GenDraw.DrawLineBetween(this.TrueCenter(), linked.TrueCenter());
            }
        }

        public static Building_UndergroundConveyorBase FindLinkTarget(IntVec3 position, Rot4 rotation, float maxDistance, Map map, ThingDef def)
        {
            IntVec3 dir = rotation.FacingCell;

            for (int i = 1; i <= (int)maxDistance; i++)
            {
                IntVec3 c = position + dir * i;
                if (!c.InBounds(map)) break;

                var building = c.GetFirstBuilding(map) as Building_UndergroundConveyorBase;
                if (building == null) continue;
                if (building.Rotation != rotation) continue;
                if (building.IsLinked) continue;
                if (!building.IsValidLinkTarget(def)) continue;

                return building;
            }
            return null;
        }

        public virtual bool IsValidLinkTarget(ThingDef def) => false;

        protected virtual bool IsValidTarget(Thing thing) => IsValidLinkTarget(thing.def);

        public void Notify_Unlinked()
        {
            linkedBuilding = null;
        }

        public void Notify_Linked(Building_UndergroundConveyorBase building)
        {
            linkedBuilding = building;
        }

        public void ReceiveQueue(List<UndergroundItem> queue)
        {
            undergroundQueue.AddRange(queue);
        }
    }
}
