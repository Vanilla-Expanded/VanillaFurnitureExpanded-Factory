using PipeSystem;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static PipeSystem.ProcessDef;

namespace VanillaFurnitureExpandedFactory
{
    public class ResultWorker_SmeltMechanoid : ResultWorker
    {
       

        public override int GetCount(Process process)
        {
            ThingDef mechCorpse = process?.GetLastStoredIngredient();
            if (mechCorpse != null)
            {        
                 return (int)(mechCorpse.race?.baseBodySize * 10);          
            }
            return result.count;
        }
    }
}
