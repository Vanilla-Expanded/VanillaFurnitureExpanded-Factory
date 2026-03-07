using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class MachiningProcessTemplateDef : Def
    {
        public bool spawnOnInteractionCell;
        public bool autoGrabFromHoppers;
        public List<IntVec3> autoInputSlots;
        public bool isFactoryProcess;
        public bool autoExtract;
        public bool onlyGrabAndOutputToFactoryHoppers;
        public bool disallowMixing;
        public bool sustainerWhenWorking;
        public SoundDef sustainerDef;
        public bool effecterWhenWorking;
        public EffecterDef effecterDef;
        public float maxOutputCount;
    }
}
