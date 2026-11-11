using System.Linq;
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

        public virtual CyberTier SourceTier => Source?.tier ?? CyberTier.T1;

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
                RPSound.At(def.GetModExtension<AbilitySoundExtension>()?.cast, pawn);
                CyberEvents.Notify_AbilityUsed(pawn, this);
                Diag.Event("ability", $"{Diag.Describe(pawn)} used {def.defName} (tier {SourceTier}), cooldown {CooldownTicksRemaining} ticks");
                if (CompOfType<CompAbilityEffect_RamCost>() != null && SourceTier >= CyberTier.T4 && pawn.GetCyberTracker() is CompCyberTracker t)
                    t.lastHighTierQuickhackTick = Find.TickManager.TicksGame;
            }
            return result;
        }

        /// <summary>Take a flat number of ticks off the remaining cooldown (Quantum Tuner).</summary>
        public void ReduceCooldownTicks(int ticks)
        {
            int remaining = CooldownTicksRemaining;
            if (remaining <= 0 || ticks <= 0) return;
            if (remaining - ticks <= 0) ResetCooldown();
            else StartCooldown(remaining - ticks);
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

        /// <summary>
        /// Why this can't be used right now (null = usable). Checked by the gizmo and by CanCast, so the AI respects it
        /// too. Quickhack debuffs on the caster: Cyberware Malfunction blocks everything, Cripple Movement blocks
        /// movement abilities, Sonic Shock blocks quickhacks.
        /// </summary>
        public virtual string BlockReason()
        {
            if (Hediff_Quickhack.TargetHas(pawn, x => x.blocksCyberwareAbilities)) return "RPC_BlockedByMalfunction".Translate();
            if (IsMovementAbility && Hediff_Quickhack.TargetHas(pawn, x => x.blocksMovementAbilities)) return "RPC_BlockedByCripple".Translate();
            var buff = CompOfType<CompAbilityEffect_CyberBuff>();
            if (buff != null && buff.Props.adrenalineCost > 0f && pawn.GetCyberTracker()?.HasAdrenaline(buff.Props.adrenalineCost) == false)
                return "RPC_NotEnoughAdrenaline".Translate(buff.Props.adrenalineCost.ToString("0"));
            return null;
        }

        /// <summary>Dashes, jumps and Sandevistans (blocked by Cripple Movement).</summary>
        public bool IsMovementAbility =>
            def.verbProperties?.verbClass is System.Type vc && (typeof(Verb_CastAbilityJump).IsAssignableFrom(vc) || typeof(Verb_MantisDash).IsAssignableFrom(vc))
            || Source?.Ext?.osType == CyberOsType.Sandevistan;

        public override AcceptanceReport CanCast
        {
            get
            {
                var r = base.CanCast;
                if (!r.Accepted) return r;
                string block = BlockReason();
                return block == null ? AcceptanceReport.WasAccepted : new AcceptanceReport(block);
            }
        }

        public override bool GizmoDisabled(out string reason)
        {
            if (base.GizmoDisabled(out reason)) return true;
            reason = BlockReason();
            if (reason != null) return true;
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
        /// <summary>Legs: granted instead of <see cref="abilities"/> while a matching pair is installed (double jump range, charges).</summary>
        public List<AbilityDef> abilitiesWhenPaired;

        public List<AbilityDef> abilities = new List<AbilityDef>();

        public HediffCompProperties_GiveCyberAbilities() => compClass = typeof(HediffComp_GiveCyberAbilities);
    }

    public class HediffComp_GiveCyberAbilities : HediffComp
    {
        public HediffCompProperties_GiveCyberAbilities Props => (HediffCompProperties_GiveCyberAbilities)props;

        private List<AbilityDef> Wanted =>
            (parent as Hediff_Cyberware)?.effectsDoubled == true && !Props.abilitiesWhenPaired.NullOrEmpty() ? Props.abilitiesWhenPaired : Props.abilities;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            Refresh();
        }

        /// <summary>Grant the current ability set (legs: the paired version once both legs match).</summary>
        public void Refresh()
        {
            var tracker = Pawn.abilities;
            if (tracker == null) return;
            var wanted = Wanted;
            foreach (var a in Props.abilities.Concat(Props.abilitiesWhenPaired ?? new List<AbilityDef>()).ToList())
                if (!wanted.Contains(a) && tracker.GetAbility(a) != null && !AnyOtherSourceGrants(a)) tracker.RemoveAbility(a);
            foreach (var a in wanted)
                if (tracker.GetAbility(a) == null) tracker.GainAbility(a);
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            var tracker = Pawn.abilities;
            if (tracker == null) return;
            foreach (var a in Props.abilities.Concat(Props.abilitiesWhenPaired ?? new List<AbilityDef>()))
                if (!AnyOtherSourceGrants(a)) tracker.RemoveAbility(a);
        }

        private bool AnyOtherSourceGrants(AbilityDef a)
        {
            foreach (var h in Pawn.health.hediffSet.hediffs)
            {
                if (h == parent || h is not HediffWithComps hwc) continue;
                var c = hwc.TryGetComp<HediffComp_GiveCyberAbilities>();
                if (c != null && (c.Props.abilities.Contains(a) || (c.Props.abilitiesWhenPaired?.Contains(a) ?? false))) return true;
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
        /// <summary>AI casts it once its enemy is this close (Sandevistan: gunfight range; Berserk: melee range, it blocks guns).</summary>
        public float aiMaxEnemyDistance = 30f;
        /// <summary>When the AI uses it (ContextualCombat.WantsBuff).</summary>
        public CyberBuffAiRule aiRule = CyberBuffAiRule.Engaged;
        /// <summary>Adrenaline spent to activate (sandevistan, berserk). 0 = free.</summary>
        public float adrenalineCost;

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
            if (Props.adrenalineCost > 0f) pawn.GetCyberTracker()?.SpendAdrenaline(Props.adrenalineCost);
            var ext = source?.Ext;
            int duration = ability?.DurationTicksFor(Props.defaultDurationSeconds) ?? CyberUtility.SecondsToTicks(Props.defaultDurationSeconds);

            var existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (existing != null) pawn.health.RemoveHediff(existing);

            var hediff = (Hediff_CyberActive)HediffMaker.MakeHediff(Props.hediffDef, pawn);
            hediff.Setup(source?.tier ?? CyberTier.T1, ext?.minTier ?? CyberTier.T1, ext?.maxTier ?? CyberTier.Iconic, duration);
            pawn.health.AddHediff(hediff);

            if (Props.aiRule == CyberBuffAiRule.CloseDistance) BarkUtility.Say(pawn, BarkKeys.Sandevistan);
            else if (Props.aiRule == CyberBuffAiRule.Pinned)
            {
                BarkUtility.Say(pawn, BarkKeys.Berserk);
                pawn.TryGetComp<CombatExtended.CompSuppressable>()?.DebugSetSuppression(0f); // shrugs it off (see Patch_CompSuppressable_AddSuppression)
            }
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            var pawn = parent.pawn;
            var enemy = pawn.mindState?.enemyTarget;
            return !IsActive && enemy != null && enemy.Spawned && ContextualCombat.WantsBuff(pawn, Props, enemy);
        }
    }
}
