using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Settings for a temporary "active" effect (sandevistan running, berserk running, ...).
    /// The tier and duration come from the implant that triggered it.
    /// </summary>
    public class ActiveEffectExtension : DefModExtension
    {
        public TieredEffects effects;

        /// <summary>Multiplies incoming damage (berserk damage reduction: 0.7 = -30%). 0 = invulnerable.</summary>
        public TieredFloat incomingDamageFactor;

        /// <summary>No body part can be taken below this fraction of its max HP while active (berserk: 0.25).</summary>
        public float partHealthFloor;

        /// <summary>Only melee attacks allowed while active (berserk).</summary>
        public bool meleeOnly;

        /// <summary>Stamina cost multiplier while active (berserk: 0).</summary>
        public float staminaCostFactor = 1f;

        /// <summary>Draw sandevistan afterimages while the pawn moves.</summary>
        public bool afterimages;

        /// <summary>Health restored per enemy neutralized while active, applied when the effect ends (0.25 = 25% of each injury).</summary>
        public float healPerNeutralizeOnEnd;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (effects != null) foreach (var e in effects.ConfigErrors("ActiveEffectExtension")) yield return e;
        }
    }

    /// <summary>A timed, tiered buff applied by a cyberware ability. Removes itself when the duration ends.</summary>
    public class Hediff_CyberActive : HediffWithComps
    {
        public CyberTier tier = CyberTier.T1;
        public CyberTier minTier = CyberTier.T1;
        public CyberTier maxTier = CyberTier.Iconic;
        public int ticksLeft = 60;
        public int neutralizedWhileActive;

        private ActiveEffectExtension extCached;
        private TieredStageCache stageCache;

        public ActiveEffectExtension Ext => extCached ??= def.GetModExtension<ActiveEffectExtension>();

        public override HediffStage CurStage
        {
            get
            {
                var ext = Ext;
                if (ext == null) return base.CurStage;
                stageCache ??= new TieredStageCache();
                return stageCache.Get(ext.effects, tier, minTier, maxTier, false, def.stages?.FirstOrFallback());
            }
        }

        public override int CurStageIndex => 0;

        public override bool ShouldRemove => ticksLeft <= 0 || base.ShouldRemove;

        public override string LabelInBrackets => (ticksLeft / 60f).ToString("0.0") + "s";

        public float IncomingDamageFactor => Ext?.incomingDamageFactor?.Evaluate(tier, minTier, maxTier) ?? 1f;

        public void Setup(CyberTier tier, CyberTier minTier, CyberTier maxTier, int durationTicks)
        {
            this.tier = tier;
            this.minTier = minTier;
            this.maxTier = maxTier;
            ticksLeft = durationTicks;
            stageCache?.Clear();
        }

        /// <summary>Extend the remaining duration by a fraction of the original (Apogee: +10% on kill).</summary>
        public void ExtendBy(int ticks) => ticksLeft += ticks;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            ticksLeft -= delta;
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            var ext = Ext;
            if (ext != null && ext.healPerNeutralizeOnEnd > 0f && neutralizedWhileActive > 0 && !pawn.Dead)
                CyberUtility.HealFraction(pawn, ext.healPerNeutralizeOnEnd * neutralizedWhileActive);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", CyberTier.T1);
            Scribe_Values.Look(ref minTier, "minTier", CyberTier.T1);
            Scribe_Values.Look(ref maxTier, "maxTier", CyberTier.Iconic);
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 60);
            Scribe_Values.Look(ref neutralizedWhileActive, "neutralizedWhileActive");
        }
    }
}
