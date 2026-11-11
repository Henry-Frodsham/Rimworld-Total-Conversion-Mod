using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace RimPunkCE
{
    /// <summary>
    /// Quickhack: the ability warmup is the upload. Upload speed scales with RPC_QuickhackUploadSpeed.
    /// While uploading, the caster's eyes glow blue (PawnRenderNodeWorker_QuickhackEyes) and MapComponent_Netrun draws
    /// the netrunner -> target line, the progress box and the result.
    /// </summary>
    public class Verb_Quickhack : Verb_CastAbility
    {
        private float uploadSeconds = 60f;
        private IntVec3 uploadOrigin = IntVec3.Invalid;
        public IntVec3 UploadOrigin => uploadOrigin;

        // Seconds at normal speed. Sample distance once, so moving targets cannot shorten an upload.
        public static float SecondsFor(float distance, float range, float speed = 1f) =>
            Mathf.Clamp(Mathf.Lerp(60f, 120f, Mathf.Clamp01(distance / Mathf.Max(1f, range))) / Mathf.Max(0.1f, speed), 60f, 120f);

        // Vanilla applies AimingDelayFactor to every verb. Cancel that gun-specific factor for uploads.
        public override float WarmupTime => uploadSeconds / Mathf.Max(0.01f, CasterPawn?.GetStatValue(StatDefOf.AimingDelayFactor) ?? 1f);

        public override bool TryStartCastOn(LocalTargetInfo target, LocalTargetInfo destination, bool surpriseAttack = false,
            bool canHitNonTargetPawns = true, bool preventFriendlyFire = false, bool nonInterruptingSelfCast = false)
        {
            if (!WorldFeatures.Quickhacks || CasterPawn == null || !target.IsValid) return false;
            uploadOrigin = CasterPawn.Position;
            uploadSeconds = SecondsFor(uploadOrigin.DistanceTo(target.Cell), EffectiveRange, CasterPawn.GetStatValue(RPStatDefOf.RPC_QuickhackUploadSpeed));
            CasterPawn.pather.StopDead();
            bool started = base.TryStartCastOn(target, destination, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
            if (started) RPSound.At(RPSoundDefOf.RPC_Snd_QhUploadStart, CasterPawn);
            return started;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref uploadSeconds, "rpcUploadSeconds", 60f);
            Scribe_Values.Look(ref uploadOrigin, "rpcUploadOrigin", IntVec3.Invalid);
        }

        public static void CancelUpload(Pawn pawn)
        {
            if (!IsUploading(pawn, out var stance)) return;
            UploadLoop.Stop(stance.verb as Verb_Quickhack);
            stance.verb.Reset();
            if (stance is Stance_Warmup warmup) warmup.Interrupt();
            pawn.stances.SetStance(new Stance_Mobile());
            if (pawn.CurJob?.ability is QuickhackAbility) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            Diag.Count("netrun-cancelled");
        }

        /// <summary>The target can have stopped being a valid one during the upload: refuse it here, nothing is spent.</summary>
        protected override bool TryCastShot()
        {
            UploadLoop.Stop(this);
            if (!WorldFeatures.Quickhacks || CasterPawn.Position != uploadOrigin) return false;
            if (ability is QuickhackAbility hack && currentTarget.Thing is Pawn p && !p.Dead && QuickhackUtility.InvalidReason(hack, p) != null)
            {
                MapComponent_Netrun.Notify_Result(p, NetrunResult.Immune);
                return false;
            }
            return base.TryCastShot();
        }

        public static bool IsUploading(Pawn pawn, out Stance_Busy stance)
        {
            stance = pawn?.stances?.curStance as Stance_Busy;
            return WorldFeatures.Quickhacks && stance is Stance_Warmup && stance.verb is Verb_Quickhack && stance.StanceBusy && stance.ticksLeft > 0;
        }

        public static float UploadProgress(Stance_Busy stance)
        {
            int elapsed = Find.TickManager.TicksGame - stance.startedTick;
            int total = elapsed + stance.ticksLeft;
            return total <= 0 ? 1f : Mathf.Clamp01((float)elapsed / total);
        }
    }

    [HarmonyPatch(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceTick))]
    public static class Patch_QuickhackUploadCommitment
    {
        public static bool Prefix(Stance_Warmup __instance)
        {
            if (__instance.verb is not Verb_Quickhack upload) return true;
            var pawn = upload.CasterPawn;
            if (WorldFeatures.Quickhacks && pawn?.Spawned == true && !pawn.Downed && pawn.Position == upload.UploadOrigin && !pawn.pather.Moving)
            {
                UploadLoop.Maintain(upload);
                return true;
            }
            // Cancellation never invokes WarmupComplete and therefore never applies/spends RAM on the hack.
            UploadLoop.Stop(upload);
            upload.Reset();
            __instance.Interrupt();
            pawn?.stances.SetStance(new Stance_Mobile());
            if (pawn?.CurJob?.ability is QuickhackAbility) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            return false;
        }
    }

    /// <summary>Eyes render node that only draws while the pawn is uploading a quickhack.</summary>
    public class PawnRenderNodeWorker_QuickhackEyes : PawnRenderNodeWorker_Eye
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            Verb_Quickhack.IsUploading(parms.pawn, out _) && base.CanDrawNow(node, parms);
    }
}
