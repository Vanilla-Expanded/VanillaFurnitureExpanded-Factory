using PipeSystem;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;


namespace VanillaFurnitureExpandedFactory
{
    public class Building_FactoryHopper : Building_Storage
    {
        private Graphic currentGraphic;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            currentGraphic = CheckForFactories();
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
