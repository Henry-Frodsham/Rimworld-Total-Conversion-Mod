using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Quickhack: the ability warmup is the upload. Upload speed scales with RPC_QuickhackUploadSpeed.
    /// While uploading, the caster's eyes glow blue (PawnRenderNodeWorker_QuickhackEyes) and the target shows
    /// an icon + progress bar (MapComponent_CyberVisuals).
    /// </summary>
    public class Verb_Quickhack : Verb_CastAbility
    {
        public override float WarmupTime => base.WarmupTime / Mathf.Max(0.1f, CasterPawn?.GetStatValue(RPStatDefOf.RPC_QuickhackUploadSpeed) ?? 1f);

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

    /// <summary>Placeholder combat quickhack: electrical damage scaled by RPC_CyberwareDamageFactor, can crit.</summary>
    public class CompProperties_AbilityQuickhackDamage : CompProperties_AbilityEffect
    {
        public DamageDef damageDef;
        public float damage = 8f;
        public float armorPenetration = 0.5f;

        public CompProperties_AbilityQuickhackDamage() => compClass = typeof(CompAbilityEffect_QuickhackDamage);
    }

    public class CompAbilityEffect_QuickhackDamage : CompAbilityEffect
    {
        public new CompProperties_AbilityQuickhackDamage Props => (CompProperties_AbilityQuickhackDamage)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            if (!target.HasThing) return;
            var caster = parent.pawn;
            float amount = Props.damage * caster.GetStatValue(RPStatDefOf.RPC_CyberwareDamageFactor);
            float crit = caster.GetStatValue(RPStatDefOf.RPC_QuickhackCritChance);
            if (crit > 0f && Rand.Chance(crit))
            {
                amount *= 2f;
                if (caster.Map != null) MoteMaker.ThrowText(target.Thing.DrawPos, caster.Map, "RPC_Crit".Translate(), Color.cyan, 2f);
            }
            var dinfo = new DamageInfo(Props.damageDef ?? RPDefOf.RPC_Electrical, amount, Props.armorPenetration, -1f, caster);
            target.Thing.TakeDamage(dinfo);
        }

        public override bool AICanTargetNow(LocalTargetInfo target) =>
            target.Thing is Pawn p && !p.Downed && p.HostileTo(parent.pawn);
    }

    /// <summary>Eyes render node that only draws while the pawn is uploading a quickhack.</summary>
    public class PawnRenderNodeWorker_QuickhackEyes : PawnRenderNodeWorker_Eye
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms) =>
            Verb_Quickhack.IsUploading(parms.pawn, out _) && base.CanDrawNow(node, parms);
    }
}
