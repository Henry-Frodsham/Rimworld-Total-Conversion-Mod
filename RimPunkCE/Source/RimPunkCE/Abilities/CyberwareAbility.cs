using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimPunkCE
{
    /// <summary>
    /// Vanilla ability (same card as psycasts) granted by cyberware. Durations, cooldowns and values come from
    /// the granting implant's tier: CyberwareExtension.values keys "cooldown" and "duration" (seconds).
    /// Everything else is a normal Ability, so vanilla AI and other mods (auto-cast) can use it.
    /// </summary>
    public class CyberwareAbility : Ability
    {
        public CyberwareAbility() { }
        public CyberwareAbility(Pawn pawn) : base(pawn) { }
        public CyberwareAbility(Pawn pawn, AbilityDef def) : base(pawn, def) { }
        public CyberwareAbility(Pawn pawn, Precept sourcePrecept) : base(pawn, sourcePrecept) { }
        public CyberwareAbility(Pawn pawn, Precept sourcePrecept, AbilityDef def) : base(pawn, sourcePrecept, def) { }

        public Hediff_Cyberware Source => CyberUtility.SourceCyberwareFor(pawn, def);

        public CyberTier SourceTier => Source?.tier ?? CyberTier.T1;

        public float GetSourceValue(string key, float fallback) => Source?.GetValue(key, fallback) ?? fallback;

        public int CooldownTicksFor()
        {
            float seconds = GetSourceValue("cooldown", def.cooldownTicksRange.Average / 60f);
            return CyberUtility.SecondsToTicks(seconds * pawn.GetStatValue(RPStatDefOf.RPC_CyberwareCooldownFactor));
        }

        public int DurationTicksFor(float fallbackSeconds) => CyberUtility.SecondsToTicks(GetSourceValue("duration", fallbackSeconds));

        public override bool Activate(LocalTargetInfo target, LocalTargetInfo dest)
        {
            bool result = base.Activate(target, dest);
            if (result)
            {
                // replace vanilla's def.cooldownTicksRange cooldown with the tier + stat based one
                if (def.cooldownTicksRange.max > 0 || Source?.Ext?.HasValue("cooldown") == true)
                    StartCooldown(CooldownTicksFor());
                CyberEvents.Notify_AbilityUsed(pawn, this);
            }
            return result;
        }

        /// <summary>Instantly reduce the remaining cooldown by a fraction (Newton Module, Axolotl).</summary>
        public void ReduceCooldownFraction(float fraction)
        {
            int remaining = CooldownTicksRemaining;
            if (remaining <= 0 || fraction <= 0f) return;
            int left = Mathf.RoundToInt(remaining * (1f - Mathf.Clamp01(fraction)));
            if (left <= 0) ResetCooldown();
            else StartCooldown(left);
        }

        public override bool GizmoDisabled(out string reason)
        {
            if (base.GizmoDisabled(out reason)) return true;
            var ram = CompOfType<CompAbilityEffect_RamCost>();
            if (ram != null)
            {
                var tracker = pawn.GetCyberTracker();
                if (tracker == null || tracker.Ram < ram.Props.ramCost)
                {
                    reason = "RPC_NotEnoughRam".Translate(ram.Props.ramCost.ToString("0.#"));
                    return true;
                }
            }
            if (CompOfType<CompAbilityEffect_CyberBuff>() is CompAbilityEffect_CyberBuff buff && buff.IsActive)
            {
                reason = "RPC_AlreadyActive".Translate();
                return true;
            }
            return false;
        }
    }

    /// <summary>Grants abilities while installed. Unlike vanilla's GiveAbility, two implants granting the same
    /// ability (e.g. mantis blades on both arms) don't remove it when only one is removed.</summary>
    public class HediffCompProperties_GiveCyberAbilities : HediffCompProperties
    {
        public List<AbilityDef> abilities = new List<AbilityDef>();

        public HediffCompProperties_GiveCyberAbilities() => compClass = typeof(HediffComp_GiveCyberAbilities);
    }

    public class HediffComp_GiveCyberAbilities : HediffComp
    {
        public HediffCompProperties_GiveCyberAbilities Props => (HediffCompProperties_GiveCyberAbilities)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            var tracker = Pawn.abilities;
            if (tracker == null) return;
            foreach (var a in Props.abilities)
                if (tracker.GetAbility(a) == null) tracker.GainAbility(a);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            var tracker = Pawn.abilities;
            if (tracker == null) return;
            foreach (var a in Props.abilities)
                if (!AnyOtherSourceGrants(a)) tracker.RemoveAbility(a);
        }

        private bool AnyOtherSourceGrants(AbilityDef a)
        {
            foreach (var h in Pawn.health.hediffSet.hediffs)
            {
                if (h == parent || h is not HediffWithComps hwc) continue;
                var c = hwc.TryGetComp<HediffComp_GiveCyberAbilities>();
                if (c != null && c.Props.abilities.Contains(a)) return true;
            }
            return false;
        }
    }

    /// <summary>Ability costs RAM (cyberdeck quickhacks). Checked in CyberwareAbility.GizmoDisabled.</summary>
    public class CompProperties_AbilityRamCost : CompProperties_AbilityEffect
    {
        public float ramCost = 2f;

        public CompProperties_AbilityRamCost() => compClass = typeof(CompAbilityEffect_RamCost);
    }

    public class CompAbilityEffect_RamCost : CompAbilityEffect
    {
        public new CompProperties_AbilityRamCost Props => (CompProperties_AbilityRamCost)props;

        public override bool CanApplyOn(LocalTargetInfo target, LocalTargetInfo dest) =>
            (parent.pawn.GetCyberTracker()?.Ram ?? 0f) >= Props.ramCost;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            parent.pawn.GetCyberTracker()?.TryUseRam(Props.ramCost);
        }
    }

    /// <summary>
    /// Applies a timed Hediff_CyberActive to the caster (sandevistan, berserk). Duration is the source
    /// implant's tiered "duration" value; tier carries over so the active effect scales too.
    /// </summary>
    public class CompProperties_AbilityCyberBuff : CompProperties_AbilityEffect
    {
        public HediffDef hediffDef;
        public float defaultDurationSeconds = 8f;

        public CompProperties_AbilityCyberBuff() => compClass = typeof(CompAbilityEffect_CyberBuff);
    }

    public class CompAbilityEffect_CyberBuff : CompAbilityEffect
    {
        public new CompProperties_AbilityCyberBuff Props => (CompProperties_AbilityCyberBuff)props;

        public bool IsActive => parent.pawn.health.hediffSet.HasHediff(Props.hediffDef);

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            var pawn = parent.pawn;
            var ability = parent as CyberwareAbility;
            var source = ability?.Source;
            var ext = source?.Ext;
            int duration = ability?.DurationTicksFor(Props.defaultDurationSeconds) ?? CyberUtility.SecondsToTicks(Props.defaultDurationSeconds);

            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (existing != null) pawn.health.RemoveHediff(existing);

            var hediff = (Hediff_CyberActive)HediffMaker.MakeHediff(Props.hediffDef, pawn);
            hediff.Setup(source?.tier ?? CyberTier.T1, ext?.minTier ?? CyberTier.T1, ext?.maxTier ?? CyberTier.Iconic, duration);
            pawn.health.AddHediff(hediff);
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            // use when an enemy is close and engaged
            var pawn = parent.pawn;
            return !IsActive && pawn.mindState?.enemyTarget != null && pawn.Position.InHorDistOf(pawn.mindState.enemyTarget.Position, 12f);
        }
    }
}
