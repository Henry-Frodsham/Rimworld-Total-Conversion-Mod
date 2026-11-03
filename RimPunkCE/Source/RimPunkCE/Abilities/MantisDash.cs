using RimWorld;
using Verse;
using Verse.AI;

namespace RimPunkCE
{
    /// <summary>
    /// Mantis dash: the pawn zips along the ground to an enemy (a fast PawnFlyer with no height arc, not a
    /// teleport, max 10 tiles from the ability's range) and attacks on landing.
    /// </summary>
    public class Verb_MantisDash : Verb_CastAbility
    {
        private static ThingDef flyerDef;

        public static ThingDef FlyerDef => flyerDef ??= DefDatabase<ThingDef>.GetNamed("RPC_DashFlyer");

        protected override bool TryCastShot()
        {
            if (!base.TryCastShot()) return false;
            var pawn = CasterPawn;
            if (!currentTarget.HasThing || pawn?.Map == null) return false;
            if (!TryFindLandingCell(pawn, currentTarget.Thing, out var cell)) return false;
            return JumpUtility.DoJump(pawn, new LocalTargetInfo(cell), null, verbProps, ability, currentTarget, FlyerDef);
        }

        public static bool TryFindLandingCell(Pawn pawn, Thing target, out IntVec3 cell)
        {
            var map = pawn.Map;
            cell = IntVec3.Invalid;
            float best = float.MaxValue;
            foreach (var c in GenAdj.CellsAdjacent8Way(target))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                if (!GenSight.LineOfSight(pawn.Position, c, map)) continue;
                float d = c.DistanceToSquared(pawn.Position);
                if (d < best)
                {
                    best = d;
                    cell = c;
                }
            }
            return cell.IsValid;
        }

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (!base.ValidateTarget(target, showMessages)) return false;
            if (!target.HasThing || CasterPawn == null) return false;
            if (!TryFindLandingCell(CasterPawn, target.Thing, out _))
            {
                if (showMessages) Messages.Message("RPC_NoDashLanding".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return true;
        }
    }

    /// <summary>On landing: start a melee attack on the dash target.</summary>
    public class CompProperties_AbilityDashStrike : CompProperties_AbilityEffect
    {
        public CompProperties_AbilityDashStrike() => compClass = typeof(CompAbilityEffect_DashStrike);
    }

    public class CompAbilityEffect_DashStrike : CompAbilityEffect, ICompAbilityEffectOnJumpCompleted
    {
        public void OnJumpCompleted(IntVec3 origin, LocalTargetInfo target)
        {
            var pawn = parent.pawn;
            if (pawn == null || pawn.Dead || pawn.Downed || !target.HasThing || target.Thing.Destroyed) return;
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, target.Thing);
            job.playerForced = pawn.Drafted;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            target.Thing is Pawn p && !p.Downed && p.HostileTo(parent.pawn) && !parent.pawn.Position.AdjacentTo8WayOrInside(p.Position);
    }
}
