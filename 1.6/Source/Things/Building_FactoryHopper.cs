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
        public Graphic cachedGraphic;

        public Graphic graphic
        {
            get
            {
                if (cachedGraphic == null)
                {
                    cachedGraphic = CheckForFactories();
                }
                return cachedGraphic;

            }

        }

        public override void TickLong()
        {
            base.TickLong();

            cachedGraphic = CheckForFactories();
            this.Map.mapDrawer.MapMeshDirty(this.Position, MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings);
          
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

        public override Graphic Graphic => graphic;

    }
}
