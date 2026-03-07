using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class FishProcessTemplateDef : Def
    {
        public bool spawnOnInteractionCell;
        public bool isFactoryProcess;
        public bool autoExtract;
        public int ticks;
        public Type workerClass;
        public string outputStringOverride;
        public bool onlyGrabAndOutputToFactoryHoppers;       
        public float maxOutputCount;
        public bool hideProcessIfNotNaturalFish;
        public bool stopProcessUnderGillRot;
    }
}
