using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class AutoloomProcessTemplateDef: Def
    {
		public bool spawnOnInteractionCell;
		public bool autoGrabFromHoppers;
		public List<IntVec3> autoInputSlots;
		public bool isFactoryProcess;
		public bool autoExtract;
		public bool onlyGrabAndOutputToFactoryHoppers;
		public bool useFirstIngredientAsOutputStuff;



    }
}
