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
            MapComponent_CyberAnimations.MantisColor(pawn, out var bladeColor);
            MapComponent_CyberAnimations.Play(pawn, CyberAnimKind.DashStreak, currentTarget, bladeColor, to: cell.ToVector3Shifted());
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
            if (!pawn.Drafted)
            {
                // AI: a few slashes, then back to the gun (CE reloads it on the next shot attempt)
                job.maxNumMeleeAttacks = 2;
                job.expiryInterval = 150;
                job.checkOverrideOnExpire = true;
            }
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        /// <summary>
        /// AI dashes to an enemy inside dash range when it's built for melee, when the enemy is already close enough that
        /// a gun is a bad idea, or when the gun is useless right now (empty magazine / out of ammo / none).
        /// </summary>
        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            var caster = parent.pawn;
            if (!(target.Thing is Pawn p) || p.Downed || !p.HostileTo(caster)) return false;
            if (caster.Position.AdjacentTo8WayOrInside(p.Position)) return false;
            if (!p.Position.InHorDistOf(caster.Position, parent.verb.verbProps.range)) return false;
            return ContextualCombat.RoleOf(caster) == CombatRole.Melee || p.Position.InHorDistOf(caster.Position, 5f) || !HasUsableGun(caster);
        }

        public static bool HasUsableGun(Pawn pawn)
        {
            var gun = pawn.equipment?.Primary;
            if (gun == null || !gun.def.IsRangedWeapon) return false;
            var ammo = gun.TryGetComp<CombatExtended.CompAmmoUser>();
            if (ammo == null) return true;
            return ammo.CanBeFiredNow;
        }
    }
}
