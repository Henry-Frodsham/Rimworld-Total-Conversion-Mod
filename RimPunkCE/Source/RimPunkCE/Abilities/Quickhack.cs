using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Quickhack: the ability warmup is the upload. Upload speed scales with RPC_QuickhackUploadSpeed.
    /// While uploading, the caster's eyes glow blue (PawnRenderNodeWorker_QuickhackEyes) and MapComponent_Netrun draws
    /// the netrunner -> target line, the progress box and the result.
    /// </summary>
    public class Verb_Quickhack : Verb_CastAbility
    {
        // upload time comes from the quickhack's learned tier (QuickhackAbility), scaled by upload speed
        public override float WarmupTime =>
            (ability is QuickhackAbility qa ? qa.UploadSeconds : base.WarmupTime) / Mathf.Max(0.1f, CasterPawn?.GetStatValue(RPStatDefOf.RPC_QuickhackUploadSpeed) ?? 1f);

        /// <summary>The target can have stopped being a valid one during the upload: refuse it here, nothing is spent.</summary>
        protected override bool TryCastShot()
        {
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
            return stance?.verb is Verb_Quickhack && stance.StanceBusy && stance.ticksLeft > 0;
        }

        public static float UploadProgress(Stance_Busy stance)
        {
            int elapsed = Find.TickManager.TicksGame - stance.startedTick;
            int total = elapsed + stance.ticksLeft;
            return total <= 0 ? 1f : Mathf.Clamp01((float)elapsed / total);
        }
    }

    /// <summary>Eyes render node that only draws while the pawn is uploading a quickhack.</summary>
    public class PawnRenderNodeWorker_QuickhackEyes : PawnRenderNodeWorker_Eye
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            Verb_Quickhack.IsUploading(parms.pawn, out _) && base.CanDrawNow(node, parms);
    }
}
