using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace RimPunkCE
{
    /// <summary>
    /// Trauma Team operator boards their shuttle. A = shuttle, B = patient to carry aboard first (optional).
    /// </summary>
    public class JobDriver_TraumaBoardShuttle : JobDriver
    {
        private Thing Shuttle => job.GetTarget(TargetIndex.A).Thing;
        private Pawn Patient => job.GetTarget(TargetIndex.B).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            Patient == null || pawn.Reserve(Patient, job, 1, -1, null, errorOnFailed);

        public override string GetReport() =>
            Patient != null ? "RPC_TraumaCarryReport".Translate(Patient.LabelShort) : "RPC_TraumaBoardReport".Translate();

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            if (Patient != null)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            }
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var board = ToilMaker.MakeToil("BoardTraumaShuttle");
            board.initAction = () =>
            {
                var transporter = Shuttle.TryGetComp<CompTransporter>();
                if (transporter == null) return;
                var map = pawn.Map;
                if (pawn.carryTracker.CarriedThing is Pawn carried)
                {
                    pawn.carryTracker.innerContainer.Remove(carried);
                    GameComponent_RimPunk.Instance?.Notify_PatientAboard(carried, map);
                }
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
                pawn.DeSpawnOrDeselect();
                transporter.GetDirectlyHeldThings().TryAdd(pawn);
            };
            board.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return board;
        }
    }
}
