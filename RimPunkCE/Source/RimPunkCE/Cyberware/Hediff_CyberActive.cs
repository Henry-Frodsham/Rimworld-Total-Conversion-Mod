using System.Collections.Generic;
using RimWorld;
using UnityEngine;
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

        /// <summary>HP healed per second while active (Blood Pump).</summary>
        public TieredFloat healPerSecond;

        /// <summary>Neutralizing an enemy while active: extend by this fraction of the original duration, heal, regain stamina (Apogee, Falcon, Warp Dancer).</summary>
        public float onNeutralizeExtendFraction;
        public TieredFloat onNeutralizeHealFraction;
        public float onNeutralizeStamina;

        /// <summary>Kiroshi optics scans: reveal (unfog) hostile pawns / traps / turrets within these radii while active.</summary>
        public TieredFloat revealEnemies;
        public TieredFloat revealTraps;
        public TieredFloat revealTurrets;

        /// <summary>Berserk: damage bonus that grows as health drops (max at 20% health). Militech Berserk: 0.5.</summary>
        public float lowHealthDamageBonus;

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
        public int originalDuration;
        public bool doubled;

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
                return stageCache.Get(ext.effects, tier, minTier, maxTier, doubled, def.stages?.FirstOrFallback());
            }
        }

        public override int CurStageIndex => 0;

        public override bool ShouldRemove => ticksLeft <= 0 || base.ShouldRemove;

        /// <summary>Conditional effects refresh every second: no countdown for those.</summary>
        public override string LabelInBrackets => originalDuration > 0 && originalDuration <= 90 ? null : (ticksLeft / 60f).ToString("0.0") + "s";

        public float IncomingDamageFactor => Ext?.incomingDamageFactor?.Evaluate(tier, minTier, maxTier) ?? 1f;

        public void Setup(CyberTier tier, CyberTier minTier, CyberTier maxTier, int durationTicks, bool doubled = false)
        {
            this.tier = tier;
            this.minTier = minTier;
            this.maxTier = maxTier;
            this.doubled = doubled;
            ticksLeft = durationTicks;
            originalDuration = durationTicks;
            stageCache?.Clear();
            pawn?.health?.Notify_HediffChanged(this);
        }

        public float Eval(TieredFloat v, float fallback = 0f) => v == null ? fallback : v.Evaluate(tier, minTier, maxTier) * (doubled ? 2f : 1f);

        /// <summary>Called when the pawn neutralizes an enemy while this is active.</summary>
        public void Notify_Neutralized()
        {
            neutralizedWhileActive++;
            var ext = Ext;
            if (ext == null) return;
            if (ext.onNeutralizeExtendFraction > 0f) ticksLeft += Mathf.RoundToInt(originalDuration * ext.onNeutralizeExtendFraction);
            float heal = Eval(ext.onNeutralizeHealFraction);
            if (heal > 0f) CyberUtility.HealFraction(pawn, heal);
            if (ext.onNeutralizeStamina > 0f) pawn.GetCyberTracker()?.Stamina?.GainFraction(ext.onNeutralizeStamina);
        }

        /// <summary>Militech Berserk: outgoing damage factor from missing health.</summary>
        public float LowHealthDamageFactor()
        {
            var ext = Ext;
            if (ext == null || ext.lowHealthDamageBonus <= 0f) return 1f;
            float health = pawn.health.summaryHealth.SummaryHealthPercent;
            // 0 bonus at full health, full bonus at 20% health or below
            return 1f + ext.lowHealthDamageBonus * Mathf.Clamp01((1f - health) / 0.8f);
        }

        /// <summary>Extend the remaining duration by a fraction of the original (Apogee: +10% on kill).</summary>
        public void ExtendBy(int ticks) => ticksLeft += ticks;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            ticksLeft -= delta;
            var ext = Ext;
            if (ext == null || !pawn.IsHashIntervalTick(60, delta)) return;
            float hps = Eval(ext.healPerSecond);
            if (hps > 0f) CyberUtility.HealFlat(pawn, hps);
            if (ext.revealEnemies != null || ext.revealTraps != null || ext.revealTurrets != null)
                RevealUtility.Reveal(pawn, Eval(ext.revealEnemies), Eval(ext.revealTraps), Eval(ext.revealTurrets));
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            var ext = Ext;
            if (ext != null && ext.healPerNeutralizeOnEnd > 0f && neutralizedWhileActive > 0 && !pawn.Dead)
                CyberUtility.HealFraction(pawn, ext.healPerNeutralizeOnEnd * neutralizedWhileActive);
            if (!pawn.Dead) CyberEvents.Notify_ActiveEnded(pawn, def);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", CyberTier.T1);
            Scribe_Values.Look(ref minTier, "minTier", CyberTier.T1);
            Scribe_Values.Look(ref maxTier, "maxTier", CyberTier.Iconic);
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 60);
            Scribe_Values.Look(ref neutralizedWhileActive, "neutralizedWhileActive");
            Scribe_Values.Look(ref originalDuration, "originalDuration");
            Scribe_Values.Look(ref doubled, "doubled");
        }
    }
}
