using Verse;

namespace VanillaFurnitureExpandedFactory
{
    public class UndergroundItem : IExposable
    {
        public Thing thing;
        public int ticksRemaining;
        public bool returning;
        public void ExposeData()
        {
            Scribe_Deep.Look(ref thing, "thing");
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
            Scribe_Values.Look(ref returning, "returning", false);
        }
    }
}
