using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace VanillaFurnitureExpandedFactory
{
    public class JobDriver_HaulFromConveyor : JobDriver
    {
        private Building_Conveyor Conveyor => job.GetTarget(TargetIndex.A).Thing as Building_Conveyor;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(Conveyor, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Conveyor.state != Building_Conveyor.ConveyorState.Waiting);

            SetFinalizerJob(delegate(JobCondition condition)
            {
                Thing carried = pawn.carryTracker.CarriedThing;
                if (carried == null) return null;
                if (condition != JobCondition.Succeeded)
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Direct, out var _);
                    return null;
                }
                return HaulAIUtility.HaulToStorageJob(pawn, carried, forced: false);
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            Toil grab = ToilMaker.MakeToil("GrabFromConveyor");
            grab.initAction = () =>
            {
                var conveyor = Conveyor;
                var items = conveyor.innerContainer.InnerListForReading;
                if (items.Count == 0) { pawn.jobs.EndCurrentJob(JobCondition.Succeeded); return; }

                Thing item = items[0];
                if (!conveyor.innerContainer.TryDrop(item, conveyor.Position, pawn.Map, ThingPlaceMode.Direct, out Thing dropped))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                pawn.carryTracker.TryStartCarry(dropped);

                if (conveyor.innerContainer.Count == 0)
                    conveyor.state = Building_Conveyor.ConveyorState.Empty;
            };
            grab.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return grab;
        }
    }
}
