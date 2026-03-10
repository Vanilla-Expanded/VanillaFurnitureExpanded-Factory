using PipeSystem;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;


namespace VanillaFurnitureExpandedFactory
{
    public class Building_FactoryHopper : Building_Storage
    {
        private Graphic currentGraphic;
        public bool allowTaking = true;
        public bool allowInserting = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowTaking, "allowTaking", true);
            Scribe_Values.Look(ref allowInserting, "allowInserting", true);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            currentGraphic = CheckForFactories();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Toggle
            {
                defaultLabel = "VFEFactory_AllowTaking".Translate(),
                defaultDesc = "VFEFactory_AllowTakingDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Gizmo/Toggle_AllowTaking"),
                isActive = () => allowTaking,
                toggleAction = () =>
                {
                    bool newValue = !allowTaking;
                    if (storageGroup != null)
                    {
                        foreach (IStorageGroupMember member in storageGroup.members)
                        {
                            if (member is Building_FactoryHopper hopper)
                            {
                                hopper.allowTaking = newValue;
                            }
                        }
                    }
                    else
                    {
                        allowTaking = newValue;
                    }
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = "VFEFactory_AllowInserting".Translate(),
                defaultDesc = "VFEFactory_AllowInsertingDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Gizmo/Toggle_AllowInserting"),
                isActive = () => allowInserting,
                toggleAction = () =>
                {
                    bool newValue = !allowInserting;
                    if (storageGroup != null)
                    {
                        foreach (IStorageGroupMember member in storageGroup.members)
                        {
                            if (member is Building_FactoryHopper hopper)
                            {
                                hopper.allowInserting = newValue;
                            }
                        }
                    }
                    else
                    {
                        allowInserting = newValue;
                    }
                }
            };
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var opt in base.GetFloatMenuOptions(selPawn))
            {
                yield return opt;
            }

            if (!allowTaking)
            {
                foreach (Thing itemToHaul in Position.GetThingList(Map))
                {
                    if (itemToHaul.def.category == ThingCategory.Item && itemToHaul.def.EverHaulable)
                    {
                        if (!StoreUtility.TryFindBestBetterStorageFor(itemToHaul, selPawn, selPawn.Map, StoragePriority.Unstored, selPawn.Faction, out var foundCell, out var dest))
                        {
                            yield return new FloatMenuOption("VFEFactory_ForceHaul".Translate() + " " + itemToHaul.Label + " (" + HaulAIUtility.NoEmptyPlaceLowerTrans + ")", null, itemToHaul, Color.white);
                            continue;
                        }

                        Job job = null;
                        if (dest is ISlotGroupParent)
                            job = HaulAIUtility.HaulToCellStorageJob(selPawn, itemToHaul, foundCell, false);
                        else if (dest is Thing destThing && destThing.TryGetInnerInteractableThingOwner() != null)
                            job = HaulAIUtility.HaulToContainerJob(selPawn, itemToHaul, destThing);

                        if (job == null)
                        {
                            yield return new FloatMenuOption("VFEFactory_ForceHaul".Translate() + " " + itemToHaul.Label + " (" + HaulAIUtility.NoEmptyPlaceLowerTrans + ")", null, itemToHaul, Color.white);
                            continue;
                        }

                        job.playerForced = true;
                        job.ignoreForbidden = true;

                        yield return new FloatMenuOption("VFEFactory_ForceHaul".Translate() + " " + itemToHaul.Label, () =>
                        {
                            selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        }, itemToHaul, Color.white);
                    }
                }
            }
        }

        public override void TickLong()
        {
            base.TickLong();

            var newGraphic = CheckForFactories();

            if (newGraphic != currentGraphic)
            {
                currentGraphic = newGraphic;
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
            }
        }

        public Graphic CheckForFactories()
        {
            if (this.Map != null)
            {
                foreach (IntVec3 edgeCell in this.OccupiedRect().ExpandedBy(1).EdgeCells)
                {
                    if (edgeCell.InBounds(this.Map))
                    {
                        List<Thing> things = edgeCell.GetThingList(this.Map);
                        foreach(Thing thing in things)
                        {
                            if (thing.def.IsEdifice())
                            {
                                CompAdvancedResourceProcessor comp = thing.TryGetComp<CompAdvancedResourceProcessor>();
                                if (comp != null)
                                {
                                    if (thing.def.hasInteractionCell) {
                                        IntVec3 pos = thing.Position + thing.def.interactionCellOffset.RotatedBy(thing.Rotation);
                                        if (pos == this.Position)
                                        {
                                            return GraphicsCache.hopperOutput;
                                        }

                                    }

                                    if(comp.Process != null)
                                    {

                                        if (!comp.Process.Def.autoInputSlots.NullOrEmpty())
                                        {
                                            foreach(IntVec3 autoslot in comp.Process.Def.autoInputSlots)
                                            {
                                                IntVec3 pos = thing.Position + autoslot.RotatedBy(thing.Rotation);
                                                if(pos == this.Position)
                                                {
                                                    return GraphicsCache.hopperInput;
                                                }
                                            }
                                            

                                        }

                                    }
                                }
                            }
                            
                        }

                    }
                }
            }


            return GraphicsCache.hopperNormal;
        }

        public override Graphic Graphic => currentGraphic ?? base.Graphic;
    }
}
